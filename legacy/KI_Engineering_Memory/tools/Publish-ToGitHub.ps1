# ASCII-only. Publishes this standalone knowledge repository to GitHub.
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Repository, [ValidateSet('private','public')][string]$Visibility='private', [string]$Root=(Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference='Stop'
if(-not(Get-Command gh -ErrorAction SilentlyContinue)){ throw 'GitHub CLI gh is required. Run: gh auth login' }
if(-not(Test-Path (Join-Path $Root 'reference'))){ throw 'Invalid memory repository root.' }
Set-Location $Root
if(-not(Test-Path '.git')){ git init | Out-Null }
git branch -M main
git add .
git diff --cached --quiet; if($LASTEXITCODE -eq 0){ Write-Host 'No changes to commit.' } else { git commit -m 'Initial or updated KI Engineering Memory' | Out-Null }
$remote=(git remote get-url origin 2>$null)
if(-not $remote){ gh repo create $Repository --$Visibility --source . --remote origin --push } else { git push -u origin main }
Write-Host "Published: $Repository"
