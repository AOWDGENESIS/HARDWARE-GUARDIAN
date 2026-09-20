<#
.SYNOPSIS
    Drives the UAC situations of the admin matrix (chapter 77) and measures what Windows answers.

.DESCRIPTION
    The hosted CI runner has no interactive prompt: a "runas" start cannot be proven there, and a
    canceled prompt (Win32 1223) cannot be proven either. Both belong to the target machine, and both
    need a person in front of it - the script prepares the situation, the person answers the prompt,
    the script measures and files the result.

    What is measured here:
      * Is this session elevated or not (the precondition of the whole matrix)?
      * ADM-02: a prompt that is confirmed -> the process runs, and the exit code is filed.
      * ADM-03: a prompt that is canceled -> Windows answers 1223 and nothing was started. The script
        recognizes that answer, so the case is proven by Windows, not by an assumption.
      * ADM-04: nothing to do when the session is already elevated - the case is only meaningful when
        the application was started "as administrator" by the user, so it stays an open point.

    What this script cannot prove: that Windows Maintenance Center itself reports UAC_CANCELLED for
    the canceled case (M31-S-004). That is a property of the application; hand in its log with
    -WmcLog and the report compares, otherwise the run stays NOT VERIFIED with the open point named.

.PARAMETER WmcLog
    Optional log of the application in which the canceled elevation is visible.

.EXAMPLE
    pwsh ./scripts/vm/Test-UacMatrix.ps1
    pwsh ./scripts/vm/Test-UacMatrix.ps1 -WmcLog C:\temp\wmc-admin-worker.log

.NOTES
    Must run on the target machine, in a session of the account whose behaviour should be proven
    (a standard user account for ADM-01 to ADM-03).
#>
[CmdletBinding()]
param(
    [string]$WmcLog = '',
    [ValidateSet('unit', 'integration', 'safety', 'security', 'recovery', 'installer',
        'localization', 'offline', 'regression', 'release')][string]$Area = 'safety'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/Evidence.ps1"

$run = New-WmcEvidenceRun -Area $Area -TestId 'M31-UAC-001' -Title 'UAC matrix: standard user, confirmed prompt, canceled prompt'

$findings = New-Object System.Collections.Generic.List[string]
$openPoints = New-Object System.Collections.Generic.List[string]
$blocked = New-Object System.Collections.Generic.List[string]
$text = New-Object System.Collections.Generic.List[string]

$isAdmin = $run.Environment.isAdministrator -eq $true
Add-WmcMeasurement -Run $run -Name 'session.isElevated' -Value $isAdmin -Source 'WindowsPrincipal.IsInRole(Administrator)'
$text.Add("session       : elevated=$isAdmin user=$env:USERNAME")

# A no-op in a separate process: it changes nothing, and its only purpose is the prompt itself.
$probe = Join-Path $env:SystemRoot 'System32/cmd.exe'
$probeArguments = @('/c', 'exit', '0')

function Invoke-Elevation {
    param([string]$Label)
    $entry = [ordered]@{ label = $Label; started = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') }
    try {
        $process = Start-Process -FilePath $probe -ArgumentList $probeArguments -Verb RunAs -PassThru -Wait -ErrorAction Stop
        $entry.outcome = 'confirmed'
        $entry.exitCode = $process.ExitCode
    } catch {
        $exception = $_.Exception
        $code = $exception.HResult
        if ($exception.InnerException) { $code = $exception.InnerException.HResult }
        $entry.outcome = 'refused'
        $entry.hresult = $code
        $entry.message = $exception.Message
    }
    return [pscustomobject]$entry
}

if ($isAdmin) {
    # Already elevated: ADM-04. Windows cannot show a prompt in this session, so the two measured
    # cases below cannot be produced here.
    $openPoints.Add('this session is already elevated: start the kit again in a standard user session to measure ADM-01 to ADM-03')
    $text.Add('ADM-02/ADM-03: not measurable in an elevated session')
} else {
    Write-Host ''
    Write-Host 'ADM-02: confirm the Windows prompt that is about to appear (it starts and closes a no-op).' -ForegroundColor Yellow
    $confirmed = Invoke-Elevation -Label 'ADM-02 confirmed prompt'
    $text.Add("ADM-02        : $($confirmed.outcome) exit=$($confirmed.exitCode)")
    if ($confirmed.outcome -eq 'confirmed') {
        Add-WmcMeasurement -Run $run -Name 'ADM-02.outcome' -Value 'confirmed, process ran and ended with exit code 0' -Source 'Start-Process -Verb RunAs'
    } else {
        $findings.Add("ADM-02 expected a confirmed prompt, Windows answered '$($confirmed.message)'")
    }

    Write-Host ''
    Write-Host 'ADM-03: cancel the Windows prompt that is about to appear.' -ForegroundColor Yellow
    $refused = Invoke-Elevation -Label 'ADM-03 canceled prompt'
    $text.Add("ADM-03        : $($refused.outcome) hresult=$($refused.hresult) message=$($refused.message)")
    $cancelCodes = @(-2147023673, 1223)
    if ($refused.outcome -eq 'refused' -and ($cancelCodes -contains $refused.hresult -or $refused.message -match 'canceled|cancelled')) {
        Add-WmcMeasurement -Run $run -Name 'ADM-03.outcome' -Value "canceled by the user, Windows answered $($refused.hresult) and no process was started" -Source 'Start-Process -Verb RunAs (error 1223)'
    } else {
        $findings.Add("ADM-03 expected the canceled answer (1223), measured: $($refused.outcome) $($refused.hresult) $($refused.message)")
    }
}

# The application side: without a log that shows how the application answered the canceled prompt,
# the matrix case is measured but not proven for the product.
if ([string]::IsNullOrWhiteSpace($WmcLog)) {
    $openPoints.Add('the application side is missing: hand in the log in which the canceled elevation is reported (-WmcLog)')
} elseif (-not (Test-Path $WmcLog)) {
    $openPoints.Add("the handed in application log $WmcLog does not exist")
} else {
    Add-WmcEvidenceFile -Run $run -Path $WmcLog -Description 'application log that has to show the canceled elevation as UAC_CANCELLED'
    $logText = Get-Content -Raw $WmcLog
    if ($logText -match 'UAC_CANCELLED') {
        Add-WmcMeasurement -Run $run -Name 'application.reportsUacCancelled' -Value 'yes' -Source 'searched the handed in log for UAC_CANCELLED'
    } else {
        $findings.Add('the handed in application log does not mention UAC_CANCELLED for the canceled prompt')
    }
}

$text | Set-Content -Encoding utf8 (Join-Path $run.Folder 'logs/uac-matrix.txt')
Add-WmcEvidenceFile -Run $run -Path (Join-Path $run.Folder 'logs/uac-matrix.txt') -Description 'the three UAC situations with their measured answers'

$status = 'PASSED'
$summary = 'the UAC situations were driven and measured on this machine'
if ($findings.Count -gt 0) {
    $status = 'FAILED'
    $summary = "$($findings.Count) measured answer(s) contradict the expectation"
} elseif ($openPoints.Count -gt 0) {
    $status = 'NOT VERIFIED'
    $summary = 'the Windows side was measured, but ' + $openPoints[0]
} elseif ($blocked.Count -gt 0) {
    $status = 'BLOCKED'
    $summary = $blocked[0]
}

Complete-WmcEvidenceRun -Run $run -Status $status -Summary $summary `
    -Findings $findings.ToArray() -OpenPoints $openPoints.ToArray() | Out-Null
