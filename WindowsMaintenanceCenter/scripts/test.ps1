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
    [string]$Filter = '',

    # The suite had 314 cases when they were first counted on 2026-09-20. A lower number means tests
    # were skipped or not discovered, which must fail the run instead of looking like a green suite
    # (spec 61 and 87). Raise this number whenever cases are added; never lower it.
    [int]$MinimumTests = 300
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$project = Join-Path $root 'tests/WindowsMaintenanceCenter.Tests/WindowsMaintenanceCenter.Tests.csproj'
$results = Join-Path $root 'artifacts/test-results'

New-Item -ItemType Directory -Force -Path $results | Out-Null

# xUnit v3 runs on the Microsoft.Testing.Platform: everything after "--" is read by the test
# application itself, not by the SDK. The TRX file is produced by the platform's report extension.
$arguments = @(
    'test', $project,
    '-c', $Configuration,
    '--',
    '--report-trx',
    '--report-trx-filename', 'windowsmaintenancecenter.trx',
    '--results-directory', $results
)
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $arguments += @('--filter', $Filter)
}

Write-Host "dotnet $($arguments -join ' ')" -ForegroundColor Cyan
& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "the test run failed with exit code $LASTEXITCODE."
}

# A run that produced no result file, or a result file without a single test, is not a passed test
# run: it means the tests were not discovered - the mistake this project must never report as
# success. The expected number of test classes guards against a silently shrinking suite.
$trx = Join-Path $results 'windowsmaintenancecenter.trx'
if (-not (Test-Path $trx)) {
    throw "the test run reported success but wrote no TRX file at $trx - the tests did not run."
}

[xml]$report = Get-Content -Raw $trx
$counters = $report.TestRun.ResultSummary.Counters
$executed = [int]$counters.total
if ($executed -lt $MinimumTests) {
    throw "only $executed test(s) were executed, at least $MinimumTests are expected - the suite shrank or was not discovered."
}

Write-Host "tests passed: $executed executed, $($counters.passed) passed, $($counters.failed) failed, $($counters.skipped) skipped" -ForegroundColor Green
Write-Host "TRX: $trx" -ForegroundColor Green
