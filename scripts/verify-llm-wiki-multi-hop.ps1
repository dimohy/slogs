[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "slogs-multihop-test-$([Guid]::NewGuid().ToString('N'))"
$password = "slogs-multihop-test"
$containerStarted = $false

try {
    & podman run --detach --rm --name $containerName `
        --publish "127.0.0.1::5432" `
        --env "POSTGRES_DB=slogs_multihop" `
        --env "POSTGRES_USER=slogs_test" `
        --env "POSTGRES_PASSWORD=$password" `
        docker.io/pgvector/pgvector:pg16 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to start disposable PostgreSQL container." }
    $containerStarted = $true

    $ready = $false
    for ($attempt = 0; $attempt -lt 60 -and -not $ready; $attempt++) {
        & podman exec $containerName pg_isready -U slogs_test -d slogs_multihop *> $null
        $ready = $LASTEXITCODE -eq 0
        if (-not $ready) { Start-Sleep -Milliseconds 250 }
    }
    if (-not $ready) { throw "Disposable PostgreSQL did not become ready." }

    $portLine = & podman port $containerName 5432/tcp
    if ($LASTEXITCODE -ne 0 -or $portLine -notmatch ':(\d+)\s*$') {
        throw "Could not resolve the disposable PostgreSQL port."
    }
    $port = [int]$Matches[1]
    $env:SLOGS_TEST_POSTGRES = "Host=127.0.0.1;Port=$port;Database=slogs_multihop;Username=slogs_test;Password=$password"

    dotnet test (Join-Path $repositoryRoot "tests/Slogs.Tests/Slogs.Tests.csproj") `
        -c Release --no-restore `
        --filter "FullyQualifiedName~LlmWikiMultiHopSearchTests"
    if ($LASTEXITCODE -ne 0) { throw "LLM Wiki multi-hop PostgreSQL integration test failed." }
}
finally {
    Remove-Item Env:SLOGS_TEST_POSTGRES -ErrorAction SilentlyContinue
    if ($containerStarted -and $containerName.StartsWith("slogs-multihop-test-", [StringComparison]::Ordinal)) {
        & podman rm --force $containerName *> $null
    }
}

Write-Host "[LLM Wiki multi-hop] PASS disposable PostgreSQL exact-depth and isolation matrix."
