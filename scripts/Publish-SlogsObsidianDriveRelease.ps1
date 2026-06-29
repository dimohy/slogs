param(
    [string] $Version,
    [string] $RuntimeIdentifier = "win-x64",
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "src\Slogs.Obsidian.Drive\Slogs.Obsidian.Drive.csproj"
$dotnetPath = Join-Path $repoRoot ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnetPath)) {
    $dotnetPath = "dotnet"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml] $projectXml = Get-Content -Path $projectPath
    $Version = $projectXml.Project.PropertyGroup.Version
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Version was not provided and could not be read from $projectPath."
}

$publishDir = Join-Path $repoRoot "artifacts\publish\slogs-obsidian-drive\$Version\$RuntimeIdentifier\publish"
$distDir = Join-Path $repoRoot "artifacts\publish\slogs-obsidian-drive\$Version\$RuntimeIdentifier\dist"
New-Item -ItemType Directory -Force -Path $publishDir, $distDir | Out-Null

& $dotnetPath publish $projectPath `
    -c $Configuration `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishAot=true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $publishDir "SlogsObsidianDrive.exe"
if (-not (Test-Path $publishedExe)) {
    throw "Expected NativeAOT executable was not produced: $publishedExe"
}

$assetName = "SlogsObsidianDrive-$Version-$RuntimeIdentifier.exe"
$assetPath = Join-Path $distDir $assetName
Copy-Item -Path $publishedExe -Destination $assetPath -Force

$stagingDir = Join-Path $distDir "SlogsObsidianDrive-$Version-$RuntimeIdentifier"
if (Test-Path $stagingDir) {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingDir | Out-Null
Copy-Item -Path $publishedExe -Destination (Join-Path $stagingDir "SlogsObsidianDrive.exe") -Force
Copy-Item -Path (Join-Path $repoRoot "src\Slogs.Obsidian.Drive\README.md") -Destination (Join-Path $stagingDir "README.md") -Force

$zipPath = Join-Path $distDir "SlogsObsidianDrive-$Version-$RuntimeIdentifier.zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath

$hash = Get-FileHash -Path $assetPath -Algorithm SHA256
$hashPath = Join-Path $distDir "$assetName.sha256"
$hash.Hash | Set-Content -Path $hashPath -Encoding ascii

[pscustomobject] @{
    Version = $Version
    RuntimeIdentifier = $RuntimeIdentifier
    Exe = $assetPath
    Zip = $zipPath
    Sha256 = $hash.Hash
}
