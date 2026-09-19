<#
.SYNOPSIS
    Runs the automated tests.

.DESCRIPTION
    Runs the xUnit test project and writes a TRX result file plus a human readable console log.
    A failing test aborts the script with a non-zero exit code, so a CI run cannot pass silently.

.PARAMETER Configuration
    Debug or Release (default: Release).

.PARAMETER Filter
    Optional xUnit filter, for example "FullyQualifiedName~PathGuardTests".

.EXAMPLE
    pwsh ./scripts/test.ps1 -Filter "FullyQualifiedName~MaintenanceSafetyTests"

.NOTES
    NOT EXECUTED in the development container (no .NET SDK there). See docs/STATUS.md.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$Filter = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$project = Join-Path $root 'tests/HardwareGuardian.Tests/HardwareGuardian.Tests.csproj'
$results = Join-Path $root 'artifacts/test-results'

New-Item -ItemType Directory -Force -Path $results | Out-Null

$arguments = @(
    'test', $project,
    '-c', $Configuration,
    '--results-directory', $results,
    '--logger', 'trx;LogFileName=hardwareguardian.trx',
    '--logger', 'console;verbosity=normal'
)
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $arguments += @('--filter', $Filter)
}

Write-Host "dotnet $($arguments -join ' ')" -ForegroundColor Cyan
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "the test run failed with exit code $LASTEXITCODE."
}

Write-Host "tests passed; TRX: $(Join-Path $results 'hardwareguardian.trx')" -ForegroundColor Green
