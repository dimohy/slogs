[CmdletBinding()]
param(
    [string]$RemoteHost = "maum.in",
    [string]$RemoteUser = "service",
    [ValidateSet("Snapshot", "Capture", "Verify")]
    [string]$Mode = "Snapshot",
    [string]$BaselinePath,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

if ($Mode -in @("Capture", "Verify") -and [string]::IsNullOrWhiteSpace($BaselinePath)) {
    throw "BaselinePath is required for $Mode mode."
}

$remotePython = @'
import concurrent.futures
import datetime
import json
import re
import subprocess
from pathlib import Path


def run(command, timeout=10):
    return subprocess.run(command, capture_output=True, text=True, timeout=timeout)


def probe(target):
    url = target if target.startswith(("http://", "https://")) else "http://" + target
    try:
        result = run([
            "curl", "-ksS", "--max-time", "3", "-o", "/dev/null",
            "-w", "%{http_code}", url
        ], timeout=5)
        code = result.stdout.strip() or "000"
        responsive = result.returncode == 0 and code != "000"
        return {
            "target": target,
            "url": url,
            "responsive": responsive,
            "httpStatus": code,
            "exitCode": result.returncode,
        }
    except subprocess.TimeoutExpired:
        return {
            "target": target,
            "url": url,
            "responsive": False,
            "httpStatus": "000",
            "exitCode": 124,
        }


caddy_text = Path("/etc/caddy/Caddyfile").read_text(encoding="utf-8")
targets = []
for line in caddy_text.splitlines():
    stripped = line.strip()
    if not stripped or stripped.startswith("#"):
        continue
    match = re.match(r"reverse_proxy\s+([^\s{]+)", stripped)
    if match:
        targets.append(match.group(1))
targets = sorted(set(targets))

with concurrent.futures.ThreadPoolExecutor(max_workers=min(12, max(1, len(targets)))) as pool:
    upstreams = sorted(pool.map(probe, targets), key=lambda item: item["target"])

caddy_state = run(["systemctl", "is-active", "caddy"])
caddy_active = caddy_state.returncode == 0 and caddy_state.stdout.strip() == "active"

container_names_result = run(["docker", "ps", "-a", "--format", "{{.Names}}"])
container_names = [name for name in container_names_result.stdout.splitlines() if name]
containers = []
for name in sorted(container_names):
    inspect = run(["docker", "inspect", name])
    if inspect.returncode != 0:
        continue
    data = json.loads(inspect.stdout)[0]
    state = data.get("State", {})
    health = (state.get("Health") or {}).get("Status", "none")
    restart_policy = (data.get("HostConfig", {}).get("RestartPolicy") or {}).get("Name", "no") or "no"
    containers.append({
        "name": name,
        "running": bool(state.get("Running")),
        "health": health,
        "restartPolicy": restart_policy,
    })

snapshot = {
    "schemaVersion": "slogs-server-continuity.v1",
    "capturedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "host": subprocess.run(["hostname"], capture_output=True, text=True).stdout.strip(),
    "caddyActive": caddy_active,
    "upstreams": upstreams,
    "containers": containers,
    "responsiveUpstreams": [item["target"] for item in upstreams if item["responsive"]],
    "restartManagedContainers": [
        item["name"] for item in containers
        if item["running"] and item["restartPolicy"] != "no"
    ],
}
print(json.dumps(snapshot, ensure_ascii=False, indent=2))
'@

$raw = ($remotePython | & ssh -o BatchMode=yes "$RemoteUser@$RemoteHost" "python3 -") -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect server continuity state on $RemoteHost."
}

$snapshot = $raw | ConvertFrom-Json -Depth 20
$failures = [System.Collections.Generic.List[string]]::new()

if (-not $snapshot.caddyActive) {
    $failures.Add("Caddy is not active.")
}

$containerByName = @{}
foreach ($container in $snapshot.containers) {
    $containerByName[$container.name] = $container
}

function Require-Container {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$RequireHealthy
    )

    $container = $containerByName[$Name]
    if ($null -eq $container) {
        $failures.Add("Required container is missing: $Name")
        return
    }
    if (-not $container.running) {
        $failures.Add("Required container is not running: $Name")
    }
    if ($RequireHealthy -and $container.health -ne "healthy") {
        $failures.Add("Required container is not healthy: $Name ($($container.health))")
    }
}

Require-Container -Name "slogs-app"
Require-Container -Name "slogs-postgres" -RequireHealthy
Require-Container -Name "slogs-bge-m3" -RequireHealthy
Require-Container -Name "palworld-dedicated-server" -RequireHealthy

$palworld = $containerByName["palworld-dedicated-server"]
if ($null -ne $palworld -and $palworld.restartPolicy -eq "no") {
    $failures.Add("Palworld is not configured to restart after a server reboot.")
}

$embedding = $containerByName["slogs-embeddinggemma-preserved"]
if ($null -eq $embedding) {
    $failures.Add("Preserved EmbeddingGemma container is missing.")
}
else {
    if ($embedding.running) {
        $failures.Add("Preserved EmbeddingGemma must remain stopped while BGE-M3 owns the 6 GB GPU.")
    }
    if ($embedding.restartPolicy -ne "no") {
        $failures.Add("Preserved EmbeddingGemma must use restart policy 'no'.")
    }
}

if ($Mode -eq "Capture") {
    $baselineDirectory = Split-Path -Parent $BaselinePath
    if ($baselineDirectory) {
        New-Item -ItemType Directory -Force -Path $baselineDirectory | Out-Null
    }
    $raw | Set-Content -LiteralPath $BaselinePath -Encoding utf8
}
elseif ($Mode -eq "Verify") {
    if (-not (Test-Path -LiteralPath $BaselinePath -PathType Leaf)) {
        throw "Continuity baseline does not exist: $BaselinePath"
    }
    $baseline = Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json -Depth 20
    $upstreamByTarget = @{}
    foreach ($upstream in $snapshot.upstreams) {
        $upstreamByTarget[$upstream.target] = $upstream
    }
    foreach ($target in $baseline.responsiveUpstreams) {
        $current = $upstreamByTarget[$target]
        if ($null -eq $current -or -not $current.responsive) {
            $failures.Add("Previously responsive Caddy upstream is unavailable: $target")
        }
    }
    foreach ($name in $baseline.restartManagedContainers) {
        $current = $containerByName[$name]
        if ($null -eq $current -or -not $current.running) {
            $failures.Add("Previously running restart-managed container is unavailable: $name")
        }
    }
}

$result = [ordered]@{
    schemaVersion = "slogs-server-continuity-result.v1"
    mode = $Mode
    passed = $failures.Count -eq 0
    failures = @($failures)
    snapshot = $snapshot
}
$resultJson = $result | ConvertTo-Json -Depth 20

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputDirectory = Split-Path -Parent $OutputPath
    if ($outputDirectory) {
        New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
    }
    $resultJson | Set-Content -LiteralPath $OutputPath -Encoding utf8
}

$resultJson
if ($failures.Count -gt 0) {
    throw "Server continuity verification failed: $($failures -join '; ')"
}
