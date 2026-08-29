[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BackupPath,
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedBackup = (Resolve-Path -LiteralPath $BackupPath).Path
$checksumPath = "$resolvedBackup.sha256"
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw "Backup checksum file is missing: $checksumPath"
}
$expectedBackupHash = ((Get-Content -LiteralPath $checksumPath -Raw).Split(' ', [StringSplitOptions]::RemoveEmptyEntries)[0]).ToLowerInvariant()
$actualBackupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedBackup).Hash.ToLowerInvariant()
if ($expectedBackupHash -ne $actualBackupHash) {
    throw "The frozen production backup checksum does not match."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\llm-wiki\semantic-corpus"
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null
$entriesPath = Join-Path $resolvedOutput "entries.jsonl"
$sourcesPath = Join-Path $resolvedOutput "sources.jsonl"
$manifestPath = Join-Path $resolvedOutput "corpus-manifest.json"

$container = "slogs-semantic-corpus-$([Guid]::NewGuid().ToString('N'))"
$databasePassword = [Guid]::NewGuid().ToString('N')
try {
    & podman run -d --name $container `
        -e POSTGRES_DB=slogs `
        -e POSTGRES_USER=slogs `
        -e "POSTGRES_PASSWORD=$databasePassword" `
        docker.io/pgvector/pgvector:pg16 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to start the disposable semantic-corpus database." }

    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        & podman exec $container pg_isready -U slogs -d slogs | Out-Null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) { throw "The disposable semantic-corpus database did not become ready." }

    & podman cp $resolvedBackup "${container}:/tmp/slogs.dump"
    if ($LASTEXITCODE -ne 0) { throw "Failed to copy the frozen backup." }
    & podman exec $container pg_restore -U slogs -d slogs --no-owner --no-privileges /tmp/slogs.dump
    if ($LASTEXITCODE -ne 0) { throw "Failed to restore the frozen backup." }

    $entrySql = @'
SELECT json_build_object(
  'id', "Id", 'ownerUserName', "OwnerUserName", 'slug', "Slug", 'title', "Title",
  'summary', "Summary", 'sourcePrompt', "SourcePrompt", 'content', "Content",
  'tagsJson', "TagsJson"::jsonb, 'categoryPath', "CategoryPath", 'isPublic', "IsPublic",
  'createdAt', "CreatedAt", 'updatedAt', "UpdatedAt")::text
FROM "LlmWikiEntries"
ORDER BY "OwnerUserName", "Id";
'@
    $sourceSql = @'
SELECT json_build_object(
  'id', "Id", 'entryId', "EntryId", 'ownerUserName', "OwnerUserName", 'action', "Action",
  'prompt', "Prompt", 'content', "Content", 'title', "Title", 'tags', "Tags",
  'categoryPath', "CategoryPath", 'createdAt', "CreatedAt")::text
FROM "LlmWikiEntrySources"
ORDER BY "OwnerUserName", "EntryId", "CreatedAt", "Id";
'@
    $entries = @(& podman exec $container psql -U slogs -d slogs -X -q -A -t -v ON_ERROR_STOP=1 -c $entrySql)
    if ($LASTEXITCODE -ne 0) { throw "Failed to export semantic entry corpus." }
    $sources = @(& podman exec $container psql -U slogs -d slogs -X -q -A -t -v ON_ERROR_STOP=1 -c $sourceSql)
    if ($LASTEXITCODE -ne 0) { throw "Failed to export semantic source corpus." }

    $entries | Set-Content -LiteralPath $entriesPath -Encoding utf8NoBOM
    $sources | Set-Content -LiteralPath $sourcesPath -Encoding utf8NoBOM
    $entryHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $entriesPath).Hash.ToLowerInvariant()
    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourcesPath).Hash.ToLowerInvariant()
    $corpusHashInput = "entries:$entryHash`nsources:$sourceHash`n"
    $corpusHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($corpusHashInput))).ToLowerInvariant()
    [ordered]@{
        schemaVersion = 1
        backupSha256 = $actualBackupHash
        entryCount = $entries.Count
        sourceCount = $sources.Count
        entriesSha256 = $entryHash
        sourcesSha256 = $sourceHash
        corpusSha256 = $corpusHash
    } | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
}
finally {
    & podman rm -f $container 2>$null | Out-Null
}

Write-Host "[LLM Wiki semantic corpus] PASS $manifestPath"
