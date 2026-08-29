[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CorpusDirectory,
    [Parameter(Mandatory = $true)][string]$OwnerUserName,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = "Stop"
$resolvedCorpus = (Resolve-Path -LiteralPath $CorpusDirectory).Path
$corpusManifest = Get-Content -Raw -LiteralPath (Join-Path $resolvedCorpus "corpus-manifest.json") | ConvertFrom-Json
$entries = @(Get-Content -LiteralPath (Join-Path $resolvedCorpus "entries.jsonl") |
    ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object ownerUserName -eq $OwnerUserName)
if ($entries.Count -eq 0) { throw "The corpus contains no entries for owner '$OwnerUserName'." }

$entities = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$mentions = [Collections.Generic.List[object]]::new()
$relations = [Collections.Generic.List[object]]::new()
$categoryEvidence = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)

foreach ($entry in $entries) {
    $memoryKey = "memory:$($entry.id)"
    $entities[$memoryKey] = [ordered]@{
        key = $memoryKey
        canonicalName = $entry.title
        entityType = "concept"
        description = $entry.summary
    }
    $mentions.Add([ordered]@{
        entityKey = $memoryKey
        entryId = $entry.id
        sourceId = $null
        evidenceField = "title"
        evidenceQuote = $entry.title
        confidence = 1.0
    })

    $segments = @($entry.categoryPath -split '/' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $prefixes = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $segments.Count; $index++) {
        $path = ($segments[0..$index] -join '/')
        $prefixes.Add($path)
        $categoryKey = "category:$path"
        if (-not $entities.ContainsKey($categoryKey)) {
            $entities[$categoryKey] = [ordered]@{
                key = $categoryKey
                canonicalName = $path
                entityType = if ($index -eq 1 -and $segments[0] -eq 'project') { "project" } else { "concept" }
                description = "LLM Wiki category $path"
            }
        }
        if (-not $categoryEvidence.ContainsKey($path)) { $categoryEvidence[$path] = $entry }
    }

    $leafPath = $prefixes[$prefixes.Count - 1]
    $relations.Add([ordered]@{
        fromEntityKey = $memoryKey
        toEntityKey = "category:$leafPath"
        relationType = "part-of"
        confidence = 1.0
        evidence = @([ordered]@{
            entryId = $entry.id
            sourceId = $null
            evidenceField = "category-path"
            evidenceQuote = $entry.categoryPath
        })
    })
}

foreach ($path in @($categoryEvidence.Keys | Sort-Object)) {
    $lastSlash = $path.LastIndexOf('/')
    if ($lastSlash -lt 0) { continue }
    $parent = $path.Substring(0, $lastSlash)
    $entry = $categoryEvidence[$path]
    $relations.Add([ordered]@{
        fromEntityKey = "category:$path"
        toEntityKey = "category:$parent"
        relationType = "part-of"
        confidence = 1.0
        evidence = @([ordered]@{
            entryId = $entry.id
            sourceId = $null
            evidenceField = "category-path"
            evidenceQuote = $path
        })
    })
}

$manifest = [ordered]@{
    schemaVersion = 1
    corpusSha256 = $corpusManifest.corpusSha256
    ownerUserName = $OwnerUserName
    generator = "slogs-taxonomy"
    generatorVersion = "2026-08-29.1"
    generatedAt = [DateTimeOffset]::UtcNow
    entities = @($entities.Values | Sort-Object key)
    mentions = @($mentions | Sort-Object entityKey,entryId)
    relations = @($relations | Sort-Object fromEntityKey,relationType,toEntityKey)
    splitProposals = @()
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8NoBOM
Write-Host "[LLM Wiki taxonomy manifest] PASS entities=$($entities.Count) mentions=$($mentions.Count) relations=$($relations.Count) path=$resolvedOutput"
