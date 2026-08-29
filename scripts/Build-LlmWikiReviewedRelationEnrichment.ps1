[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CorpusDirectory,
    [Parameter(Mandatory = $true)][string]$WorklistPath,
    [Parameter(Mandatory = $true)][string]$DecisionsPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$OwnerUserName = "dimohy"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$corpusRoot = (Resolve-Path -LiteralPath $CorpusDirectory).Path
$corpusManifest = Get-Content -LiteralPath (Join-Path $corpusRoot "corpus-manifest.json") -Raw | ConvertFrom-Json
$entries = @{}
Get-Content -LiteralPath (Join-Path $corpusRoot "entries.jsonl") | ForEach-Object {
    $entry = $_ | ConvertFrom-Json
    $entries[$entry.id] = $entry
}
$decisions = @{}
Get-Content -LiteralPath (Resolve-Path -LiteralPath $DecisionsPath) | ForEach-Object {
    $decision = $_ | ConvertFrom-Json
    $decisions[$decision.candidateId] = $decision
}
$worklist = @(Get-Content -LiteralPath (Resolve-Path -LiteralPath $WorklistPath) | ForEach-Object { $_ | ConvertFrom-Json })

$relations = [Collections.Generic.List[object]]::new()
$relationKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($sourceGroup in $worklist | Group-Object sourceEntryId) {
    $source = $entries[$sourceGroup.Name]
    if ($null -eq $source -or $source.ownerUserName -ne $OwnerUserName) { throw "Unknown or cross-owner source '$($sourceGroup.Name)'." }
    $mapped = [Collections.Generic.List[object]]::new()
    foreach ($candidate in $sourceGroup.Group) {
        $decision = $decisions[$candidate.candidateId]
        if ($null -eq $decision) { throw "Missing reviewed decision '$($candidate.candidateId)'." }
        if ($decision.decision -notin "merge-existing", "superseded-existing") { continue }
        $targetId = [string]$decision.matchedEntryIds[0]
        $target = $entries[$targetId]
        if ($null -eq $target -or $target.ownerUserName -ne $OwnerUserName) { throw "Unknown or cross-owner reviewed target '$targetId'." }
        if ($mapped.Count -eq 0 -or $mapped[$mapped.Count - 1].TargetId -ne $targetId) {
            $mapped.Add([pscustomobject]@{ Candidate = $candidate; TargetId = $targetId; Target = $target })
        }
    }

    for ($index = 0; $index + 1 -lt $mapped.Count; $index++) {
        $from = $mapped[$index]
        $to = $mapped[$index + 1]
        if ($from.TargetId -eq $to.TargetId) { continue }
        $key = "$($from.TargetId)|precedes|$($to.TargetId)"
        if (-not $relationKeys.Add($key)) { continue }
        $fromHeading = "## $($from.Candidate.heading)"
        $toHeading = "## $($to.Candidate.heading)"
        $fromIndex = $source.content.IndexOf($fromHeading, [StringComparison]::Ordinal)
        $toIndex = $source.content.IndexOf($toHeading, $fromIndex + $fromHeading.Length, [StringComparison]::Ordinal)
        if ($fromIndex -lt 0 -or $toIndex -le $fromIndex) { throw "Could not prove reviewed chronology for '$key'." }
        $chronologyQuote = $source.content.Substring($fromIndex, ($toIndex + $toHeading.Length) - $fromIndex)
        $relations.Add([ordered]@{
            fromEntityKey = "memory:$($from.TargetId)"
            toEntityKey = "memory:$($to.TargetId)"
            relationType = "precedes"
            confidence = 0.95
            evidence = @(
                [ordered]@{ entryId = $source.id; sourceId = $null; evidenceField = "content"; evidenceQuote = $chronologyQuote },
                [ordered]@{ entryId = $from.TargetId; sourceId = $null; evidenceField = "title"; evidenceQuote = $from.Target.title },
                [ordered]@{ entryId = $to.TargetId; sourceId = $null; evidenceField = "title"; evidenceQuote = $to.Target.title }
            )
        })
    }
}

$enrichment = [ordered]@{
    ownerUserName = $OwnerUserName
    corpusSha256 = $corpusManifest.corpusSha256
    generator = "codex-reviewed-memory-sequence"
    generatorVersion = "2026-08-29.1"
    entities = @()
    mentions = @()
    relations = $relations
    splitProposals = @()
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllText($resolvedOutput, ($enrichment | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
Write-Host "REVIEWED_RELATION_ENRICHMENT=PASS relations=$($relations.Count) output=$resolvedOutput"
