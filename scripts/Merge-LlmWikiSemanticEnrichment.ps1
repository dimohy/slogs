[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaseManifestPath,
    [Parameter(Mandatory = $true)][string]$EnrichmentPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = "Stop"
$base = Get-Content -Raw -LiteralPath (Resolve-Path -LiteralPath $BaseManifestPath) | ConvertFrom-Json
$enrichment = Get-Content -Raw -LiteralPath (Resolve-Path -LiteralPath $EnrichmentPath) | ConvertFrom-Json
if ($enrichment.ownerUserName -ne $base.ownerUserName) { throw "Enrichment owner does not match the base manifest." }
if ($enrichment.corpusSha256 -ne $base.corpusSha256) { throw "Enrichment corpus SHA-256 does not match the base manifest." }

$knownEntities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entity in $base.entities) { [void]$knownEntities.Add($entity.key) }
foreach ($entity in @($enrichment.entities)) {
    if (-not $knownEntities.Add($entity.key)) { throw "Duplicate enriched entity '$($entity.key)'." }
    $base.entities += $entity
}
foreach ($mention in @($enrichment.mentions)) { $base.mentions += $mention }
foreach ($relation in @($enrichment.relations)) {
    if (-not $knownEntities.Contains($relation.fromEntityKey) -or -not $knownEntities.Contains($relation.toEntityKey)) {
        throw "Enriched relation has an unknown endpoint."
    }
    $base.relations += $relation
}
foreach ($split in @($enrichment.splitProposals)) { $base.splitProposals += $split }
$base.generator = "$($base.generator)+$($enrichment.generator)"
$base.generatorVersion = $enrichment.generatorVersion
$base.generatedAt = [DateTimeOffset]::UtcNow

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
$base | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8NoBOM
Write-Host "[LLM Wiki semantic enrichment] PASS entities=$($base.entities.Count) mentions=$($base.mentions.Count) relations=$($base.relations.Count) splits=$($base.splitProposals.Count) path=$resolvedOutput"
