[CmdletBinding()]
param(
    [string]$RemoteHost = "maum.in",
    [string]$RemoteUser = "service",
    [string]$RemoteRoot = "/home/service/apps/slogs",
    [string]$Domain = "slogs.dev",
    [string]$WwwDomain = "www.slogs.dev",
    [int]$AppPort = 31012,
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "linux-x64",
    [switch]$SkipPublish,
    [switch]$WasmAot,
    [switch]$NoWasmAot,
    [switch]$NativeAot,
    [switch]$ApplyCaddy,
    [switch]$NoRuntimeCaddyFallback
)

$ErrorActionPreference = "Stop"

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE"
    }
}

function Invoke-Remote {
    param([Parameter(Mandatory = $true)][string]$Command)
    Invoke-Native ssh "-o" "BatchMode=yes" "$RemoteUser@$RemoteHost" $Command
}

function Send-RemoteContent {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$RemotePath
    )

    $tempFile = New-TemporaryFile
    try {
        [System.IO.File]::WriteAllText($tempFile.FullName, $Content, [System.Text.UTF8Encoding]::new($false))
        Invoke-Native scp $tempFile.FullName "${RemoteUser}@${RemoteHost}:$RemotePath"
    }
    finally {
        Remove-Item -Force $tempFile.FullName -ErrorAction SilentlyContinue
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$dotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

$releaseId = Get-Date -Format "yyyyMMddHHmmss"
$publishRoot = Join-Path $repoRoot "artifacts\publish"
$publishDir = Join-Path $publishRoot "slogs-$RuntimeIdentifier"
$archivePath = Join-Path $publishRoot "slogs-$releaseId-$RuntimeIdentifier.tar.gz"
$remote = "$RemoteUser@$RemoteHost"
$enableWasmAot = $WasmAot.IsPresent

New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

if (-not $SkipPublish) {
    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }

    if ($NativeAot -and $RuntimeIdentifier.StartsWith("linux-") -and -not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        throw "NativeAOT publishes native code for the target runtime. For $RuntimeIdentifier, run this script on a Linux build host/container or omit -NativeAot."
    }

    $publishArguments = @(
        "publish"
        (Join-Path $repoRoot "src\Slogs\Slogs.csproj")
        "-c"
        $Configuration
        "-r"
        $RuntimeIdentifier
        "--self-contained"
        "true"
        "-p:PublishSingleFile=false"
        "-o"
        $publishDir
    )

    if ($WasmAot -and $NoWasmAot) {
        throw "Use either -WasmAot or -NoWasmAot, not both."
    }

    if ($WasmAot) {
        Write-Host "WebAssembly AOT is enabled because -WasmAot was specified."
        $enableWasmAot = $true
    }

    if ($enableWasmAot) {
        $publishArguments += "-p:SlogsWasmAot=true"
    }

    if ($NativeAot) {
        Write-Warning "NativeAOT is experimental for the current Slogs server because Blazor Server/InteractiveAuto and external authentication have NativeAOT compatibility limits."
        $publishArguments += "-p:SlogsNativeAot=true"
    }

    Write-Host "Publishing Slogs: runtime=$RuntimeIdentifier, configuration=$Configuration, wasmAot=$enableWasmAot, nativeAot=$($NativeAot.IsPresent)"
    Invoke-Native $dotnet @publishArguments
}

if (Test-Path $archivePath) {
    Remove-Item -Force $archivePath
}

Invoke-Native tar "-czf" $archivePath "-C" $publishDir "."

$remoteUid = (ssh -o BatchMode=yes $remote "id -u").Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remoteUid)) {
    throw "Failed to read remote uid."
}

$remoteGid = (ssh -o BatchMode=yes $remote "id -g").Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($remoteGid)) {
    throw "Failed to read remote gid."
}

$remoteInitTemplate = @'
set -eu
REMOTE_ROOT="__REMOTE_ROOT__"
mkdir -p "$REMOTE_ROOT/releases" "$REMOTE_ROOT/uploads" "$REMOTE_ROOT/postgres-data"
if [ ! -f "$REMOTE_ROOT/.env" ]; then
    umask 077
    if command -v openssl >/dev/null 2>&1; then
        SLOGS_DB_PASSWORD="$(openssl rand -base64 36 | tr -d '\n')"
    else
        SLOGS_DB_PASSWORD="$(date +%s%N | sha256sum | cut -d' ' -f1)"
    fi
    {
        echo "SLOGS_DB_PASSWORD=$SLOGS_DB_PASSWORD"
        echo "GOOGLE_CLIENT_ID="
        echo "GOOGLE_CLIENT_SECRET="
    } > "$REMOTE_ROOT/.env"
fi
'@
$remoteInit = $remoteInitTemplate.Replace("__REMOTE_ROOT__", $RemoteRoot)
Invoke-Remote $remoteInit

$composeTemplate = @'
services:
  postgres:
    image: postgres:16-alpine
    container_name: slogs-postgres
    restart: unless-stopped
    environment:
      POSTGRES_DB: slogs
      POSTGRES_USER: slogs
      POSTGRES_PASSWORD: ${SLOGS_DB_PASSWORD}
    volumes:
      - ./postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U slogs -d slogs"]
      interval: 10s
      timeout: 5s
      retries: 10

  app:
    image: mcr.microsoft.com/dotnet/runtime-deps:10.0
    container_name: slogs-app
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy
    user: "__REMOTE_UID__:__REMOTE_GID__"
    working_dir: /app
    command: ["/app/Slogs"]
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://0.0.0.0:8080
      ConnectionStrings__SlogsDatabase: Host=postgres;Port=5432;Database=slogs;Username=slogs;Password=${SLOGS_DB_PASSWORD}
      Authentication__Google__ClientId: ${GOOGLE_CLIENT_ID:-}
      Authentication__Google__ClientSecret: ${GOOGLE_CLIENT_SECRET:-}
      Slogs__PublicBaseUrl: https://__DOMAIN__
    ports:
      - "127.0.0.1:__APP_PORT__:8080"
    volumes:
      - ./current:/app:ro
      - ./uploads:/app/wwwroot/uploads
'@
$compose = $composeTemplate.
    Replace("__REMOTE_UID__", $remoteUid).
    Replace("__REMOTE_GID__", $remoteGid).
    Replace("__DOMAIN__", $Domain).
    Replace("__APP_PORT__", [string]$AppPort)
Send-RemoteContent $compose "$RemoteRoot/compose.yml"

$caddyBlock = @"
$Domain {
    reverse_proxy 127.0.0.1:$AppPort
}

$WwwDomain {
    redir https://$Domain{uri} permanent
}
"@
Send-RemoteContent $caddyBlock "$RemoteRoot/Caddyfile.slogs.dev"

Invoke-Native scp $archivePath "${remote}:$RemoteRoot/releases/$releaseId.tar.gz"

$deployTemplate = @'
set -eu
REMOTE_ROOT="__REMOTE_ROOT__"
RELEASE_ID="__RELEASE_ID__"
RELEASE_DIR="$REMOTE_ROOT/releases/$RELEASE_ID"
mkdir -p "$RELEASE_DIR" "$REMOTE_ROOT/uploads"
tar -xzf "$REMOTE_ROOT/releases/$RELEASE_ID.tar.gz" -C "$RELEASE_DIR"
chmod +x "$RELEASE_DIR/Slogs"
ln -sfn "$RELEASE_DIR" "$REMOTE_ROOT/current"
cd "$REMOTE_ROOT"
docker compose --env-file "$REMOTE_ROOT/.env" up -d postgres
docker compose --env-file "$REMOTE_ROOT/.env" up -d --force-recreate app
docker compose ps
find "$REMOTE_ROOT/releases" -mindepth 1 -maxdepth 1 -type d | sort -r | tail -n +6 | xargs -r rm -rf
'@
$deployCommand = $deployTemplate.
    Replace("__REMOTE_ROOT__", $RemoteRoot).
    Replace("__RELEASE_ID__", $releaseId)
Invoke-Remote $deployCommand

if ($ApplyCaddy) {
    $runtimeFallback = if ($NoRuntimeCaddyFallback) { "false" } else { "true" }
    $caddyTemplate = @'
set -eu
REMOTE_ROOT="__REMOTE_ROOT__"
DOMAIN="__DOMAIN__"
WWW_DOMAIN="__WWW_DOMAIN__"
APP_PORT="__APP_PORT__"
RUNTIME_FALLBACK="__RUNTIME_FALLBACK__"
PROPOSED="$REMOTE_ROOT/Caddyfile.proposed"
python3 - "$PROPOSED" "$DOMAIN" "$WWW_DOMAIN" "$APP_PORT" <<'PY'
import sys

out_path, domain, www_domain, app_port = sys.argv[1:5]
with open("/etc/caddy/Caddyfile", "r", encoding="utf-8") as source:
    lines = source.read().splitlines()

remove_sites = {domain, www_domain}
out = []
i = 0
while i < len(lines):
    stripped = lines[i].strip()
    is_removed_site = any(stripped == f"{site} {{" or stripped == f"{site}{{" for site in remove_sites)
    if is_removed_site:
        depth = lines[i].count("{") - lines[i].count("}")
        i += 1
        while i < len(lines) and depth > 0:
            depth += lines[i].count("{") - lines[i].count("}")
            i += 1
        while out and not out[-1].strip():
            out.pop()
        continue

    out.append(lines[i])
    i += 1

block = f"""
{domain} {{
    reverse_proxy 127.0.0.1:{app_port}
}}

{www_domain} {{
    redir https://{domain}{{uri}} permanent
}}
"""

with open(out_path, "w", encoding="utf-8") as target:
    target.write("\n".join(out).rstrip() + "\n\n" + block.strip() + "\n")
PY
caddy fmt --overwrite "$PROPOSED" >/dev/null

if sudo -n true 2>/dev/null; then
    BACKUP="$REMOTE_ROOT/Caddyfile.backup.$(date +%Y%m%d%H%M%S)"
    sudo cp /etc/caddy/Caddyfile "$BACKUP"
    sudo cp "$PROPOSED" /etc/caddy/Caddyfile
    sudo caddy validate --config /etc/caddy/Caddyfile >/dev/null
    sudo systemctl reload caddy
    echo "caddy=persistent"
elif [ -w /etc/caddy/Caddyfile ]; then
    BACKUP="$REMOTE_ROOT/Caddyfile.backup.$(date +%Y%m%d%H%M%S)"
    cp /etc/caddy/Caddyfile "$BACKUP"
    cp "$PROPOSED" /etc/caddy/Caddyfile
    caddy validate --config /etc/caddy/Caddyfile >/dev/null
    systemctl reload caddy
    echo "caddy=persistent"
else
    echo "caddy=persistent-permission-denied"
    if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
        caddy validate --config "$PROPOSED" --adapter caddyfile >/dev/null
        BACKUP_NAME="Caddyfile.backup.$(date +%Y%m%d%H%M%S)"
        docker run --rm -v /etc/caddy:/host-caddy -v "$REMOTE_ROOT:/slogs" postgres:16-alpine sh -c "cp /host-caddy/Caddyfile /slogs/$BACKUP_NAME && cp /slogs/Caddyfile.proposed /host-caddy/Caddyfile"
        echo "caddy=persistent-via-docker"
    fi

    if [ "$RUNTIME_FALLBACK" = "true" ]; then
        caddy adapt --config "$PROPOSED" --adapter caddyfile > "$REMOTE_ROOT/caddy.autoload.json"
        curl -fsS -H "Content-Type: application/json" --data-binary @"$REMOTE_ROOT/caddy.autoload.json" http://127.0.0.1:2019/load >/dev/null
        echo "caddy=runtime-loaded"
    fi
fi
'@
    $caddyCommand = $caddyTemplate.
        Replace("__REMOTE_ROOT__", $RemoteRoot).
        Replace("__DOMAIN__", $Domain).
        Replace("__WWW_DOMAIN__", $WwwDomain).
        Replace("__APP_PORT__", [string]$AppPort).
        Replace("__RUNTIME_FALLBACK__", $runtimeFallback)
    $remoteCaddyScript = "$RemoteRoot/apply-caddy-slogs.sh"
    Send-RemoteContent $caddyCommand $remoteCaddyScript
    Invoke-Remote "bash '$remoteCaddyScript'"
}

Write-Host "Deployment complete: $Domain -> 127.0.0.1:$AppPort, release $releaseId"
Write-Host "Remote root: $RemoteRoot"
Write-Host "Caddy snippet: $RemoteRoot/Caddyfile.slogs.dev"
