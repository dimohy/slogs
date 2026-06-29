param(
    [string]$PluginDir = "src/obsidian-slogs-sync",
    [string]$OutputDir = "artifacts/obsidian-slogs-sync"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$pluginRoot = (Resolve-Path (Join-Path $repoRoot $PluginDir)).Path
$outputRoot = Join-Path $repoRoot $OutputDir
$repoOutput = Join-Path $outputRoot "repo"
$releaseOutput = Join-Path $outputRoot "release"

Push-Location $pluginRoot
try {
    npm run check
    npm run test
    npm run build
}
finally {
    Pop-Location
}

$manifestPath = Join-Path $pluginRoot "manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace($manifest.id) -or [string]::IsNullOrWhiteSpace($manifest.version)) {
    throw "manifest.json must include id and version."
}

$versionsPath = Join-Path $pluginRoot "versions.json"
if (-not (Test-Path -LiteralPath $versionsPath)) {
    throw "versions.json is required for Obsidian community plugin releases."
}

$versions = Get-Content -LiteralPath $versionsPath -Raw | ConvertFrom-Json
if ($null -eq $versions.($manifest.version)) {
    throw "versions.json must include the current manifest version '$($manifest.version)'."
}

Remove-Item -LiteralPath $outputRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $repoOutput, $releaseOutput | Out-Null

$repoFiles = @(
    "README.md",
    "manifest.json",
    "versions.json",
    "package.json",
    "package-lock.json",
    "tsconfig.json",
    "esbuild.config.mjs",
    "main.ts",
    "plugin-core.ts"
)

foreach ($file in $repoFiles) {
    Copy-Item -LiteralPath (Join-Path $pluginRoot $file) -Destination (Join-Path $repoOutput $file)
}

Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $repoOutput "LICENSE")
Copy-Item -LiteralPath (Join-Path $pluginRoot "scripts") -Destination (Join-Path $repoOutput "scripts") -Recurse

$releaseFiles = @("manifest.json", "main.js")
foreach ($file in $releaseFiles) {
    Copy-Item -LiteralPath (Join-Path $pluginRoot $file) -Destination (Join-Path $releaseOutput $file)
}

$stylesPath = Join-Path $pluginRoot "styles.css"
if (Test-Path -LiteralPath $stylesPath) {
    Copy-Item -LiteralPath $stylesPath -Destination (Join-Path $releaseOutput "styles.css")
}

$zipPath = Join-Path $outputRoot "$($manifest.id)-$($manifest.version).zip"
Compress-Archive -Path (Join-Path $releaseOutput "*") -DestinationPath $zipPath -Force

[PSCustomObject]@{
    Id = $manifest.id
    Version = $manifest.version
    RepositoryOutput = $repoOutput
    ReleaseOutput = $releaseOutput
    ReleaseZip = $zipPath
}
