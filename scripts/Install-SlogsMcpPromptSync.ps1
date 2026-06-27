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

$legacyTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -ne $legacyTask) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

& $powerShellCommand.Source `
    -NoProfile `
    -ExecutionPolicy Bypass `
    -File $syncScript `
    -PromptUrl $PromptUrl `
    -VersionUrl $VersionUrl `
    -TargetPath $TargetPath
