# ASCII-only. Creates a portable context bundle for any AI or agent.
[CmdletBinding()]
param([string]$InstallRoot = "$env:LOCALAPPDATA\KI-Engineering-Memory", [string]$OutputPath = "$env:USERPROFILE\Desktop\KI-Engineering-Context.txt")
$ErrorActionPreference='Stop'
$ref=Join-Path $InstallRoot 'reference\KI_Dauerreferenz_Programmierung_und_Softwarequalitaet_DE.txt'
$load=Join-Path $InstallRoot 'reference\LOAD_INSTRUCTION.txt'
$prof=Join-Path $InstallRoot 'runtime\KI_Maschinenprofil.md'
foreach($p in @($ref,$load,$prof)){ if(-not(Test-Path -LiteralPath $p)){ throw "Missing file: $p" } }
$content=(Get-Content -Raw -LiteralPath $load) + "`r`n`r`n" + (Get-Content -Raw -LiteralPath $prof) + "`r`n`r`n" + (Get-Content -Raw -LiteralPath $ref)
[IO.File]::WriteAllText($OutputPath,$content,(New-Object Text.UTF8Encoding($false)))
Write-Host "Context exported: $OutputPath"
