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

# xUnit v3 runs on the Microsoft.Testing.Platform (MTP). The .NET 10 SDK no longer starts MTP
# projects through the old VSTest path, and `dotnet test` only uses MTP when the repository asks for
# it. Both routes are used here, in this order:
#   1. build the test project,
#   2. start the produced test executable directly - that is the route the xUnit project documents and
#      it cannot be affected by any SDK runner default.
# The TRX file comes from the platform's report extension, so a run without a result file stays a run
# without a result file instead of looking green.
$buildArguments = @('build', $project, '-c', $Configuration, '--nologo')
Write-Host "dotnet $($buildArguments -join ' ')" -ForegroundColor Cyan
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "the test project could not be built (exit code $LASTEXITCODE)."
}

$testExecutable = Join-Path $root "tests/WindowsMaintenanceCenter.Tests/bin/$Configuration/net10.0-windows/WindowsMaintenanceCenter.Tests.exe"
if (-not (Test-Path $testExecutable)) {
    throw "the test executable was not produced at $testExecutable - the test project did not build."
}

$arguments = @(
    '--report-trx',
    '--report-trx-filename', 'windowsmaintenancecenter.trx',
    '--results-directory', $results
)
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $arguments += @('--filter', $Filter)
}

Write-Host "$testExecutable $($arguments -join ' ')" -ForegroundColor Cyan
& $testExecutable @arguments
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
