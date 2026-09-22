<#
.SYNOPSIS
    Measures what the repair and check tools answer on a real machine (target machine, read-only).

.DESCRIPTION
    The repair tools (DISM, SFC, CHKDSK) are registered but not released, and this script shows why
    that is a measurement and not caution: nobody has yet recorded what these programs answer when
    they find something. DISM /ScanHealth answers 0 when it finds corruption, sfc /verifyonly answers
    0 as well, and chkdsk has its own set of codes. Until those numbers are measured, a non-zero exit
    code would be reported as "the run failed", which would be a wrong statement about the run.

    This script starts only the read-only switches - /ScanHealth, /verifyonly, chkdsk /scan - and
    never a switching one (/RestoreHealth, /scannow, /f, /r). It records the exit code, the duration
    and the output of every run, and it records the tool version, because an exit code is only
    comparable with the version that produced it.

    Nothing is judged here: the report states what was measured. -Expect is the only way to turn the
    measurement into a verdict, and the expectations have to be handed in deliberately.

.PARAMETER Volume
    Volume that chkdsk should scan, for example "C:". Defaults to the system volume.

.PARAMETER Expect
    Optional table of expected exit codes, for example @{ DismScanHealth = 0 }. A different code
    turns the run into FAILED.

.PARAMETER Skip
    Names of tools to leave out. They are recorded as open points with the reason, never silently
    dropped: SFC and CHKDSK can need a quarter of an hour on a cold machine, which belongs on the
    target machine and not in a build pipeline. The CI runner measures the rest.

.EXAMPLE
    pwsh ./scripts/vm/Test-RepairToolExitCodes.ps1
    pwsh ./scripts/vm/Test-RepairToolExitCodes.ps1 -Volume 'D:' -Expect @{ ChkdskScan = 0 }

.NOTES
    Must run on the target machine. Needs administrator rights for the DISM, SFC and CHKDSK parts;
    without them those parts are reported BLOCKED and the read-only queries are still measured.
#>
[CmdletBinding()]
param(
    [string]$Volume = "$env:SystemDrive",
    [hashtable]$Expect = @{},
    [string[]]$Skip = @(),
    [ValidateSet('unit', 'integration', 'safety', 'security', 'recovery', 'installer',
        'localization', 'offline', 'regression', 'release')][string]$Area = 'security'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/Evidence.ps1"

$run = New-WmcEvidenceRun -Area $Area -TestId 'M32-SEC-EXIT-001' -Title 'Exit codes of the repair and check tools, read-only switches'

$isAdmin = $run.Environment.isAdministrator -eq $true
$findings = New-Object System.Collections.Generic.List[string]
$openPoints = New-Object System.Collections.Generic.List[string]
$blocked = New-Object System.Collections.Generic.List[string]

# The switches are fixed here and are the read-only ones; the templates are the same as in
# SystemActionCatalog, so the measured code belongs to the declaration the application uses.
$tools = @(
    [pscustomobject]@{ Name = 'DismScanHealth'; File = 'dism.exe'; Arguments = @('/Online', '/Cleanup-Image', '/ScanHealth'); NeedsAdmin = $true; TimeoutMinutes = 60 },
    [pscustomobject]@{ Name = 'SfcVerifyOnly'; File = 'sfc.exe'; Arguments = @('/verifyonly'); NeedsAdmin = $true; TimeoutMinutes = 90 },
    [pscustomobject]@{ Name = 'ChkdskScan'; File = 'chkdsk.exe'; Arguments = @($Volume, '/scan'); NeedsAdmin = $true; TimeoutMinutes = 120 },
    [pscustomobject]@{ Name = 'FsutilDeleteNotifyQuery'; File = 'fsutil.exe'; Arguments = @('behavior', 'query', 'DisableDeleteNotify'); NeedsAdmin = $false; TimeoutMinutes = 1 },
    [pscustomobject]@{ Name = 'IpConfigAll'; File = 'ipconfig.exe'; Arguments = @('/all'); NeedsAdmin = $false; TimeoutMinutes = 1 },
    [pscustomobject]@{ Name = 'SystemInfoSnapshot'; File = 'systeminfo.exe'; Arguments = @('/fo', 'list'); NeedsAdmin = $false; TimeoutMinutes = 5 }
)

$text = New-Object System.Collections.Generic.List[string]

foreach ($tool in $tools) {
    $label = $tool.Name

    if ($Skip -contains $label) {
        # A skipped measurement is an open point, not a zero and not a pass (chapter 5).
        $openPoints.Add("$label was skipped on this machine (-Skip): it needs a machine whose operator can wait for it")
        $text.Add("$label : SKIPPED (-Skip)")
        continue
    }

    if ($tool.NeedsAdmin -and -not $isAdmin) {
        $blocked.Add("$label needs administrator rights and this session does not have them")
        $text.Add("$label : BLOCKED (no administrator rights)")
        continue
    }

    $executable = Get-Command $tool.File -ErrorAction SilentlyContinue
    if (-not $executable) {
        $blocked.Add("$label : $($tool.File) is not present on this machine")
        $text.Add("$label : BLOCKED ($($tool.File) not found)")
        continue
    }

    $standardOutput = Join-Path $run.Folder "logs/$label.out.txt"
    $standardError = Join-Path $run.Folder "logs/$label.err.txt"

    # Every tool is measured inside its own guard, and that guard is not decoration: in run 35705003836
    # the version of DISM was read from the process object *after* the process had ended
    # ($process.MainModule) - which throws, aborted the whole script and threw away the measurement that
    # had just been taken (DISM /ScanHealth answered 0 after 453 seconds). One unreadable value cost the
    # evidence of every following tool. From here on a tool that cannot be measured becomes a line in
    # the report and the next tool is measured anyway; the run status says BLOCKED, never PASSED.
    try {
        $started = Get-Date

        # Start-Process instead of the call operator: the exit code has to be read, the output has to be
        # filed, and a run that hangs must not hang the evidence collection with it.
        $process = Start-Process -FilePath $executable.Source -ArgumentList $tool.Arguments -NoNewWindow -PassThru `
            -RedirectStandardOutput $standardOutput -RedirectStandardError $standardError
        $finished = $process.WaitForExit([int]($tool.TimeoutMinutes * 60000))
        if (-not $finished) {
            try { $process.Kill() } catch { }
            $blocked.Add("$label did not finish within $($tool.TimeoutMinutes) minute(s) and was stopped")
            $text.Add("$label : BLOCKED (timeout after $($tool.TimeoutMinutes) minute(s))")
            continue
        }

        $seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
        $exit = $process.ExitCode
        Add-WmcMeasurement -Run $run -Name "$label.ExitCode" -Value $exit -Source "$($tool.File) $($tool.Arguments -join ' ')"
        Add-WmcMeasurement -Run $run -Name "$label.Seconds" -Value $seconds -Unit 's' -Source 'measured around the process'
    } catch {
        $blocked.Add("$label could not be started or its exit code could not be read: $($_.Exception.Message)")
        $text.Add("$label : BLOCKED ($($_.Exception.Message))")
        continue
    }

    # The tool version comes from the file on disk, not from the process object: a finished process has
    # no readable module information, and an unreadable version has to say so instead of stopping
    # everything. The value is what a reader needs anyway - which version of the tool answered here.
    $version = 'not readable'
    try {
        $version = (Get-Item -LiteralPath $executable.Source).VersionInfo.FileVersion
        if ([string]::IsNullOrWhiteSpace($version)) { $version = 'not reported by the file' }
    } catch {
        $version = "not readable ($($_.Exception.GetType().Name))"
    }
    Add-WmcMeasurement -Run $run -Name "$label.ToolVersion" -Value $version -Source $executable.Source
    $text.Add("$label : exit=$exit after $seconds s  version=$version  ($($tool.File) $($tool.Arguments -join ' '))")

    if ($Expect.ContainsKey($label)) {
        if ([int]$Expect[$label] -ne $exit) {
            $findings.Add("$label answered $exit, expected was $($Expect[$label])")
        }
    } else {
        # Measuring is not interpreting. DISM /ScanHealth answers 0 even when it *finds* corruption -
        # a zero is therefore evidence, not a verdict, and until somebody records what the number
        # means for this Windows build the catalog keeps the action at Allowed=false. That is true for
        # every code here, not only for a non-zero one (run 35705003836: 0 after 453 s).
        $openPoints.Add("$label answered $exit - record what that number means for this Windows build before the action may be released")
    }
}

# The output of the tools is filed as evidence; it names findings in human readable form, and a
# reader can compare it with the report of the application.
$text | Set-Content -Encoding utf8 (Join-Path $run.Folder 'logs/exit-codes.txt')
Add-WmcEvidenceFile -Run $run -Path (Join-Path $run.Folder 'logs/exit-codes.txt') -Description 'measured exit codes with duration and tool version'

$status = 'PASSED'
$summary = "measured $($run.Measurements.Count) values for $($tools.Count) tools on $($run.Environment.machine)"
if ($findings.Count -gt 0) {
    $status = 'FAILED'
    $summary = "$($findings.Count) measured code(s) differ from the handed in expectation"
} elseif ($blocked.Count -gt 0) {
    $status = 'BLOCKED'
    $summary = "$($blocked.Count) tool(s) could not be measured: $($blocked[0])"
} elseif ($openPoints.Count -gt 0) {
    $status = 'NOT VERIFIED'
    $summary = 'the codes were measured, but they are not yet explained: ' + $openPoints[0]
}

Complete-WmcEvidenceRun -Run $run -Status $status -Summary $summary `
    -Findings $findings.ToArray() -OpenPoints $openPoints.ToArray() | Out-Null

Write-Host "[M32-SEC-EXIT-001] $status - $summary" -ForegroundColor Yellow
if ($status -ne 'PASSED') {
    Write-Host 'A registration is not a release: the catalogs stay Allowed=false until these codes are explained.' -ForegroundColor Yellow
}

# The exit code answers one question only: did the measuring itself work? FAILED means a handed in
# expectation was not met, BLOCKED means a tool could not be measured - both are a broken measurement.
# NOT VERIFIED is the normal state after a successful measuring with no interpretation yet, and it is
# not an error of the tool. The verdict for the release is in the report, not in this code.
if ($status -in @('FAILED', 'BLOCKED')) { exit 1 }
exit 0
