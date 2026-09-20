<#
.SYNOPSIS
    Builds Windows Maintenance Center from a clean checkout.

.DESCRIPTION
    Restores, builds and (optionally) publishes the application for a given runtime identifier.
    Every step checks the exit code and stops on the first failure: a build that did not run must
    never look like a successful build.

    The version lives in eng/Version.props, the build metadata is injected here so that the
    resulting binaries can be traced back to a commit.

.PARAMETER Configuration
    Debug or Release (default: Release).

.PARAMETER RuntimeIdentifier
    Target runtime (default: win-x64).

.PARAMETER SelfContained
    Publish the runtime with the application (default: true, required for the portable exe).

.PARAMETER OutputDirectory
    Publish output (default: artifacts/portable).

.EXAMPLE
    pwsh ./scripts/build.ps1 -Configuration Release

.NOTES
    NOT EXECUTED in the development container (no .NET SDK there). See docs/STATUS.md.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [bool]$SelfContained = $true,
    [string]$OutputDirectory = 'artifacts/portable',
    [switch]$SkipPublish,
    [switch]$NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$solution = Join-Path $root 'WindowsMaintenanceCenter.sln'
$app = Join-Path $root 'src/WindowsMaintenanceCenter.App/WindowsMaintenanceCenter.App.csproj'
$output = Join-Path $root $OutputDirectory

function Invoke-DotNet {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function Get-VersionProperty {
    param([string]$Name)
    [xml]$props = Get-Content -Raw (Join-Path $root 'eng/Version.props')
    $value = $props.Project.PropertyGroup.$Name
    if ([string]::IsNullOrWhiteSpace($value)) { return '0.0.0' }
    return $value.Trim()
}

$version = Get-VersionProperty 'VersionPrefix'
$suffix = Get-VersionProperty 'VersionSuffix'
if (-not [string]::IsNullOrWhiteSpace($suffix)) { $version = "$version-$suffix" }

$revision = 'unknown'
$buildDate = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
if (Get-Command git -ErrorAction SilentlyContinue) {
    $candidate = (& git -C $root rev-parse --short=12 HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($candidate)) {
        $revision = $candidate.Trim()
    }
}

Write-Host "Windows Maintenance Center $version ($revision) - $Configuration $RuntimeIdentifier" -ForegroundColor Green

Push-Location $root
try {
    if (-not $NoRestore) {
        Invoke-DotNet restore $solution
    }

    Invoke-DotNet build $solution -c $Configuration --no-restore `
        -p:Version=$version -p:SourceRevisionId=$revision -p:BuildDate=$buildDate

    if (-not $SkipPublish) {
        New-Item -ItemType Directory -Force -Path $output | Out-Null
        $publishArguments = @(
            'publish', $app,
            '-c', $Configuration,
            '-r', $RuntimeIdentifier,
            '--no-restore',
            '-o', $output,
            "-p:SelfContained=$($SelfContained.ToString().ToLowerInvariant())",
            '-p:PublishSingleFile=true',
            '-p:IncludeNativeLibrariesForSelfExtract=true',
            '-p:EnableCompressionInSingleFile=true',
            '-p:DebugType=none',
            "-p:Version=$version",
            "-p:SourceRevisionId=$revision",
            "-p:BuildDate=$buildDate"
        )
        Invoke-DotNet @publishArguments
        Write-Host "published: $(Join-Path $output 'WindowsMaintenanceCenter.exe')" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
