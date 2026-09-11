[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PublishDirectory,
    [string]$SourceRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$ContractPath = (Join-Path $PSScriptRoot "..\tests\Fixtures\slogs-required-mcp-tools.v1.json"),
    [string]$ResultPath
)

$ErrorActionPreference = "Stop"

$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$source = (Resolve-Path -LiteralPath $SourceRoot).Path
$contractFile = (Resolve-Path -LiteralPath $ContractPath).Path
$assemblyPath = Join-Path $publish "Slogs.dll"
$programPath = Join-Path $source "src\Slogs\Program.cs"

foreach ($requiredPath in @($assemblyPath, $programPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release-contract input is missing: $requiredPath"
    }
}

$contract = Get-Content -LiteralPath $contractFile -Raw | ConvertFrom-Json
if ($contract.schemaVersion -ne "1.0.0" -or @($contract.requiredToolTypes).Count -eq 0) {
    throw "Unsupported or empty MCP release contract: $contractFile"
}

$programSource = Get-Content -LiteralPath $programPath -Raw
$assemblyText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($assemblyPath))
$checks = [Collections.Generic.List[object]]::new()

foreach ($toolType in $contract.requiredToolTypes) {
    $typeName = [string]$toolType.typeName
    $toolSourcePath = Join-Path $source "src\Slogs\Data\$typeName.cs"
    if (-not (Test-Path -LiteralPath $toolSourcePath -PathType Leaf)) {
        throw "Required MCP tool source is missing: $toolSourcePath"
    }

    $registrationPattern = "\.WithTools\s*<\s*$([regex]::Escape($typeName))\s*>\s*\(\s*\)"
    if (-not [regex]::IsMatch($programSource, $registrationPattern)) {
        throw "Required MCP tool type is not registered in Program.cs: $typeName"
    }

    $toolSource = Get-Content -LiteralPath $toolSourcePath -Raw
    foreach ($toolNameValue in $toolType.tools) {
        $toolName = [string]$toolNameValue
        $attributePattern = "McpServerTool\s*\(\s*Name\s*=\s*`"$([regex]::Escape($toolName))`"\s*\)"
        $sourceDeclared = [regex]::IsMatch($toolSource, $attributePattern)
        $published = $assemblyText.Contains($toolName, [StringComparison]::Ordinal)
        $checks.Add([ordered]@{
            typeName = $typeName
            toolName = $toolName
            sourceDeclared = $sourceDeclared
            typeRegistered = $true
            publishedAssemblyContainsName = $published
        })
        if (-not $sourceDeclared -or -not $published) {
            throw "Required MCP tool failed the published release contract: $toolName"
        }
    }
}

$result = [ordered]@{
    status = "passed"
    schemaVersion = [string]$contract.schemaVersion
    verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    assemblyPath = $assemblyPath
    assemblySha256 = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    checks = $checks
}
$json = $result | ConvertTo-Json -Depth 6

if (-not [string]::IsNullOrWhiteSpace($ResultPath)) {
    $resultParent = Split-Path -Parent $ResultPath
    if (-not [string]::IsNullOrWhiteSpace($resultParent)) {
        New-Item -ItemType Directory -Force -Path $resultParent | Out-Null
    }
    [IO.File]::WriteAllText($ResultPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}

Write-Output $json
