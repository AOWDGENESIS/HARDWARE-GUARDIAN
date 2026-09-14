# Universal connector. Can be called from any project; no project path is required.
[CmdletBinding()]
param([string]$InstallRoot = "$env:LOCALAPPDATA\KI-Engineering-Memory", [string]$OutputPath = "")
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($OutputPath)){ $OutputPath=Join-Path $env:TEMP 'KI-Engineering-Context.txt' }
& (Join-Path $InstallRoot 'tools\Update-KI-MachineProfile.ps1') -InstallRoot $InstallRoot
& (Join-Path $InstallRoot 'tools\Export-KI-Context.ps1') -InstallRoot $InstallRoot -OutputPath $OutputPath
Write-Output $OutputPath
