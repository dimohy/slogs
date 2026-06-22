[CmdletBinding()]
param(
    [string]$PromptUrl = "https://slogs.dev/prompts/slogs-mcp.ko.md",
    [string]$VersionUrl = "https://slogs.dev/prompts/slogs-mcp.version",
    [string]$TargetPath = (Join-Path $HOME ".codex\AGENTS.md")
)

$ErrorActionPreference = "Stop"

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

$targetDirectory = Split-Path -Parent $TargetPath
if (-not [string]::IsNullOrWhiteSpace($targetDirectory)) {
    [System.IO.Directory]::CreateDirectory($targetDirectory) | Out-Null
}

$current = if (Test-Path -LiteralPath $TargetPath) {
    [System.IO.File]::ReadAllText($TargetPath, $utf8NoBom)
}
else {
    ""
}

$versionResponse = Invoke-WebRequest -Uri $VersionUrl -UseBasicParsing -Headers @{ "Cache-Control" = "no-cache" }
if ($versionResponse.StatusCode -lt 200 -or $versionResponse.StatusCode -ge 300) {
    throw "Failed to fetch Slogs MCP prompt version from $VersionUrl. HTTP status: $($versionResponse.StatusCode)"
}

$serverVersion = ([string]$versionResponse.Content).Trim()
if ([string]::IsNullOrWhiteSpace($serverVersion)) {
    throw "Fetched Slogs MCP prompt version is empty: $VersionUrl"
}

if ($serverVersion -notmatch '^[0-9A-Za-z._-]+$') {
    throw "Fetched Slogs MCP prompt version contains unsupported characters: $serverVersion"
}

$pattern = "(?s)<!-- SLOGS_MCP_PROMPT:BEGIN.*?<!-- SLOGS_MCP_PROMPT:END -->"
$regex = [System.Text.RegularExpressions.Regex]::new($pattern)
$existingBlockMatch = $regex.Match($current)
if ($existingBlockMatch.Success) {
    $existingHeaderMatch = [System.Text.RegularExpressions.Regex]::Match(
        $existingBlockMatch.Value,
        "<!-- SLOGS_MCP_PROMPT:BEGIN url=(?<url>.*?) version=(?<version>[0-9A-Za-z._-]+) sha256=(?<sha256>[a-f0-9]{64}) updated=.*? -->")
    $alreadyCurrentByVersion = $existingHeaderMatch.Success `
        -and [string]::Equals($existingHeaderMatch.Groups["url"].Value, $PromptUrl, [System.StringComparison]::Ordinal) `
        -and [string]::Equals($existingHeaderMatch.Groups["version"].Value, $serverVersion, [System.StringComparison]::Ordinal)

    if ($alreadyCurrentByVersion) {
        [pscustomobject]@{
            Updated = $false
            PromptFetched = $false
            PromptUrl = $PromptUrl
            VersionUrl = $VersionUrl
            TargetPath = [System.IO.Path]::GetFullPath($TargetPath)
            Version = $serverVersion
            Sha256 = $existingHeaderMatch.Groups["sha256"].Value
        } | ConvertTo-Json -Compress
        return
    }
}

$response = Invoke-WebRequest -Uri $PromptUrl -UseBasicParsing -Headers @{ "Cache-Control" = "no-cache" }
if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 300) {
    throw "Failed to fetch Slogs MCP prompt from $PromptUrl. HTTP status: $($response.StatusCode)"
}

$prompt = [string]$response.Content
if ([string]::IsNullOrWhiteSpace($prompt)) {
    throw "Fetched Slogs MCP prompt is empty: $PromptUrl"
}

$sha256 = [System.Security.Cryptography.SHA256]::Create()
try {
    $hashBytes = $sha256.ComputeHash($utf8NoBom.GetBytes($prompt))
}
finally {
    $sha256.Dispose()
}

$hash = ($hashBytes | ForEach-Object { $_.ToString("x2") }) -join ""
$updatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$managedBlock = @"
<!-- SLOGS_MCP_PROMPT:BEGIN url=$PromptUrl version=$serverVersion sha256=$hash updated=$updatedAt -->
$prompt
<!-- SLOGS_MCP_PROMPT:END -->
"@.TrimEnd()

if ($existingBlockMatch.Success) {
    $next = $regex.Replace(
        $current,
        [System.Text.RegularExpressions.MatchEvaluator] { param($match) $managedBlock },
        1)
}
else {
    $separator = if ($current.Trim().Length -eq 0) { "" } else { "`r`n`r`n" }
    $next = $current.TrimEnd() + $separator + $managedBlock + "`r`n"
}

$changed = -not [string]::Equals($current, $next, [System.StringComparison]::Ordinal)
if ($changed) {
    [System.IO.File]::WriteAllText($TargetPath, $next, $utf8NoBom)
}

[pscustomobject]@{
    Updated = $changed
    PromptFetched = $true
    PromptUrl = $PromptUrl
    VersionUrl = $VersionUrl
    TargetPath = [System.IO.Path]::GetFullPath($TargetPath)
    Version = $serverVersion
    Sha256 = $hash
} | ConvertTo-Json -Compress
