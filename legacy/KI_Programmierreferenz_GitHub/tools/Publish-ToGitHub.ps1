# ASCII-only. Windows PowerShell 5.1 / PowerShell 7.
[CmdletBinding()]
param(
    [string]$Repository = 'KI-Programmierreferenz',
    [ValidateSet('public','private')]
    [string]$Visibility = 'private',
    [switch]$NoPush
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$Root = Split-Path -Parent $PSScriptRoot

function Fail([string]$Message) { throw $Message }

if (-not (Get-Command git.exe -ErrorAction SilentlyContinue)) { Fail 'Git was not found.' }
if (-not (Get-Command gh.exe -ErrorAction SilentlyContinue)) { Fail 'GitHub CLI gh.exe was not found. Install GitHub CLI, then run gh auth login.' }

Push-Location $Root
try {
    if (-not (Test-Path -LiteralPath (Join-Path $Root '.git'))) {
        git init -b main | Out-Null
    }

    git add .
    git status --short

    $hasCommit = $true
    try { git rev-parse HEAD | Out-Null } catch { $hasCommit = $false }
    if (-not $hasCommit) {
        git config user.name ((gh api user --jq .login) | Out-String).Trim()
        git config user.email ((gh api user --jq .email) | Out-String).Trim()
        git commit -m 'Initial KI programming reference' | Out-Null
    } else {
        git commit -m 'Update KI programming reference' | Out-Null
    }

    $owner = (gh api user --jq .login | Out-String).Trim()
    $remote = "https://github.com/$owner/$Repository.git"

    if (-not $NoPush) {
        $existing = $false
        try { gh repo view "$owner/$Repository" | Out-Null; $existing = $true } catch { }
        if (-not $existing) {
            gh repo create $Repository --$Visibility --source $Root --remote origin --push
        } else {
            $currentRemote = git remote get-url origin 2>$null
            if (-not $currentRemote) { git remote add origin $remote }
            git branch -M main
            git push -u origin main
        }
    }

    Write-Host 'GitHub publication completed.'
    Write-Host ("Repository: https://github.com/$owner/$Repository")
}
finally {
    Pop-Location
}
