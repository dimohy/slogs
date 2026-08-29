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
    $normalizedCommand = $Command.Replace("`r`n", "`n").Replace("`r", "`n")
    Invoke-Native ssh "-o" "BatchMode=yes" "$RemoteUser@$RemoteHost" $normalizedCommand
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

# Production is gated by the complete local organization-memory regression suite.
# This gate intentionally runs even when a previously published artifact is reused.
Invoke-Native $dotnet "test" (Join-Path $repoRoot "Slogs.slnx") "-warnaserror"

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
        "-warnaserror"
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
    else {
        Write-Host "WebAssembly AOT is disabled by default. Specify -WasmAot only for an explicit AOT deployment."
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
mkdir -p "$REMOTE_ROOT/releases" "$REMOTE_ROOT/uploads" "$REMOTE_ROOT/postgres-data" "$REMOTE_ROOT/bge-m3-runtime" "$REMOTE_ROOT/bge-m3-data" "$REMOTE_ROOT/certificates" "$REMOTE_ROOT/data-protection" "$REMOTE_ROOT/backups"
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
if ! grep -q '^OIDC_CERT_PASSWORD=' "$REMOTE_ROOT/.env"; then
    umask 077
    printf 'OIDC_CERT_PASSWORD=%s\n' "$(openssl rand -hex 32)" >> "$REMOTE_ROOT/.env"
fi
. "$REMOTE_ROOT/.env"
for purpose in encryption signing; do
    target="$REMOTE_ROOT/certificates/openiddict-$purpose.pfx"
    if [ ! -f "$target" ]; then
        key="$REMOTE_ROOT/certificates/$purpose.key"
        crt="$REMOTE_ROOT/certificates/$purpose.crt"
        openssl req -x509 -newkey rsa:3072 -sha256 -days 825 -nodes \
            -subj "/CN=slogs-openiddict-$purpose" -keyout "$key" -out "$crt" >/dev/null 2>&1
        openssl pkcs12 -export -out "$target" -inkey "$key" -in "$crt" \
            -passout "pass:$OIDC_CERT_PASSWORD" >/dev/null 2>&1
        rm -f "$key" "$crt"
        chmod 600 "$target"
    fi
done
target="$REMOTE_ROOT/certificates/data-protection.pfx"
if [ ! -f "$target" ]; then
    key="$REMOTE_ROOT/certificates/data-protection.key"
    crt="$REMOTE_ROOT/certificates/data-protection.crt"
    openssl req -x509 -newkey rsa:3072 -sha256 -days 825 -nodes \
        -subj "/CN=slogs-data-protection" -keyout "$key" -out "$crt" >/dev/null 2>&1
    openssl pkcs12 -export -out "$target" -inkey "$key" -in "$crt" \
        -passout "pass:$OIDC_CERT_PASSWORD" >/dev/null 2>&1
    rm -f "$key" "$crt"
    chmod 600 "$target"
fi
'@
$remoteInit = $remoteInitTemplate.Replace("__REMOTE_ROOT__", $RemoteRoot)
Invoke-Remote $remoteInit

Send-RemoteContent (Get-Content (Join-Path $repoRoot "infra\bge-m3-full\Dockerfile") -Raw) "$RemoteRoot/bge-m3-runtime/Dockerfile"
Send-RemoteContent (Get-Content (Join-Path $repoRoot "infra\bge-m3-full\server.py") -Raw) "$RemoteRoot/bge-m3-runtime/server.py"

$remoteGpuCheck = @'
set -eu
if [ "$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{end}}' slogs-bge-m3 2>/dev/null || true)" = "healthy" ]; then
    echo "bge-m3=existing-healthy"
    exit 0
fi
command -v nvidia-smi >/dev/null 2>&1
nvidia-smi >/dev/null
docker run --rm --gpus=all nvidia/cuda:12.4.1-base-ubuntu22.04 nvidia-smi >/dev/null
echo "bge-m3=gpu-ready"
'@
Invoke-Remote $remoteGpuCheck

$composeTemplate = @'
services:
  bge-m3:
    image: localhost/slogs-bge-m3-full:1.0.0
    container_name: slogs-bge-m3
    restart: unless-stopped
    environment:
      BGE_M3_MODEL_PATH: /models/bge-m3
      BGE_M3_MODEL_REVISION: 5617a9f61b028005a4858fdac845db406aefb181
      BGE_M3_MAX_BATCH_SIZE: 8
      NVIDIA_VISIBLE_DEVICES: all
      NVIDIA_DRIVER_CAPABILITIES: compute,utility
    volumes:
      - ./bge-m3-data:/models
    shm_size: 1gb
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu]
    healthcheck:
      test: ["CMD", "python", "-c", "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8080/health', timeout=3).read()"]
      interval: 10s
      timeout: 5s
      retries: 90

  postgres:
    image: pgvector/pgvector:pg16
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
      bge-m3:
        condition: service_healthy
      postgres:
        condition: service_healthy
    user: "__REMOTE_UID__:__REMOTE_GID__"
    working_dir: /app
    command: ["/app/Slogs"]
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_HTTP_PORTS: 8080
      ConnectionStrings__SlogsDatabase: Host=postgres;Port=5432;Database=slogs;Username=slogs;Password=${SLOGS_DB_PASSWORD}
      Authentication__Google__ClientId: ${GOOGLE_CLIENT_ID:-}
      Authentication__Google__ClientSecret: ${GOOGLE_CLIENT_SECRET:-}
      Authentication__OpenIddict__EncryptionCertificatePath: /certificates/openiddict-encryption.pfx
      Authentication__OpenIddict__EncryptionCertificatePassword: ${OIDC_CERT_PASSWORD}
      Authentication__OpenIddict__SigningCertificatePath: /certificates/openiddict-signing.pfx
      Authentication__OpenIddict__SigningCertificatePassword: ${OIDC_CERT_PASSWORD}
      DataProtection__KeysPath: /data-protection
      DataProtection__CertificatePath: /certificates/data-protection.pfx
      DataProtection__CertificatePassword: ${OIDC_CERT_PASSWORD}
      BgeM3__BaseUrl: http://bge-m3:8080
      EmbeddingGemma__Endpoint: http://embeddinggemma:11434/api/embed
      Slogs__PublicBaseUrl: https://__DOMAIN__
    ports:
      - "127.0.0.1:__APP_PORT__:8080"
    volumes:
      - ./${SLOGS_APP_DIR:-current}:/app:ro
      - ./uploads:/app/wwwroot/uploads
      - ./certificates:/certificates:ro
      - ./data-protection:/data-protection
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
PREVIOUS_RELEASE="$(readlink -f "$REMOTE_ROOT/current" 2>/dev/null || true)"
ACTIVATED=0
APP_STOPPED=0
EMBEDDINGGEMMA_CONTAINER=""
EMBEDDINGGEMMA_WAS_RUNNING=0

for candidate in slogs-embeddinggemma-preserved slogs-embeddinggemma; do
    if docker inspect "$candidate" >/dev/null 2>&1; then
        EMBEDDINGGEMMA_CONTAINER="$candidate"
        break
    fi
done

run_migration() {
    phase="$1"
    SLOGS_APP_DIR="releases/$RELEASE_ID" docker compose --env-file "$REMOTE_ROOT/.env" run --rm --no-deps \
        -e Slogs__SkipDbInitializer=true \
        app /app/Slogs --bge-m3-migration "$phase"
}

recover_on_failure() {
    status=$?
    if [ "$status" -eq 0 ]; then
        return
    fi
    echo "Deployment failed; attempting the bounded embedding migration rollback." >&2
    if [ "$ACTIVATED" -eq 1 ]; then
        docker stop slogs-app >/dev/null 2>&1 || true
        APP_STOPPED=1
        run_migration rollback || true
    fi
    if [ -n "$PREVIOUS_RELEASE" ] && [ -d "$PREVIOUS_RELEASE" ]; then
        ln -sfn "$PREVIOUS_RELEASE" "$REMOTE_ROOT/current"
    fi
    if [ -n "$EMBEDDINGGEMMA_CONTAINER" ] && [ "$EMBEDDINGGEMMA_WAS_RUNNING" -eq 1 ]; then
        docker update --restart unless-stopped "$EMBEDDINGGEMMA_CONTAINER" >/dev/null 2>&1 || true
        docker start "$EMBEDDINGGEMMA_CONTAINER" >/dev/null 2>&1 || true
    fi
    if [ "$APP_STOPPED" -eq 1 ]; then
        SLOGS_APP_DIR=current docker compose --env-file "$REMOTE_ROOT/.env" up -d --no-deps --force-recreate app || true
    fi
    exit "$status"
}
trap recover_on_failure EXIT

mkdir -p "$RELEASE_DIR" "$REMOTE_ROOT/uploads"
tar -xzf "$REMOTE_ROOT/releases/$RELEASE_ID.tar.gz" -C "$RELEASE_DIR"
chmod +x "$RELEASE_DIR/Slogs"
if docker inspect slogs-postgres >/dev/null 2>&1; then
    docker exec slogs-postgres sh -lc 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$REMOTE_ROOT/backups/pre-$RELEASE_ID.dump"
fi
cd "$REMOTE_ROOT"

# A 6 GB GPU cannot safely serve EmbeddingGemma and run full BGE-M3
# dense+sparse+multi-vector inference at the same time. Preserve the old
# runtime and model data for rollback, but unload it before BGE-M3 starts.
if [ -n "$EMBEDDINGGEMMA_CONTAINER" ]; then
    if [ "$(docker inspect --format '{{.State.Running}}' "$EMBEDDINGGEMMA_CONTAINER")" = "true" ]; then
        EMBEDDINGGEMMA_WAS_RUNNING=1
        docker stop --time 30 "$EMBEDDINGGEMMA_CONTAINER" >/dev/null
    fi
fi

docker build -t localhost/slogs-bge-m3-full:1.0.0 "$REMOTE_ROOT/bge-m3-runtime"
docker run --rm \
    -v "$REMOTE_ROOT/bge-m3-data:/models" \
    --entrypoint python \
    localhost/slogs-bge-m3-full:1.0.0 \
    -c "from huggingface_hub import snapshot_download; snapshot_download('BAAI/bge-m3', revision='5617a9f61b028005a4858fdac845db406aefb181', local_dir='/models/bge-m3')"

docker compose --env-file "$REMOTE_ROOT/.env" up -d postgres bge-m3
ready=0
i=0
while [ "$i" -lt 180 ]; do
    health="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{end}}' slogs-bge-m3 2>/dev/null || true)"
    if [ "$health" = "healthy" ]; then
        ready=1
        break
    fi
    i=$((i + 1))
    sleep 2
done
if [ "$ready" -ne 1 ]; then
    docker logs --tail 150 slogs-bge-m3 >&2 || true
    echo "BGE-M3 runtime failed its readiness deadline." >&2
    exit 1
fi

dimensions="$(docker exec slogs-postgres psql -U slogs -d slogs -Atc \
    "SELECT a.atttypmod FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relname='LlmWikiEntryEmbeddings' AND a.attname='Embedding' AND NOT a.attisdropped;" 2>/dev/null || true)"
if [ "$dimensions" = "768" ]; then
    run_migration prepare
    docker stop slogs-app >/dev/null
    APP_STOPPED=1
    run_migration prepare
    run_migration activate
    ACTIVATED=1
elif [ -n "$dimensions" ] && [ "$dimensions" != "1024" ]; then
    echo "Unsupported active embedding dimensions: $dimensions" >&2
    exit 1
fi

ln -sfn "$RELEASE_DIR" "$REMOTE_ROOT/current"
docker compose --env-file "$REMOTE_ROOT/.env" up -d --force-recreate app
APP_STOPPED=0

ready=0
i=0
while [ "$i" -lt 60 ]; do
    if curl -fsS "http://127.0.0.1:__APP_PORT__/" >/dev/null 2>&1; then
        ready=1
        break
    fi
    i=$((i + 1))
    sleep 2
done
if [ "$ready" -ne 1 ]; then
    docker logs --tail 150 slogs-app >&2 || true
    echo "Slogs app failed its post-deployment readiness deadline." >&2
    exit 1
fi

run_migration validate
if [ "$ACTIVATED" -eq 1 ]; then
    echo "EmbeddingGemma legacy indexes are preserved for bounded rollback."
    ACTIVATED=0
fi

if [ -n "$EMBEDDINGGEMMA_CONTAINER" ]; then
    docker update --restart no "$EMBEDDINGGEMMA_CONTAINER" >/dev/null
    echo "EmbeddingGemma runtime preserved stopped as $EMBEDDINGGEMMA_CONTAINER."
fi

docker compose ps
find "$REMOTE_ROOT/releases" -mindepth 1 -maxdepth 1 -type d | sort -r | tail -n +6 | xargs -r rm -rf
trap - EXIT
'@
$deployCommand = $deployTemplate.
    Replace("__REMOTE_ROOT__", $RemoteRoot).
    Replace("__RELEASE_ID__", $releaseId).
    Replace("__APP_PORT__", [string]$AppPort)
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
