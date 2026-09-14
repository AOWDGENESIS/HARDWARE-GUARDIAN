# ASCII-only. Installs the project-independent memory package into LocalAppData.
[CmdletBinding()]
param([string]$SourceRoot = (Split-Path -Parent $PSScriptRoot), [string]$InstallRoot = "$env:LOCALAPPDATA\KI-Engineering-Memory")
$ErrorActionPreference='Stop'
if(-not (Test-Path -LiteralPath $SourceRoot -PathType Container)){ throw "Source root not found: $SourceRoot" }
New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $SourceRoot 'reference') -Destination $InstallRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $SourceRoot 'tools') -Destination $InstallRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $SourceRoot 'connectors') -Destination $InstallRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $SourceRoot 'docs') -Destination $InstallRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $SourceRoot 'profiles') -Destination $InstallRoot -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $InstallRoot 'runtime') | Out-Null
& (Join-Path $InstallRoot 'tools\Update-KI-MachineProfile.ps1') -InstallRoot $InstallRoot
Write-Host "Installed: $InstallRoot"
