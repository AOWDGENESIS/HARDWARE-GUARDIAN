# Orchestrates the complete VM test environment for Windows Maintenance Center / Hardware Guardian
param(
    [switch]$SkipHardware = $false,
    [switch]$SkipInstaller = $false,
    [string]$EvidenceDir = ""
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($EvidenceDir)) {
    $EvidenceDir = Join-Path (Split-Path -Parent (Split-Path -Parent $ScriptDir)) "test-results"
}

Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host " Hardware Guardian / Windows Maintenance Center - VM Test Suite" -ForegroundColor Cyan
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host "Evidence Directory: $EvidenceDir"
Write-Host ""

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

function Execute-TestStep {
    param(
        [string]$Name,
        [string]$ScriptPath,
        [hashtable]$Parameters = @{}
    )

    Write-Host "--> Running $Name..." -NoNewline
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $status = "PASSED"
    $errorMsg = ""

    try {
        & $ScriptPath @Parameters | Out-Null
        $stopwatch.Stop()
        Write-Host " [PASSED] ($($stopwatch.ElapsedMilliseconds) ms)" -ForegroundColor Green
    } catch {
        $stopwatch.Stop()
        $status = "FAILED"
        $errorMsg = $_.Exception.Message
        Write-Host " [FAILED] ($($stopwatch.ElapsedMilliseconds) ms)" -ForegroundColor Red
        Write-Host "    Error: $errorMsg" -ForegroundColor Yellow
    }

    $results.Add([PSCustomObject]@{
        Name = $Name
        Status = $status
        DurationMs = $stopwatch.ElapsedMilliseconds
        Error = $errorMsg
    })
}

# 1. Test Evidence Library Self-Test
Execute-TestStep -Name "Evidence Library Validation" -ScriptPath (Join-Path $ScriptDir "Test-EvidenceLibrary.ps1")

# 2. Test Repair Tool Exit Codes
Execute-TestStep -Name "Repair Tool Exit Codes Matrix" -ScriptPath (Join-Path $ScriptDir "Test-RepairToolExitCodes.ps1")

# 3. Test Backup & Restore Chain
Execute-TestStep -Name "Backup & Restore Chain" -ScriptPath (Join-Path $ScriptDir "Test-BackupRestore.ps1")

# 4. Test UAC & Elevation Matrix
Execute-TestStep -Name "UAC Elevation Matrix" -ScriptPath (Join-Path $ScriptDir "Test-UacMatrix.ps1")

# 5. Hardware Evidence (Optional / if supported in VM)
if (-not $SkipHardware) {
    Execute-TestStep -Name "VM Hardware & CIM Evidence" -ScriptPath (Join-Path $ScriptDir "Invoke-VmHardwareEvidence.ps1")
}

# 6. Installer Cycle
if (-not $SkipInstaller) {
    Execute-TestStep -Name "Installer & Uninstaller Cycle" -ScriptPath (Join-Path $ScriptDir "Test-InstallerCycle.ps1")
}

Write-Host ""
Write-Host "=====================================================================" -ForegroundColor Cyan
Write-Host " Summary of VM Test Results" -ForegroundColor Cyan
Write-Host "=====================================================================" -ForegroundColor Cyan
$failed = 0
foreach ($r in $results) {
    $color = if ($r.Status -eq "PASSED") { "Green" } else { "Red" }
    Write-Host ("{0,-35} : {1}" -f $r.Name, $r.Status) -ForegroundColor $color
    if ($r.Status -ne "PASSED") { $failed++ }
}
Write-Host ""

if ($failed -gt 0) {
    Write-Host "$failed test suite(s) failed." -ForegroundColor Red
    exit 1
} else {
    Write-Host "All VM test suites completed successfully." -ForegroundColor Green
    exit 0
}
