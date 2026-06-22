[CmdletBinding()]
param(
    [string]$PromptUrl = "https://slogs.dev/prompts/slogs-mcp.ko.md",
    [string]$VersionUrl = "https://slogs.dev/prompts/slogs-mcp.version",
    [string]$TargetPath = (Join-Path $HOME ".codex\AGENTS.md"),
    [string]$TaskName = "SlogsMcpPromptSync",
    [ValidateRange(15, 1440)]
    [int]$IntervalMinutes = 60
)

$ErrorActionPreference = "Stop"

$syncScript = Join-Path $PSScriptRoot "Sync-SlogsMcpPrompt.ps1"
if (-not (Test-Path -LiteralPath $syncScript)) {
    throw "Sync script not found: $syncScript"
}

$powerShellCommand = Get-Command pwsh.exe -ErrorAction SilentlyContinue
if ($null -eq $powerShellCommand) {
    $powerShellCommand = Get-Command powershell.exe -ErrorAction Stop
}

$powerShellExe = $powerShellCommand.Source
$taskArguments = "-NoProfile -ExecutionPolicy Bypass -File `"$syncScript`" -PromptUrl `"$PromptUrl`" -VersionUrl `"$VersionUrl`" -TargetPath `"$TargetPath`""

$action = New-ScheduledTaskAction -Execute $powerShellExe -Argument $taskArguments
$trigger = New-ScheduledTaskTrigger `
    -Once `
    -At (Get-Date).AddMinutes(1) `
    -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes) `
    -RepetitionDuration (New-TimeSpan -Days 3650)
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Settings $settings `
    -Force | Out-Null

& $powerShellExe -NoProfile -ExecutionPolicy Bypass -File $syncScript -PromptUrl $PromptUrl -VersionUrl $VersionUrl -TargetPath $TargetPath
