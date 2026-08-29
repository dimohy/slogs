[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$CorpusDirectory,
    [Parameter(Mandatory = $true)][string]$CandidatePath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$OwnerUserName = "dimohy",
    [ValidateRange(1, 20)][int]$MatchLimit = 5
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Get-Tokens([string]$Text) {
    $stopWords = [Collections.Generic.HashSet[string]]::new(
        [string[]]@("입력", "요약", "사용자", "범위", "적용", "보존", "판단", "기준", "기능", "관련", "현재", "대한", "한다", "하도록"),
        [StringComparer]::Ordinal)
    $tokens = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($Text.ToLowerInvariant(), '[a-z0-9가-힣][a-z0-9가-힣._/-]{1,}')) {
        $token = $match.Value.Trim('.', '/', '-')
        if ($token.Length -ge 2 -and -not $stopWords.Contains($token)) { [void]$tokens.Add($token) }
    }
    return $tokens
}

function Get-Sha256([string]$Text) {
    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Text))).ToLowerInvariant()
}

$resolvedCorpus = (Resolve-Path -LiteralPath $CorpusDirectory).Path
$resolvedCandidates = (Resolve-Path -LiteralPath $CandidatePath).Path
$entriesPath = Join-Path $resolvedCorpus "entries.jsonl"
if (-not (Test-Path -LiteralPath $entriesPath -PathType Leaf)) { throw "entries.jsonl was not found." }

$entries = @(Get-Content -LiteralPath $entriesPath | ForEach-Object { $_ | ConvertFrom-Json } | Where-Object {
    $_.ownerUserName -eq $OwnerUserName -and $_.categoryPath -ne "slogs/llm-wiki/user-input"
})
$documents = foreach ($entry in $entries) {
    $text = "$($entry.title)`n$($entry.summary)`n$($entry.sourcePrompt)`n$($entry.content)"
    [pscustomobject]@{ Entry = $entry; Tokens = Get-Tokens $text }
}

$documentFrequency = @{}
foreach ($document in $documents) {
    foreach ($token in $document.Tokens) { $documentFrequency[$token] = 1 + ($documentFrequency[$token] ?? 0) }
}

$output = [Collections.Generic.List[string]]::new()
foreach ($candidate in Get-Content -LiteralPath $resolvedCandidates | ForEach-Object { $_ | ConvertFrom-Json }) {
    $candidateTokens = Get-Tokens "$($candidate.heading)`n$($candidate.body)"
    $queryWeight = 0.0
    foreach ($token in $candidateTokens) {
        $frequency = $documentFrequency[$token] ?? 0
        $queryWeight += [Math]::Log(($documents.Count + 1.0) / ($frequency + 1.0)) + 1.0
    }

    $matches = foreach ($document in $documents) {
        $overlap = 0.0
        $documentWeight = 0.0
        foreach ($token in $document.Tokens) {
            $frequency = $documentFrequency[$token] ?? 0
            $weight = [Math]::Log(($documents.Count + 1.0) / ($frequency + 1.0)) + 1.0
            $documentWeight += $weight
            if ($candidateTokens.Contains($token)) { $overlap += $weight }
        }
        if ($overlap -le 0) { continue }
        $score = $overlap / [Math]::Sqrt([Math]::Max($queryWeight * $documentWeight, 0.000001))
        [pscustomobject]@{
            id = $document.Entry.id
            title = $document.Entry.title
            categoryPath = $document.Entry.categoryPath
            score = [Math]::Round($score, 6)
        }
    }

    $bodySha = Get-Sha256 $candidate.body
    $reviewItem = [ordered]@{
        candidateId = "$($candidate.sourceEntryId):$($candidate.heading):$($bodySha.Substring(0, 12))"
        ownerUserName = $OwnerUserName
        sourceEntryId = $candidate.sourceEntryId
        sourceTitle = $candidate.sourceTitle
        heading = $candidate.heading
        body = $candidate.body
        bodySha256 = $bodySha
        suggestedMatches = @($matches | Sort-Object score -Descending | Select-Object -First $MatchLimit)
        decision = "pending-ai-review"
        matchedEntryIds = @()
        rationale = ""
    }
    $output.Add(($reviewItem | ConvertTo-Json -Compress -Depth 5))
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
[IO.File]::WriteAllLines($resolvedOutput, $output, [Text.UTF8Encoding]::new($false))
Write-Host "SPLIT_REVIEW_PREPARE=PASS candidates=$($output.Count) entries=$($entries.Count) output=$resolvedOutput"
