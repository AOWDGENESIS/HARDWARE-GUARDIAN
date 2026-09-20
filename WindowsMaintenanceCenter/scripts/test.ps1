<#
.SYNOPSIS
    Runs the automated tests and proves that they ran.

.DESCRIPTION
    Runs the xUnit test project and writes a TRX result file plus a human readable console log.
    A failing test aborts the script with a non-zero exit code, so a CI run cannot pass silently.

    Why this script starts the test application itself instead of calling `dotnet test`:
    xUnit v3 runs on the Microsoft.Testing.Platform (MTP), and the .NET 10 SDK does not start MTP
    projects through the old VSTest target any more ("Testing with VSTest target is no longer
    supported"). Starting the produced executable is the route the xUnit project documents itself,
    and it cannot be affected by an SDK default.

    Why the script asks the executable what it can do before it runs:
    An option that a test application does not know makes the run fail with "unknown option", which
    looks like a broken suite although only the report extension was missing. MTP v2 registers
    report extensions through build hooks, so whether TRX is available depends on the build, not on
    the command line. The script therefore reads `--help` first, uses the report option that really
    exists, and writes the help text into artifacts/test-results/help.log. Without that file a
    missing report option is indistinguishable from a typing mistake.

    A run without a TRX file is still a run - the console log of the test application is kept as
    artifacts/test-results/test-run.log - but it is reported as a run whose proof is incomplete, so
    a run that was only executed but not documented never looks like a fully verified one (spec 86).

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

# Native exit codes are checked explicitly below ($LASTEXITCODE) and turned into a message that says
# what failed. Letting PowerShell raise its own error for every non-zero exit code would hide the
# distinction between "the test executable printed its help" and "the tests failed".
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$project = Join-Path $root 'tests/WindowsMaintenanceCenter.Tests/WindowsMaintenanceCenter.Tests.csproj'
$results = Join-Path $root 'artifacts/test-results'

New-Item -ItemType Directory -Force -Path $results | Out-Null

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

$testDirectory = Split-Path -Parent $testExecutable

# Which files of the test platform and its extensions the build produced. This list is evidence: a
# report extension that is missing here cannot be asked for on the command line, and a report
# extension that is present here but unknown to the executable is a registration problem, not a
# missing package.
Write-Host '-- test platform files next to the test application --' -ForegroundColor Cyan
$platformFiles = Get-ChildItem -Path $testDirectory -Filter '*.dll' |
    Where-Object { $_.Name -match 'Microsoft\.Testing|TestAdapter|Trx' } |
    Sort-Object Name
if ($platformFiles) {
    foreach ($file in $platformFiles) {
        Write-Host ("  {0,-64} {1}" -f $file.Name, $file.VersionInfo.FileVersion)
    }
} else {
    Write-Host '  none - the test application carries no test platform file of its own'
}

# What the executable really offers. This is the only reliable source for option names.
$helpLog = Join-Path $results 'help.log'
Write-Host "-- $testExecutable --help" -ForegroundColor Cyan
$helpText = (& $testExecutable --help 2>&1 | Tee-Object -FilePath $helpLog) -join "`n"

$reportOption = $null
foreach ($candidate in @('--report-trx', '--report-xunit-trx')) {
    if ($helpText -match ('(?m)^\s*' + [regex]::Escape($candidate) + '\b')) {
        $reportOption = $candidate
        break
    }
}
if ($reportOption) {
    Write-Host "report option offered by the executable: $reportOption" -ForegroundColor Green
} else {
    Write-Warning 'the executable offers no TRX report option - the run is executed and logged, but its proof is incomplete (see help.log).'
}

$arguments = @()
if ($helpText -match '(?m)^\s*--results-directory\b') {
    $arguments += @('--results-directory', $results)
}
if ($helpText -match '(?m)^\s*--no-ansi\b') {
    $arguments += '--no-ansi'
}
if ($reportOption) {
    $arguments += $reportOption
    if ($reportOption -eq '--report-trx' -and $helpText -match '(?m)^\s*--report-trx-filename\b') {
        $arguments += @('--report-trx-filename', 'windowsmaintenancecenter.trx')
    }
}
if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $arguments += @('--filter', $Filter)
}

# The console log is kept as a file as well: it is the fallback proof when no TRX file could be
# written, and it stays readable from outside the runner.
$consoleLog = Join-Path $results 'test-run.log'
$console = New-Object System.Collections.Generic.List[string]

Write-Host "$testExecutable $($arguments -join ' ')" -ForegroundColor Cyan
& $testExecutable @arguments 2>&1 | ForEach-Object { $console.Add([string]$_) }
$testExitCode = $LASTEXITCODE
$console | Set-Content -Encoding utf8 $consoleLog
$console | ForEach-Object { Write-Host $_ }

if ($testExitCode -ne 0) {
    throw "the test run failed with exit code $testExitCode (see $consoleLog)."
}

# A run that produced no result file, or a result file without a single test, is not a passed test
# run: it means the tests were not discovered - the mistake this project must never report as
# success. The expected number of test cases guards against a silently shrinking suite.
$trx = Join-Path $results 'windowsmaintenancecenter.trx'
if (Test-Path $trx) {
    [xml]$report = Get-Content -Raw $trx
    $counters = $report.TestRun.ResultSummary.Counters
    $executed = [int]$counters.total
    if ($executed -lt $MinimumTests) {
        throw "only $executed test(s) were executed, at least $MinimumTests are expected - the suite shrank or was not discovered."
    }

    Write-Host "tests passed: $executed executed, $($counters.passed) passed, $($counters.failed) failed, $($counters.skipped) skipped" -ForegroundColor Green
    Write-Host "TRX: $trx" -ForegroundColor Green
    Write-Host "console log: $consoleLog" -ForegroundColor Green
    return
}

# No TRX file: the test application does not carry the report extension. The run itself is still
# proven by its console log, and the number of executed tests is read from that log, because a run
# whose size nobody checked is not a proof of anything.
$text = $console -join "`n"
$total = if ($text -match '(?m)^\s*total:\s*(\d+)') { [int]$Matches[1] } else { $null }
$failed = if ($text -match '(?m)^\s*failed:\s*(\d+)') { [int]$Matches[1] } else { $null }

if ($null -eq $total) {
    throw "the test run wrote no TRX file and its console log at $consoleLog carries no test count - the run cannot be verified."
}
if ($total -lt $MinimumTests) {
    throw "only $total test(s) were reported, at least $MinimumTests are expected - the suite shrank or was not discovered."
}
if ($null -ne $failed -and $failed -gt 0) {
    throw "$failed test(s) failed (see $consoleLog)."
}

Write-Warning "tests passed: $total executed, but NO TRX file was written - the proof of this run rests on $consoleLog alone."
Write-Host 'The report extension is missing from the test application. This is a gap in the evidence, not a passed gate; see docs/VM-CI.md.' -ForegroundColor Yellow
