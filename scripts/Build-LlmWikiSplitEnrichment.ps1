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

function Get-Sha256([string]$Text) {
    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()
}

$corpusRoot = (Resolve-Path -LiteralPath $CorpusDirectory).Path
$corpusManifest = Get-Content -LiteralPath (Join-Path $corpusRoot "corpus-manifest.json") -Raw | ConvertFrom-Json
$entries = @{}
Get-Content -LiteralPath (Join-Path $corpusRoot "entries.jsonl") | ForEach-Object {
    $entry = $_ | ConvertFrom-Json
    $entries[$entry.id] = $entry
}
$worklist = @{}
Get-Content -LiteralPath (Resolve-Path -LiteralPath $WorklistPath) | ForEach-Object {
    $candidate = $_ | ConvertFrom-Json
    $worklist[$candidate.candidateId] = $candidate
}

$splitProposals = [Collections.Generic.List[object]]::new()
foreach ($decision in Get-Content -LiteralPath (Resolve-Path -LiteralPath $DecisionsPath) | ForEach-Object { $_ | ConvertFrom-Json }) {
    if ($decision.decision -ne "split-new") { continue }
    $candidate = $worklist[$decision.candidateId]
    if ($null -eq $candidate) { throw "Split decision has no frozen worklist item: $($decision.candidateId)" }
    if ((Get-Sha256 $candidate.body) -ne $decision.bodySha256) { throw "Split candidate body hash changed." }
    $source = $entries[$decision.sourceEntryId]
    if ($null -eq $source -or $source.ownerUserName -ne $OwnerUserName) { throw "Split source is missing or cross-owner." }
    if (-not $source.content.Contains($candidate.body, [StringComparison]::Ordinal)) {
        throw "Split evidence is not an exact source-content quote: $($decision.candidateId)"
    }
    $splitProposals.Add([ordered]@{
        sourceEntryId = $decision.sourceEntryId
        proposedTitle = $decision.proposedTitle
        proposedCategoryPath = $decision.proposedCategoryPath
        proposedPrompt = $decision.proposedPrompt
        proposedContent = $decision.proposedContent
        reason = $decision.rationale
        evidence = @([ordered]@{
            entryId = $decision.sourceEntryId
            sourceId = $null
            evidenceField = "content"
            evidenceQuote = $candidate.body
        })
    })
}

$enrichment = [ordered]@{
    ownerUserName = $OwnerUserName
    corpusSha256 = $corpusManifest.corpusSha256
    generator = "codex-memory-split-review"
    generatorVersion = "2026-08-29.1"
    entities = @()
    mentions = @()
    relations = @()
    splitProposals = $splitProposals
}
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllText($resolvedOutput, ($enrichment | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
Write-Host "SPLIT_ENRICHMENT_BUILD=PASS splits=$($splitProposals.Count) output=$resolvedOutput"
