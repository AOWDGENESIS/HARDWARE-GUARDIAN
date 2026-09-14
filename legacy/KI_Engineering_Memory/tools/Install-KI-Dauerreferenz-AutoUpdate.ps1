# ASCII-only. Registers a user task for daily runtime refresh and logon refresh.
[CmdletBinding()]
param([string]$InstallRoot = "$env:LOCALAPPDATA\KI-Engineering-Memory")
$ErrorActionPreference='Stop'
$script=Join-Path $InstallRoot 'tools\Update-KI-MachineProfile.ps1'
if(-not(Test-Path -LiteralPath $script)){ throw "Memory package is not installed: $InstallRoot" }
$taskName='KI-Engineering-Memory-Runtime-Update'
$action=New-ScheduledTaskAction -Execute 'pwsh.exe' -Argument ('-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}"' -f $script)
$triggers=@(New-ScheduledTaskTrigger -AtLogOn, New-ScheduledTaskTrigger -Daily -At 03:15)
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $triggers -Description 'Refresh volatile machine and local AI facts for KI Engineering Memory.' -Force | Out-Null
Write-Host "Auto update installed: $taskName"
