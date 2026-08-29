[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BackupPath,
    [ValidateSet("Performance", "RecallSampling")][string]$Suite = "Performance",
    [ValidateRange(1, 10000)][int]$SampleCount = 1000,
    [string]$ResultPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedBackup = (Resolve-Path -LiteralPath $BackupPath).Path
$checksumPath = "$resolvedBackup.sha256"
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw "Backup checksum file is missing: $checksumPath"
}

$expectedChecksum = ((Get-Content -LiteralPath $checksumPath -Raw).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]).ToLowerInvariant()
$actualChecksum = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedBackup).Hash.ToLowerInvariant()
if ($actualChecksum -ne $expectedChecksum) {
    throw "The frozen production backup checksum does not match."
}

if ([string]::IsNullOrWhiteSpace($ResultPath)) {
    $resultName = if ($Suite -eq "RecallSampling") { "recall-sampling-1000x3.json" } else { "production-corpus-multihop.json" }
    $ResultPath = Join-Path $repoRoot "artifacts\llm-wiki\$resultName"
}
$resolvedResult = [IO.Path]::GetFullPath($ResultPath)
$container = "slogs-production-corpus-$([Guid]::NewGuid().ToString('N'))"
$password = [Guid]::NewGuid().ToString('N')

try {
    & podman run -d --name $container `
        -e POSTGRES_DB=slogs `
        -e POSTGRES_USER=slogs `
        -e "POSTGRES_PASSWORD=$password" `
        -p "127.0.0.1::5432" `
        docker.io/pgvector/pgvector:pg16 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to start the disposable production-corpus database."
    }

    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        & podman exec $container pg_isready -U slogs -d slogs | Out-Null
        if ($LASTEXITCODE -eq 0) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) {
        throw "The disposable production-corpus database did not become ready."
    }

    $portLine = & podman port $container 5432/tcp
    if ($LASTEXITCODE -ne 0 -or $portLine -notmatch ':(\d+)\s*$') {
        throw "Could not resolve the disposable production-corpus PostgreSQL port."
    }
    $port = [int]$Matches[1]

    & podman cp $resolvedBackup "${container}:/tmp/slogs.dump"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to copy the frozen backup into the disposable database."
    }
    & podman exec $container pg_restore -U slogs -d slogs --no-owner --no-privileges /tmp/slogs.dump
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restore the frozen production corpus."
    }

    $env:SLOGS_PRODUCTION_CORPUS_POSTGRES = "Host=127.0.0.1;Port=$port;Database=slogs;Username=slogs;Password=$password"
    $env:SLOGS_PRODUCTION_CORPUS_RESULT = $resolvedResult
    $env:SLOGS_RECALL_SAMPLE_COUNT = $SampleCount.ToString([Globalization.CultureInfo]::InvariantCulture)
    $filter = if ($Suite -eq "RecallSampling") { "Category=PostgreSqlRecallSampling" } else { "Category=PostgreSqlProductionCorpus" }
    $dotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"
    & $dotnet test (Join-Path $repoRoot "tests\Slogs.Tests\Slogs.Tests.csproj") `
        -c Release `
        --filter $filter `
        --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "The frozen production-corpus performance contract failed."
    }
}
finally {
    Remove-Item Env:SLOGS_PRODUCTION_CORPUS_POSTGRES -ErrorAction SilentlyContinue
    Remove-Item Env:SLOGS_PRODUCTION_CORPUS_RESULT -ErrorAction SilentlyContinue
    Remove-Item Env:SLOGS_RECALL_SAMPLE_COUNT -ErrorAction SilentlyContinue
    & podman rm -f $container 2>$null | Out-Null
}

Write-Host "[LLM Wiki production corpus] PASS $resolvedResult"
