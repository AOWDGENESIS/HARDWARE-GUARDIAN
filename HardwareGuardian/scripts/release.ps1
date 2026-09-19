<#
.SYNOPSIS
    Produces the release artefacts: portable exe, installer, checksums and release notes.

.DESCRIPTION
    Builds the portable single-file executable, builds the installer with Inno Setup, then writes
    HardwareGuardian-Checksums.txt (SHA-256) and HardwareGuardian-ReleaseNotes.txt.

    The script refuses to continue when a required artefact is missing: the checksum file must
    describe files that really exist, and the release notes must name the commit they were built
    from. Nothing here is written by hand.

.PARAMETER Version
    Overrides the version from build/Version.props.

.PARAMETER InnoSetupPath
    Full path to ISCC.exe when Inno Setup is not installed in a standard location.

.EXAMPLE
    pwsh ./scripts/release.ps1

.NOTES
    NOT EXECUTED in the development container (no .NET SDK, no Inno Setup, not Windows).
    See docs/STATUS.md and docs/RELEASE.md.
#>
[CmdletBinding()]
param(
    [string]$Version = '',
    [string]$InnoSetupPath = '',
    [string]$RuntimeIdentifier = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$releaseDirectory = Join-Path $root 'artifacts/release'
$portableDirectory = Join-Path $root 'artifacts/portable'
$installerScript = Join-Path $root 'installer/HardwareGuardian.iss'
$portableName = 'HardwareGuardian-Portable-x64.exe'
$setupName = 'HardwareGuardianSetup-x64.exe'
$checksumName = 'HardwareGuardian-Checksums.txt'
$releaseNotesName = 'HardwareGuardian-ReleaseNotes.txt'

function Get-VersionProperty {
    param([string]$Name)
    [xml]$props = Get-Content -Raw (Join-Path $root 'build/Version.props')
    $value = $props.Project.PropertyGroup.$Name
    if ([string]::IsNullOrWhiteSpace($value)) { return '' }
    return $value.Trim()
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-VersionProperty 'VersionPrefix'
    $suffix = Get-VersionProperty 'VersionSuffix'
    if (-not [string]::IsNullOrWhiteSpace($suffix)) { $Version = "$Version-$suffix" }
}

$revision = 'unknown'
if (Get-Command git -ErrorAction SilentlyContinue) {
    $candidate = (& git -C $root rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($candidate)) { $revision = $candidate.Trim() }
}

Write-Host "release $Version at $revision" -ForegroundColor Green
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null

# 1. portable executable
& (Join-Path $root 'scripts/build.ps1') -Configuration Release -RuntimeIdentifier $RuntimeIdentifier -OutputDirectory 'artifacts/portable'
if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed; no release artefacts were produced." }

$publishedExe = Join-Path $portableDirectory 'HardwareGuardian.exe'
if (-not (Test-Path $publishedExe)) { throw "expected the published executable at $publishedExe" }
$portableTarget = Join-Path $releaseDirectory $portableName
Copy-Item -Path $publishedExe -Destination $portableTarget -Force

# 2. installer
if ([string]::IsNullOrWhiteSpace($InnoSetupPath)) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path $candidate)) { $InnoSetupPath = $candidate; break }
    }
}

if ([string]::IsNullOrWhiteSpace($InnoSetupPath)) {
    throw 'Inno Setup (ISCC.exe) was not found. Install it and pass -InnoSetupPath, see docs/BUILD.md.'
}

Write-Host "ISCC $InnoSetupPath" -ForegroundColor Cyan
& $InnoSetupPath "/DAppVersion=$Version" "/DSourceDirectory=$portableDirectory" "/DOutputDirectory=$releaseDirectory" $installerScript
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

$setupPath = Join-Path $releaseDirectory $setupName
if (-not (Test-Path $setupPath)) { throw "expected the installer at $setupPath" }

# 3. checksums (SHA-256, "hash  file" - same format as sha256sum/CertUtil output)
$lines = Get-ChildItem -Path $releaseDirectory -File |
    Where-Object { $_.Name -ne $checksumName -and $_.Name -ne $releaseNotesName } |
    Sort-Object Name |
    ForEach-Object {
        $hash = (Get-FileHash -Algorithm SHA256 -Path $_.FullName).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }

if (-not $lines) { throw 'no artefact to hash - the checksum file would have been empty.' }
Set-Content -Path (Join-Path $releaseDirectory $checksumName) -Value $lines -Encoding UTF8

# 4. release notes
$notes = Join-Path $root 'docs/RELEASE_NOTES.md'
$recent = @()
if (Get-Command git -ErrorAction SilentlyContinue) {
    $log = & git -C $root log --no-merges --pretty=format:"- %s" -n 40 2>$null
    if ($LASTEXITCODE -eq 0 -and $log) { $recent = $log }
}

$body = @()
$body += "Hardware Guardian $Version"
$body += "Windows Hardware Diagnostics, Maintenance & Update Center"
$body += ""
$body += "Built:       $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')"
$body += "Commit:      $revision"
$body += "Runtime:     .NET 10 (Windows Desktop), $RuntimeIdentifier"
$body += ""
$body += "Artefacts"
$body += "  $portableName      portable, single file, no installation required"
$body += "  $setupName         installer (Start Menu entry, optional desktop shortcut, uninstaller)"
$body += "  $checksumName      SHA-256 for every artefact above"
$body += ""
$body += "Verification"
$body += "  Get-FileHash -Algorithm SHA256 .\\$portableName"
$body += "  compare the value with the entry in $checksumName"
$body += ""
$body += "Safety properties of this build"
$body += "  - no firmware is flashed automatically"
$body += "  - no third-party driver portal is used as a source"
$body += "  - maintenance always runs scan -> plan -> dry run -> approval -> execute"
$body += "  - telemetry is disabled by default and the application works offline"
$body += ""
$body += "Recent changes"
$body += $recent
if (Test-Path $notes) {
    $body += ""
    $body += (Get-Content -Raw $notes)
}

Set-Content -Path (Join-Path $releaseDirectory $releaseNotesName) -Value $body -Encoding UTF8

Write-Host "release artefacts in $releaseDirectory" -ForegroundColor Green
Get-ChildItem -Path $releaseDirectory | Format-Table Name, Length -AutoSize
