# ASCII-only. Windows PowerShell 5.1 / PowerShell 7.
[CmdletBinding()]
param(
    [switch]$Remove
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$Root=$PSScriptRoot
$Updater=Join-Path $Root 'Update-KI-Dauerreferenz.ps1'
$TaskName='KI-Dauerreferenz-AutoUpdate'
if ($Remove) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host 'Auto-update task removed.'
    exit 0
}
if (-not (Test-Path -LiteralPath $Updater -PathType Leaf)) { throw 'Updater not found.' }
$pwsh=(Get-Command pwsh.exe -ErrorAction SilentlyContinue).Source
if ([string]::IsNullOrWhiteSpace($pwsh)) { $pwsh=(Get-Command powershell.exe -ErrorAction Stop).Source }
$action=New-ScheduledTaskAction -Execute $pwsh -Argument ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $Updater + '"')
$trigger1=New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$trigger2=New-ScheduledTaskTrigger -Daily -At 03:15
$settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger @($trigger1,$trigger2) -Settings $settings -Description 'Updates the local KI programming reference with current machine and tool facts.' -Force | Out-Null
& $pwsh -NoProfile -ExecutionPolicy Bypass -File $Updater
Write-Host 'Auto-update installed and first inventory run completed.'
Write-Host 'Task:' $TaskName
