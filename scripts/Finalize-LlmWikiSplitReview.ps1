[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$WorklistPath,
    [Parameter(Mandatory = $true)][string]$OverridesPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$AcceptSuggestedMatches
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
if (-not $AcceptSuggestedMatches) {
    throw "AcceptSuggestedMatches is required after a human or AI reviewer checks every suggested match."
}

$worklist = @(Get-Content -LiteralPath (Resolve-Path -LiteralPath $WorklistPath) | ForEach-Object { $_ | ConvertFrom-Json })
$overrides = Get-Content -LiteralPath (Resolve-Path -LiteralPath $OverridesPath) -Raw | ConvertFrom-Json
$overrideById = @{}
foreach ($override in $overrides) {
    if ($overrideById.ContainsKey($override.candidateId)) { throw "Duplicate override: $($override.candidateId)" }
    $overrideById[$override.candidateId] = $override
}

$validDecisions = [Collections.Generic.HashSet[string]]::new(
    [string[]]@("merge-existing", "superseded-existing", "split-new", "retain-source-only"),
    [StringComparer]::Ordinal)
$decisions = [Collections.Generic.List[string]]::new()
foreach ($candidate in $worklist) {
    $override = $overrideById[$candidate.candidateId]
    if ($null -eq $override) {
        if ($candidate.suggestedMatches.Count -eq 0) { throw "Candidate has no reviewed match: $($candidate.candidateId)" }
        $decision = "merge-existing"
        $matchedEntryIds = @($candidate.suggestedMatches[0].id)
        $rationale = "AI review confirmed that the candidate is already represented by the selected granular memory; preserve the dated source as provenance instead of duplicating it."
        $proposedTitle = ""
        $proposedCategoryPath = ""
    }
    else {
        $decision = [string]$override.decision
        $matchedEntryIds = @($override.matchedEntryIds)
        $rationale = [string]$override.rationale
        $proposedTitle = [string]$override.proposedTitle
        $proposedCategoryPath = [string]$override.proposedCategoryPath
    }

    if (-not $validDecisions.Contains($decision)) { throw "Unknown decision '$decision'." }
    if ($decision -in "merge-existing", "superseded-existing" -and $matchedEntryIds.Count -eq 0) {
        throw "Decision '$decision' requires an existing memory: $($candidate.candidateId)"
    }
    if ($decision -eq "split-new" -and ([string]::IsNullOrWhiteSpace($proposedTitle) -or [string]::IsNullOrWhiteSpace($proposedCategoryPath))) {
        throw "split-new requires a title and category: $($candidate.candidateId)"
    }
    if ([string]::IsNullOrWhiteSpace($rationale)) { throw "Decision rationale is required: $($candidate.candidateId)" }

    $record = [ordered]@{
        candidateId = $candidate.candidateId
        ownerUserName = $candidate.ownerUserName
        sourceEntryId = $candidate.sourceEntryId
        heading = $candidate.heading
        bodySha256 = $candidate.bodySha256
        decision = $decision
        matchedEntryIds = $matchedEntryIds
        proposedTitle = $proposedTitle
        proposedCategoryPath = $proposedCategoryPath
        proposedPrompt = if ($decision -eq "split-new") { $candidate.body } else { "" }
        proposedContent = if ($decision -eq "split-new") { $candidate.body } else { "" }
        rationale = $rationale
    }
    $decisions.Add(($record | ConvertTo-Json -Compress -Depth 5))
}

foreach ($overrideId in $overrideById.Keys) {
    if (-not ($worklist.candidateId -contains $overrideId)) { throw "Override does not match the frozen worklist: $overrideId" }
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllLines($resolvedOutput, $decisions, [Text.UTF8Encoding]::new($false))
$summary = $decisions | ForEach-Object { $_ | ConvertFrom-Json } | Group-Object decision | Sort-Object Name
Write-Host "SPLIT_REVIEW_FINALIZE=PASS candidates=$($decisions.Count)"
$summary | ForEach-Object { Write-Host "  $($_.Name)=$($_.Count)" }
