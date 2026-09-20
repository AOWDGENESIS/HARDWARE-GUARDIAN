<#
.SYNOPSIS
    Runs the automated tests, proves that they ran, and files the proof under test-results/unit.

.DESCRIPTION
    Builds the xUnit test project, starts the produced test application and writes a TRX result file
    plus a human readable console log.

    Why this script starts the test application itself instead of calling `dotnet test`:
    xUnit v3 runs on a test platform that the .NET 10 SDK no longer starts through the old VSTest
    target ("Testing with VSTest target is no longer supported"). Starting the produced executable is
    the route the xUnit project documents itself, and it cannot be affected by an SDK default.

    Why the script asks the executable what it can do before it runs:
    An option that a test application does not know makes the run fail with "unknown option", which
    looks like a broken suite although only the report option was wrong. The run of 35504520714
    showed exactly that: the application runs xUnit's own in-process runner, which takes
    `-result-trx <file>`, not the `--report-trx` of the Microsoft.Testing.Platform extensions. The
    script therefore reads `--help` first, uses the report option that really exists and keeps the
    help text as artifacts/test-results/help.log.

    A run without a TRX file is still a run - the console log is kept and its test count is read from
    it - but it is reported as a run whose proof is incomplete, so a run that was only executed but
    not documented never looks like a verified one (specification chapters 5 and 86).

    Every run copies its results into test-results/unit/<utc>-<outcome>/ with the five fields of
    chapter 71 (timestamp, version, build, environment, result). That folder is the evidence the
    specification asks for, not artifacts/.

.PARAMETER Configuration
    Debug or Release (default: Release).

.PARAMETER Filter
    Optional filter, for example "FullyQualifiedName~PathGuardTests" (MTP) or a class name (xUnit).

.EXAMPLE
    pwsh ./scripts/test.ps1 -Filter "*PathGuard*"

.NOTES
    NOT EXECUTED in the development container (no .NET SDK there). See docs/STATUS.md.
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$Filter = '',

    # The suite had 315 cases when they were first counted on 2026-09-20 (run 35505322775 executed
    # 315, 0 failed). The journal and the recovery engine (M34/M35) added 14 cases, the hash chain of
    # the journal (SEC-12) another 5 and report injection (SEC-14) one, so the floor is 335. A lower number means tests were skipped or
    # not discovered, which must fail the run instead of looking like a green suite (specification
    # chapters 61 and 87). Raise this number whenever cases are added; never lower it.
    [int]$MinimumTests = 335
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Native exit codes are checked explicitly below ($LASTEXITCODE) and turned into a message that says
# what failed. Letting PowerShell raise its own error for every non-zero exit code would hide the
# difference between "the test executable printed its help" and "the tests failed".
if (Test-Path variable:PSNativeCommandUseErrorActionPreference) {
    $PSNativeCommandUseErrorActionPreference = $false
}

$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$project = Join-Path $root 'tests/WindowsMaintenanceCenter.Tests/WindowsMaintenanceCenter.Tests.csproj'
$results = Join-Path $root 'artifacts/test-results'
$trx = Join-Path $results 'windowsmaintenancecenter.trx'

New-Item -ItemType Directory -Force -Path $results | Out-Null
if (Test-Path $trx) { Remove-Item $trx -Force }

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

# Which files of the test platform and its extensions the build produced. A report extension that is
# missing here cannot be asked for on the command line; one that is present but unknown to the
# executable is a registration problem, not a missing package.
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

# Two test platforms are possible: the xUnit v3 in-process runner (option `-result-trx <file>`) and
# the Microsoft.Testing.Platform extensions (option `--report-trx`). Which one is available is
# decided by the build, so it is read from the help instead of being assumed.
$mtpReport = [bool]($helpText -match '(?m)^\s*--report-trx\b')
$xunitReport = [bool]($helpText -match '(?m)^\s*-result-trx\b')

$arguments = New-Object System.Collections.Generic.List[string]
if ($xunitReport) {
    Write-Host 'report option offered by the executable: -result-trx (xUnit v3 in-process runner)' -ForegroundColor Green
    $arguments.AddRange([string[]]@('-result-trx', $trx, '-noColor'))
    if ($helpText -match '(?m)^\s*-result-html\b') {
        $arguments.AddRange([string[]]@('-result-html', (Join-Path $results 'windowsmaintenancecenter.html')))
    }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $arguments.AddRange([string[]]@('-filter', $Filter))
    }
} elseif ($mtpReport) {
    Write-Host 'report option offered by the executable: --report-trx (Microsoft.Testing.Platform)' -ForegroundColor Green
    if ($helpText -match '(?m)^\s*--results-directory\b') {
        $arguments.AddRange([string[]]@('--results-directory', $results))
    }
    if ($helpText -match '(?m)^\s*--no-ansi\b') { $arguments.Add('--no-ansi') }
    $arguments.Add('--report-trx')
    if ($helpText -match '(?m)^\s*--report-trx-filename\b') {
        $arguments.AddRange([string[]]@('--report-trx-filename', 'windowsmaintenancecenter.trx'))
    }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $arguments.AddRange([string[]]@('--filter', $Filter))
    }
} else {
    Write-Warning 'the executable offers no TRX report option - the run is executed and logged, but its proof is incomplete (see help.log).'
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $arguments.AddRange([string[]]@('-filter', $Filter))
    }
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

# The numbers are read from the TRX when there is one, and from the console log otherwise. A run that
# produced no numbers at all is not a passed test run: it means the tests were not discovered - the
# mistake this project must never report as success.
$executed = 0
$passed = 0
$failed = 0
$skipped = 0
$proof = 'TRX'
$hasTrx = Test-Path $trx

if ($hasTrx) {
    [xml]$report = Get-Content -Raw $trx
    # The TRX file declares a default XML namespace, so the obvious path //UnitTestResult finds
    # nothing at all. local-name() matches the element without depending on the namespace, and a run
    # whose report could not be read must not be mistaken for a run without tests.
    $cases = @($report.SelectNodes("//*[local-name()='UnitTestResult']"))
    $executed = $cases.Count
    foreach ($node in $cases) {
        switch ($node.GetAttribute('outcome')) {
            'Passed' { $passed++ }
            'Failed' { $failed++ }
            default { $skipped++ }
        }
    }
}

if ($executed -eq 0) {
    # Either no report was written, or it carried nothing readable: then the console log is the only
    # source of numbers, and the run says so instead of pretending the TRX proved anything.
    $proof = if ($hasTrx) {
        'console log only (the TRX file carried no readable test results)'
    } else {
        'console log only (no TRX file was written)'
    }
    $text = $console -join "`n"
    if ($text -match '(?m)\bTotal:\s*(\d+)') { $executed = [int]$Matches[1] }
    if ($text -match '(?m)\bFailed:\s*(\d+)') { $failed = [int]$Matches[1] }
    if ($text -match '(?m)\bSkipped:\s*(\d+)') { $skipped = [int]$Matches[1] }
    if ($executed -gt 0) { $passed = $executed - $failed - $skipped }
}

if ($executed -lt $MinimumTests) {
    throw "only $executed test case(s) were reported, at least $MinimumTests are expected - the suite shrank or was not discovered."
}
if ($failed -gt 0) {
    throw "$failed test(s) failed (see $consoleLog)."
}

$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$outcome = "$passed-of-$executed"
$evidence = Join-Path $root "test-results/unit/$stamp-$outcome"
New-Item -ItemType Directory -Force -Path $evidence | Out-Null

# Chapter 71: every report carries timestamp, version, build, environment and result. The build is
# taken from the runner's environment when there is one, and stays "unknown" otherwise.
$version = 'unknown'
$props = Join-Path $root 'eng/Version.props'
if (Test-Path $props) {
    $propsText = Get-Content -Raw $props
    if ($propsText -match '<VersionPrefix>(.*?)</VersionPrefix>') { $version = $Matches[1].Trim() }
}
$build = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { 'unknown (no checkout revision in this environment)' }
$environment = @(
    "machine=$env:COMPUTERNAME",
    "os=$([System.Environment]::OSVersion.VersionString)",
    "powerShell=$($PSVersionTable.PSVersion)",
    "dotnet=$(& dotnet --version 2>$null)",
    "configuration=$Configuration",
    "filter=$(if ([string]::IsNullOrWhiteSpace($Filter)) { 'none' } else { $Filter })"
)

$summary = New-Object System.Collections.Generic.List[string]
$summary.Add("timestamp   : $stamp")
$summary.Add("version     : $version")
$summary.Add("build       : $build")
$summary.Add("result      : $passed passed, $failed failed, $skipped skipped, $executed executed (exit code $testExitCode)")
$summary.Add("proof       : $proof")
$summary.Add('environment :')
$environment | ForEach-Object { $summary.Add("  $_") }
$summary | Set-Content -Encoding utf8 (Join-Path $evidence 'summary.txt')

foreach ($file in @($trx, $consoleLog, $helpLog, (Join-Path $results 'windowsmaintenancecenter.html'))) {
    if (Test-Path $file) {
        Copy-Item $file (Join-Path $evidence (Split-Path -Leaf $file)) -Force
    }
}

Write-Host "tests passed: $executed executed, $passed passed, $failed failed, $skipped skipped" -ForegroundColor Green
Write-Host "proof: $proof" -ForegroundColor Green
Write-Host "evidence: $evidence" -ForegroundColor Green
if ($proof -ne 'TRX') {
    Write-Warning 'This run is documented by its console log only. It is not the proof chapter 71 asks for, so it must not be filed as a passed gate; see docs/VM-CI.md.'
}
