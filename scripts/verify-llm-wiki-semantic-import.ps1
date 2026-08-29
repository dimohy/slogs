[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BackupPath,
    [Parameter(Mandatory = $true)][string]$CorpusDirectory,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [string]$Version = "semantic-verification-v1",
    [switch]$Activate,
    [switch]$RunSearchVerification
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedBackup = (Resolve-Path -LiteralPath $BackupPath).Path
$resolvedCorpus = (Resolve-Path -LiteralPath $CorpusDirectory).Path
$resolvedManifest = (Resolve-Path -LiteralPath $ManifestPath).Path
$versionPattern = '^[A-Za-z0-9._-]+$'
if ($Version -notmatch $versionPattern) { throw "Version must match $versionPattern." }
if ($RunSearchVerification -and -not $Activate) {
    throw "RunSearchVerification requires Activate so baseline and active modes can be compared."
}
$container = "slogs-semantic-import-$([Guid]::NewGuid().ToString('N'))"
$databasePassword = [Guid]::NewGuid().ToString('N')
$keyPath = Join-Path $repoRoot "artifacts\llm-wiki\semantic-import-keys\$container"

function Get-AuthoritativeHash([string]$ContainerName) {
    $entryRows = @(& podman exec $ContainerName psql -U slogs -d slogs -X -q -A -t -c 'SELECT row("Id","OwnerUserName","Slug","Title","Summary","SourcePrompt","Content","TagsJson","CategoryPath","CategoryDepth","CreatedAt","UpdatedAt","IsPublic","PublishedAt")::text FROM "LlmWikiEntries" ORDER BY "Id";')
    if ($LASTEXITCODE -ne 0) { throw "Failed to read authoritative entries." }
    $sourceRows = @(& podman exec $ContainerName psql -U slogs -d slogs -X -q -A -t -c 'SELECT row("Id","EntryId","OwnerUserName","Action","Prompt","Content","Title","Tags","CategoryPath","CreatedAt")::text FROM "LlmWikiEntrySources" ORDER BY "Id";')
    if ($LASTEXITCODE -ne 0) { throw "Failed to read authoritative sources." }
    $text = ($entryRows -join "`n") + "`n--sources--`n" + ($sourceRows -join "`n")
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}

try {
    & podman run -d --name $container -e POSTGRES_DB=slogs -e POSTGRES_USER=slogs -e "POSTGRES_PASSWORD=$databasePassword" -p "127.0.0.1::5432" docker.io/pgvector/pgvector:pg16 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to start the disposable semantic-import database." }
    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        & podman exec $container pg_isready -U slogs -d slogs | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw "The disposable semantic-import database did not become ready." }
    $portLine = & podman port $container 5432/tcp
    if ($portLine -notmatch ':(\d+)\s*$') { throw "Could not resolve the disposable PostgreSQL port." }
    $port = [int]$Matches[1]
    & podman cp $resolvedBackup "${container}:/tmp/slogs.dump"
    & podman exec $container pg_restore -U slogs -d slogs --no-owner --no-privileges /tmp/slogs.dump
    if ($LASTEXITCODE -ne 0) { throw "Failed to restore the frozen backup." }

    $beforeHash = Get-AuthoritativeHash $container
    $env:ConnectionStrings__SlogsDatabase = "Host=127.0.0.1;Port=$port;Database=slogs;Username=slogs;Password=$databasePassword"
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:DataProtection__KeysPath = $keyPath
    $env:Logging__LogLevel__Default = "Warning"
    $dotnet = Join-Path $repoRoot ".dotnet\dotnet.exe"
    $application = Join-Path $repoRoot "src\Slogs\bin\Release\net11.0\Slogs.dll"
    $importArguments = @(
        $application,
        "--llm-wiki-semantic-import", $resolvedManifest,
        "--semantic-corpus", $resolvedCorpus,
        "--semantic-version", $Version
    )
    if ($Activate) { $importArguments += "--activate-semantic-graph" }
    & $dotnet @importArguments
    if ($LASTEXITCODE -ne 0) { throw "The semantic graph importer failed." }

    $counts = & podman exec $container psql -U slogs -d slogs -X -q -A -t -F ',' -c 'SELECT (SELECT COUNT(*) FROM "LlmWikiSemanticEntities"),(SELECT COUNT(*) FROM "LlmWikiSemanticMentions"),(SELECT COUNT(*) FROM "LlmWikiSemanticRelations"),(SELECT COUNT(*) FROM "LlmWikiMemorySplitProposals"),(SELECT COUNT(*) FROM "LlmWikiSemanticGraphVersions" WHERE "State"=''validated'');'
    if ($LASTEXITCODE -ne 0) { throw "Failed to read imported semantic graph counts." }
    $activeCount = & podman exec $container psql -U slogs -d slogs -X -q -A -t -c 'SELECT COUNT(*) FROM "LlmWikiSemanticGraphVersions" WHERE "State"=''active'';'
    if ($LASTEXITCODE -ne 0) { throw "Failed to read semantic graph activation state." }
    $expectedActiveCount = if ($Activate) { 1 } else { 0 }
    if ([int]$activeCount -ne $expectedActiveCount) {
        throw "Semantic graph activation mismatch. Expected $expectedActiveCount active version(s), got $activeCount."
    }
    $afterHash = Get-AuthoritativeHash $container
    if ($beforeHash -ne $afterHash) { throw "Semantic import changed authoritative memories or Raw Provenance." }

    if ($RunSearchVerification) {
        $env:SLOGS_PRODUCTION_CORPUS_POSTGRES = $env:ConnectionStrings__SlogsDatabase
        $env:SLOGS_SEMANTIC_COMPARISON_VERSION = $Version
        $env:SLOGS_SEMANTIC_COMPARISON_RESULT = Join-Path $repoRoot "artifacts\llm-wiki\semantic-search-paired-performance.json"
        & $dotnet test (Join-Path $repoRoot "tests\Slogs.Tests\Slogs.Tests.csproj") -c Release --no-restore `
            --filter "FullyQualifiedName~LlmWikiSemanticPerformanceComparisonTests"
        if ($LASTEXITCODE -ne 0) { throw "Interleaved semantic search performance comparison failed." }

        $env:SLOGS_SEMANTIC_PRECISION_HOLDOUT = Join-Path $repoRoot "tests\Slogs.Tests\Fixtures\llm-wiki-semantic-precision-holdout.json"
        $env:SLOGS_SEMANTIC_PRECISION_RESULT = Join-Path $repoRoot "artifacts\llm-wiki\semantic-precision-holdout-result.json"
        & $dotnet test (Join-Path $repoRoot "tests\Slogs.Tests\Slogs.Tests.csproj") -c Release --no-restore `
            --filter "FullyQualifiedName~LlmWikiSemanticPrecisionTests"
        if ($LASTEXITCODE -ne 0) { throw "Semantic precision holdout verification failed." }
    }

    & $dotnet $application --llm-wiki-semantic-import $resolvedManifest --semantic-corpus $resolvedCorpus --semantic-version $Version 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { throw "Duplicate semantic graph version did not fail closed." }
}
finally {
    Remove-Item Env:ConnectionStrings__SlogsDatabase,Env:ASPNETCORE_ENVIRONMENT,Env:DataProtection__KeysPath,Env:Logging__LogLevel__Default,Env:SLOGS_PRODUCTION_CORPUS_POSTGRES,Env:SLOGS_PRODUCTION_CORPUS_RESULT,Env:SLOGS_SEMANTIC_COMPARISON_VERSION,Env:SLOGS_SEMANTIC_COMPARISON_RESULT,Env:SLOGS_SEMANTIC_PRECISION_HOLDOUT,Env:SLOGS_SEMANTIC_PRECISION_RESULT -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $keyPath -PathType Container) { Remove-Item -LiteralPath $keyPath -Recurse -Force }
    & podman rm -f $container 2>$null | Out-Null
}

Write-Host "[LLM Wiki semantic import] PASS counts=$counts authoritativeHash=$afterHash"
