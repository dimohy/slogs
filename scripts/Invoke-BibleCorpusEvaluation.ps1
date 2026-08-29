[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EvaluationPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$OwnerUserName = "dimohy",
    [ValidateRange(1, 10)][int]$Limit = 10,
    [ValidateRange(0, 3)][int]$MaxGraphHops = 1,
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
    Invoke-Native ssh "-o" "BatchMode=yes" "$RemoteUser@$RemoteHost" $Command.Replace("`r`n", "`n").Replace("`r", "`n")
}

$evaluationFile = (Resolve-Path -LiteralPath $EvaluationPath).Path
$evaluationDocument = Get-Content -Raw -LiteralPath $evaluationFile | ConvertFrom-Json -Depth 40
$evaluationHash = (Get-FileHash -LiteralPath $evaluationFile -Algorithm SHA256).Hash
if ($evaluationDocument.frozenBeforeCorrectedOriginalActivation -eq $true) {
    $lockFile = [System.IO.Path]::ChangeExtension($evaluationFile, ".lock.json")
    if (-not (Test-Path -LiteralPath $lockFile -PathType Leaf)) {
        throw "Frozen Bible evaluation lock is missing: $lockFile"
    }
    $lock = Get-Content -Raw -LiteralPath $lockFile | ConvertFrom-Json -Depth 20
    if ($lock.state -ne "frozen" -or
        $lock.frozenBeforeCorrectedOriginalActivation -ne $true -or
        $lock.holdoutSha256 -ne $evaluationHash) {
        throw "Frozen Bible evaluation lock does not match the evaluation SHA-256."
    }
}
$outputFile = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFile
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

$remote = "$RemoteUser@$RemoteHost"
$remoteEvaluation = "$RemoteRoot/imports/bible-evaluation-$evaluationHash.json"
$remoteOutput = "$RemoteRoot/import-state/bible-evaluation-$evaluationHash-result.json"
$remoteStatus = "$RemoteRoot/import-state/bible-evaluation-$evaluationHash-status.txt"

Invoke-Remote "mkdir -p '$RemoteRoot/imports' '$RemoteRoot/import-state'"
Invoke-Native scp $evaluationFile "${remote}:$remoteEvaluation"

$remoteTemplate = @'
set -u
REMOTE_ROOT="__REMOTE_ROOT__"
OWNER="__OWNER__"
LIMIT="__LIMIT__"
HOPS="__HOPS__"
HASH="__HASH__"
cd "$REMOTE_ROOT"
set +e
docker compose --env-file "$REMOTE_ROOT/.env" run --rm --no-deps app /app/Slogs \
  --bible-corpus-evaluate "/app/imports/bible-evaluation-$HASH.json" \
  --bible-evaluation-output "/app/import-state/bible-evaluation-$HASH-result.json" \
  --bible-owner "$OWNER" \
  --bible-evaluation-limit "$LIMIT" \
  --bible-evaluation-hops "$HOPS"
STATUS=$?
set -e
printf '%s\n' "$STATUS" > "$REMOTE_ROOT/import-state/bible-evaluation-$HASH-status.txt"
'@
Invoke-Remote ($remoteTemplate.
    Replace("__REMOTE_ROOT__", $RemoteRoot).
    Replace("__OWNER__", $OwnerUserName).
    Replace("__LIMIT__", $Limit.ToString([System.Globalization.CultureInfo]::InvariantCulture)).
    Replace("__HOPS__", $MaxGraphHops.ToString([System.Globalization.CultureInfo]::InvariantCulture)).
    Replace("__HASH__", $evaluationHash))

Invoke-Native scp "${remote}:$remoteOutput" $outputFile
$statusText = (& ssh "-o" "BatchMode=yes" $remote "cat '$remoteStatus'") -join "`n"
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read remote evaluation status."
}
if ($statusText.Trim() -ne "0") {
    throw "Bible corpus evaluation failed. Evidence was saved to $outputFile"
}

$result = Get-Content -Raw -LiteralPath $outputFile | ConvertFrom-Json
if (-not $result.passed) {
    throw "Bible corpus evaluation did not pass all cases. Evidence was saved to $outputFile"
}

Write-Host "BIBLE_CORPUS_LIVE_EVALUATION=PASS passed=$($result.passedCases) total=$($result.totalCases) output=$outputFile"
