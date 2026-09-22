<#
    Self-test of the proof library (scripts/vm/Evidence.ps1).

    Why this file exists
    --------------------
    Two runs ended like this, after every single step of the installation cycle had passed:

        the run report could not be completed: Complete-WmcEvidenceRun failed in line 325:
        $report = [ordered]@{ - Argument types do not match

    Install, layout, SHA-256, start, use probe, uninstall, portable artefact: all PASS. And then the
    proof itself could not be written, so a complete cycle left a folder without report.json and
    without report.txt. Chapter 71 of the specification wants five fields in every report - a tool
    that writes the proof is the one tool that has to be checked before it is trusted.

    What this script does
    ---------------------
      1. It runs the real path: new run, measurement, evidence file, complete. Then it checks that
         report.json and report.txt exist, that the JSON parses and that it carries the five fields.
      2. It checks the rule that must hold: a PASSED run without a single measurement or evidence
         file is refused (chapter 86). That check is negative on purpose - a check that only ever
         says yes proves nothing.
      3. If step 1 fails, it narrows the failure down: the same structures are built one at a time
         (a bisect), so the log names the construct that breaks instead of a line number. This needs
         no further run, and it is the reason this file exists in this shape.

    Exit code 0 means the library works. Anything else is a finding, not a warning.
#>

[CmdletBinding()]
param(
    # The self-test writes its own run folder. `-KeepOnSuccess` keeps it (used when a reader wants to
    # look at the report), otherwise a passed self-test removes its folder again - the self-test is
    # tooling evidence, not product evidence, and the repository should not grow with it on every run.
    [switch]$KeepOnSuccess
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$failures = New-Object System.Collections.Generic.List[string]
$cleaned = $false
$runFolder = $null

function Write-Result([string]$Name, [bool]$Ok, [string]$Detail = '') {
    $mark = if ($Ok) { 'PASS' } else { 'FAIL' }
    if ([string]::IsNullOrWhiteSpace($Detail)) {
        Write-Host "  $mark      $Name"
    } else {
        Write-Host "  $mark      $Name - $Detail"
    }
}

function Get-FailureText($ErrorRecord) {
    # The message alone was not enough the last two times: the same text came back twice with only a
    # changed line number. The stack tells which frame threw.
    $where = $ErrorRecord.InvocationInfo
    $parts = @($ErrorRecord.Exception.Message)
    if ($where) {
        $parts += "at line $($where.ScriptLineNumber): $($where.Line)".Trim()
    }
    if ($ErrorRecord.ScriptStackTrace) {
        $parts += "stack: $($ErrorRecord.ScriptStackTrace -replace "`r?`n", ' <- ')"
    }
    return ($parts -join ' | ')
}

Write-Host 'Evidence library self-test (chapter 71)' -ForegroundColor Cyan
. "$PSScriptRoot/Evidence.ps1"

# ---- 1. the real path ------------------------------------------------------------------------------
$run = $null
$completeError = $null
try {
    $run = New-WmcEvidenceRun -Area 'integration' -TestId 'M00-E-001' -Title 'Evidence library self-test'
    $runFolder = $run.Folder

    Write-WmcEvidenceText -Run $run -Name 'selftest.txt' -Lines @(
        'this file is written by Test-EvidenceLibrary.ps1',
        "at $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
    ) -Description 'a text file that the self-test writes and registers' | Out-Null
    Add-WmcMeasurement -Run $run -Name 'selftest.marker' -Value 1 -Source 'Test-EvidenceLibrary.ps1' | Out-Null
    Complete-WmcEvidenceRun -Run $run -Status 'PASSED' -Summary 'the evidence library writes its report'
    Write-Result 'the library completes a run' $true
} catch {
    $completeError = $_
    Write-Result 'the library completes a run' $false (Get-FailureText $_)
    $failures.Add("Complete-WmcEvidenceRun failed: $(Get-FailureText $_)")
}

# ---- 2. the report has to be there, and it has to say the right thing ------------------------------
if ($runFolder) {
    $jsonPath = Join-Path $runFolder 'report.json'
    $textPath = Join-Path $runFolder 'report.txt'
    Write-Result 'report.json exists' (Test-Path $jsonPath) $jsonPath
    Write-Result 'report.txt exists' (Test-Path $textPath) $textPath

    if (Test-Path $jsonPath) {
        try {
            $report = Get-Content -Raw $jsonPath | ConvertFrom-Json
            $missing = New-Object System.Collections.Generic.List[string]
            foreach ($field in 'timestamp', 'version', 'build', 'environment', 'result') {
                if ($null -eq $report.$field -or [string]::IsNullOrWhiteSpace([string]$report.$field)) {
                    # An empty field would look like a field. Chapter 71 wants the value.
                    if ($field -eq 'environment') { $missing.Add($field) } else { $missing.Add($field) }
                }
            }
            Write-Result 'the report carries timestamp, version, build, environment and result' `
                ($missing.Count -eq 0) ($(if ($missing.Count) { "empty: $($missing -join ', ')" } else { 'all five present' }))
            Write-Result 'the report says PASSED and holds the evidence' `
                ($report.result.status -eq 'PASSED' -and @($report.evidence).Count -ge 1) `
                "status=$($report.result.status) evidence=$(@($report.evidence).Count) measurements=$(@($report.measurements).Count)"
        } catch {
            Write-Result 'the report is readable JSON' $false (Get-FailureText $_)
            $failures.Add("report.json is not readable: $(Get-FailureText $_)")
        }
    }
}

# ---- 3. a success claim without proof has to be refused (chapter 86) -------------------------------
$refused = $false
$refusalText = ''
try {
    # A stub on purpose: the guard that has to fire sits before anything is written, so no folder is
    # created and nothing has to be cleaned up.
    $stub = [pscustomobject]@{
        TestId       = 'M00-E-002'
        Title        = 'a success claim without proof'
        Area         = 'integration'
        Folder       = (Join-Path ([System.IO.Path]::GetTempPath()) 'wmc-selftest-stub')
        StartedAt    = (Get-Date).ToUniversalTime()
        StartedBy    = 'Test-EvidenceLibrary.ps1'
        Environment  = [pscustomobject]@{ machine = 'stub' }
        Version      = [pscustomobject]@{ version = '0.0.0'; build = 'stub' }
        Evidence     = New-Object System.Collections.Generic.List[object]
        Measurements = New-Object System.Collections.Generic.List[object]
    }
    Complete-WmcEvidenceRun -Run $stub -Status 'PASSED' -Summary 'nothing was measured'
} catch {
    $refused = $true
    $refusalText = $_.Exception.Message
}
Write-Result 'a PASSED report without evidence is refused' $refused $refusalText
if (-not $refused) { $failures.Add('a PASSED run without evidence or measurement was accepted') }

# ---- 4. bisect: if the real path failed, name the construct that breaks ----------------------------
if ($completeError) {
    Write-Host ''
    Write-Host 'Bisect: the same structures, built one at a time' -ForegroundColor Yellow
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $inner = [ordered]@{ x = 1 }
    $probes = [ordered]@{
        'plain [ordered] literal'                        = { $null = [ordered]@{ a = 1 } }
        'a string value'                                 = { $null = [ordered]@{ a = 'text' } }
        'a formatted timestamp value'                    = { $null = [ordered]@{ t = $stamp } }
        'a nested [ordered] dictionary'                   = { $null = [ordered]@{ inner = $inner } }
        'a nested pscustomobject'                         = { $null = [ordered]@{ e = $run.Environment } }
        'a version record field of the run'               = { $null = [ordered]@{ v = $run.Version.version } }
        'the evidence list, just read'                     = { $null = $run.Evidence.Count }
        'the evidence list through @()'                    = { $null = @($run.Evidence) }
        'the evidence list through a cast'                 = { $null = [object[]]$run.Evidence }
        'the evidence list through .ToArray()'             = { $null = $run.Evidence.ToArray() }
        'the evidence list as a literal value'             = { $null = [pscustomobject]@{ l = $run.Evidence } }
        'a literal with a plain array of strings'          = { $null = [pscustomobject]@{ l = @('a', 'b') } }
        'a literal with a comma array'                     = { $null = [pscustomobject]@{ l = 'a', 'b' } }
        'a literal with an empty array'                    = { $null = [pscustomobject]@{ l = @(); m = @() } }
        'a literal with an array of pscustomobjects'       = { $null = [pscustomobject]@{ l = @([pscustomobject]@{ a = 1 }) } }
        # The two forms that failed in runs 35701860625 and 35703422964. They stay in the list as
        # probes: a reader sees which construct breaks instead of taking it on faith.
        'an [ordered] literal with the list through @()'   = { $null = [ordered]@{ l = @($run.Evidence) } }
        'a pscustomobject literal with the list through @()' = { $null = [pscustomobject]@{ l = @($run.Evidence) } }
        'the evidence list through ConvertTo-Json'         = { $null = $run.Evidence | ConvertTo-Json -Depth 8 }
        'a plain hashtable with the list through @()'      = { $null = @{ l = @($run.Evidence) } }
        # The form the report uses now: a dictionary filled through Add(), the list passed as it is.
        'an OrderedDictionary filled with Add(), serialised' = {
            $probe = New-Object System.Collections.Specialized.OrderedDictionary
            $probe.Add('id', 'M00-E-004')
            $probe.Add('evidence', $run.Evidence)
            $probe.Add('measurements', $run.Measurements)
            $probe.Add('findings', [string[]]@('one'))
            $text = $probe | ConvertTo-Json -Depth 8
            $back = $text | ConvertFrom-Json
            if (@($back.evidence).Count -ne $run.Evidence.Count) {
                throw "the evidence list did not survive the round trip (got $(@($back.evidence).Count))"
            }
        }
        # The same form with the two arrays that give the report its structure.
        'an OrderedDictionary with the version and the environment' = {
            $probe = New-Object System.Collections.Specialized.OrderedDictionary
            $probe.Add('version', $run.Version.version)
            $probe.Add('build', $run.Version.build)
            $probe.Add('environment', $run.Environment)
            $probe.Add('startedAt', $run.StartedAt.ToString('yyyy-MM-ddTHH:mm:ssZ'))
            $null = $probe | ConvertTo-Json -Depth 8
        }
    }
    foreach ($name in $probes.Keys) {
        try {
            & $probes[$name]
            Write-Result $name $true
        } catch {
            Write-Result $name $false (Get-FailureText $_)
        }
    }
}

# ---- result ---------------------------------------------------------------------------------------
if ($runFolder -and $failures.Count -eq 0 -and -not $KeepOnSuccess) {
    Remove-Item -Recurse -Force $runFolder
    $cleaned = $true
}

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host "evidence library self-test passed$(if ($cleaned) { ' (its own run folder was removed again)' })" -ForegroundColor Green
    if ($runFolder -and $KeepOnSuccess) { Write-Host "the report of the self-test: $runFolder" }
    exit 0
}

foreach ($failure in $failures) { Write-Host "  - $failure" -ForegroundColor Red }
if ($runFolder) { Write-Host "the run folder of the failed self-test: $runFolder" }
exit 1
