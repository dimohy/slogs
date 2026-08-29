[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [ValidateSet("all", "translations", "original")][string]$Layer = "all",
    [string]$OwnerUserName = "dimohy",
    [string]$RemoteHost = "maum.in",
    [string]$RemoteUser = "service",
    [string]$RemoteRoot = "/home/service/apps/slogs"
)

$ErrorActionPreference = "Stop"

if ($OwnerUserName -notmatch '^[A-Za-z0-9_.-]{1,80}$') {
    throw "OwnerUserName contains unsupported characters."
}
if ($RemoteRoot -notmatch '^/[A-Za-z0-9_./-]+$') {
    throw "RemoteRoot must be an absolute path containing only safe path characters."
}

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
    $normalized = $Command.Replace("`r`n", "`n").Replace("`r", "`n")
    Invoke-Native ssh "-o" "BatchMode=yes" "$RemoteUser@$RemoteHost" $normalized
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$dotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnet = "dotnet"
}

$verifyArguments = @(
    "run"
    "--project"
    (Join-Path $repoRoot "src\Slogs\Slogs.csproj")
    "--no-build"
    "--"
    "--bible-corpus-import"
    $packageRoot
    "--bible-import-checkpoints"
    (Join-Path $repoRoot "artifacts\bible-import-checkpoints")
    "--bible-owner"
    $OwnerUserName
    "--bible-import-layer"
    $Layer
    "--bible-verify-only"
)
$verifyOutput = (& $dotnet @verifyArguments 2>&1) -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw "Local Bible corpus verification failed.`n$verifyOutput"
}
Write-Host $verifyOutput

$hashMatch = [regex]::Match($verifyOutput, 'BIBLE_CORPUS_IMPORT=PASS .* hash=(?<hash>[A-F0-9]{64}) ')
if (-not $hashMatch.Success) {
    throw "Local Bible corpus verification did not return a package SHA-256."
}
$packageHash = $hashMatch.Groups["hash"].Value
$remote = "$RemoteUser@$RemoteHost"
$archiveRoot = Join-Path $repoRoot "artifacts\bible-import"
$archivePath = Join-Path $archiveRoot "bible-$packageHash.tar.gz"
New-Item -ItemType Directory -Force -Path $archiveRoot | Out-Null

try {
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    Invoke-Native tar "-czf" $archivePath "-C" $packageRoot "."

    $remotePrepareTemplate = @'
set -eu
REMOTE_ROOT="__REMOTE_ROOT__"
PACKAGE_HASH="__PACKAGE_HASH__"
mkdir -p "$REMOTE_ROOT/imports" "$REMOTE_ROOT/import-state" "$REMOTE_ROOT/backups"
'@
    Invoke-Remote ($remotePrepareTemplate.
        Replace("__REMOTE_ROOT__", $RemoteRoot).
        Replace("__PACKAGE_HASH__", $packageHash))

    Invoke-Native scp $archivePath "${remote}:$RemoteRoot/imports/bible-$packageHash.tar.gz"

    $remoteImportTemplate = @'
set -eu
REMOTE_ROOT="__REMOTE_ROOT__"
PACKAGE_HASH="__PACKAGE_HASH__"
OWNER="__OWNER__"
LAYER="__LAYER__"
ARCHIVE="$REMOTE_ROOT/imports/bible-$PACKAGE_HASH.tar.gz"
PACKAGE_DIR="$REMOTE_ROOT/imports/$PACKAGE_HASH"
if [ ! -d "$PACKAGE_DIR" ]; then
    TEMP_DIR="$REMOTE_ROOT/imports/$PACKAGE_HASH.tmp.$$"
    mkdir -p "$TEMP_DIR"
    tar -xzf "$ARCHIVE" -C "$TEMP_DIR"
    mv "$TEMP_DIR" "$PACKAGE_DIR"
fi
cd "$REMOTE_ROOT"
docker compose --env-file "$REMOTE_ROOT/.env" run --rm --no-deps app /app/Slogs \
  --bible-corpus-import "/app/imports/$PACKAGE_HASH" \
  --bible-import-checkpoints "/app/import-state/$PACKAGE_HASH" \
  --bible-owner "$OWNER" \
  --bible-import-layer "$LAYER" \
  --bible-verify-only
docker exec slogs-postgres sh -lc 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' \
  > "$REMOTE_ROOT/backups/pre-bible-$PACKAGE_HASH.dump"
docker compose --env-file "$REMOTE_ROOT/.env" run --rm --no-deps app /app/Slogs \
  --bible-corpus-import "/app/imports/$PACKAGE_HASH" \
  --bible-import-checkpoints "/app/import-state/$PACKAGE_HASH" \
  --bible-owner "$OWNER" \
  --bible-import-layer "$LAYER"
'@
    Invoke-Remote ($remoteImportTemplate.
        Replace("__REMOTE_ROOT__", $RemoteRoot).
        Replace("__PACKAGE_HASH__", $packageHash).
        Replace("__OWNER__", $OwnerUserName).
        Replace("__LAYER__", $Layer))
}
finally {
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
}
