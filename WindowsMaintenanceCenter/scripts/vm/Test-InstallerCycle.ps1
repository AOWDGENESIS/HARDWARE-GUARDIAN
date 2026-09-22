<#
.SYNOPSIS
    Runs the installation cycle of chapter 62 / chapter 74 on the machine it is started on and files
    the evidence under test-results/installer.

.DESCRIPTION
    Install -> verify the layout -> start the program -> probe that it really worked -> uninstall ->
    verify what is gone and what was kept on purpose. Afterwards the portable artefact is probed the
    same way: it must keep its data next to itself without any second file.

    What this script CAN prove:
      * the setup runs and returns 0,
      * the uninstall entry names the installation folder, the version and the publisher,
      * the installed executable is byte-identical to the one that was published,
      * the program starts, stays alive, writes its data folder and a log - the probe for "it really
        ran" when nobody can click through the interface,
      * the installed copy keeps its data in %ProgramData% and NOT next to the executable (chapter 82),
      * the Start menu entry appears and is removed again, the uninstall entry disappears, no program
        file survives the uninstall,
      * settings and audit trail survive the uninstall - on purpose, the uninstaller asks first,
      * the portable artefact (one file, no marker beside it) stores its data next to itself.

    What it CANNOT prove and therefore names as open points:
      * "USE" in the sense of chapter 62 - a person working through analysis and maintenance,
      * the reboot, the upgrade from an older version, the repair installation and the installation on
        D:, E: and F: (chapter 74).

.PARAMETER Installer
    Setup executable to test. Default: artifacts/release/WindowsMaintenanceCenter-Setup-x64.exe.

.PARAMETER PortableArtefact
    The shipped single-file artefact. Default: artifacts/release/WindowsMaintenanceCenter-Portable-x64.exe.

.PARAMETER PublishedProgram
    The published executable the installer was built from (artifacts/install), used for the SHA-256
    comparison. Default: artifacts/install/WindowsMaintenanceCenter.exe.

.PARAMETER StartWaitSeconds
    How long a started program gets before it is asked to close. Default 20.

.EXAMPLE
    pwsh ./scripts/vm/Test-InstallerCycle.ps1 -StartedBy 'ci-runner'

.NOTES
    Runs on the CI machine and on the target VM with the same evidence format (chapter 71).
#>
[CmdletBinding()]
param(
    [string]$Installer,
    [string]$PortableArtefact,
    [string]$PublishedProgram,
    [int]$StartWaitSeconds = 20,

    # Upper limit for the setup and the uninstaller. A hanging installer must end as a finding in the
    # report, not as a step that runs until the job is killed.
    [int]$InstallBudgetSeconds = 300,
    [string]$StartedBy = $env:USERNAME
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/Evidence.ps1"

$root = Get-WmcRepositoryRoot
if (-not $Installer) { $Installer = Join-Path $root 'artifacts/release/WindowsMaintenanceCenter-Setup-x64.exe' }
if (-not $PortableArtefact) { $PortableArtefact = Join-Path $root 'artifacts/release/WindowsMaintenanceCenter-Portable-x64.exe' }
if (-not $PublishedProgram) { $PublishedProgram = Join-Path $root 'artifacts/install/WindowsMaintenanceCenter.exe' }

$logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("wmc-inscycle-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

$findings = New-Object System.Collections.Generic.List[string]
$openPoints = New-Object System.Collections.Generic.List[string]
$steps = New-Object System.Collections.Generic.List[object]

function Add-Step {
    param([string]$Name, [string]$Result, [string]$Detail = '')
    $steps.Add([pscustomobject]@{ step = $Name; result = $Result; detail = $Detail })
    $colour = switch ($Result) {
        'PASS' { 'Green' }
        'FAIL' { 'Red' }
        default { 'Yellow' }
    }

    Write-Host ("  {0,-9} {1} {2}" -f $Result, $Name, $Detail) -ForegroundColor $colour
}

function Wait-ProcessBounded {
    <#
        Waits for a process at most $Seconds and says whether it ended. Why not "Start-Process -Wait":
        that waits forever, and the first run of this script on the CI machine (run 35695298315) stood
        still until somebody cancelled it - a test that cannot end is not a test. A process that runs
        longer than its budget is killed and reported.
    #>
    param([Parameter(Mandatory)]$Process, [Parameter(Mandatory)][int]$Seconds)

    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) { return $true }
        Start-Sleep -Milliseconds 500
        $Process.Refresh()
    }

    return $Process.HasExited
}

function Get-DataRoots {
    <#
        The folders the application may write to. Installed mode uses the machine data folder with a
        fallback to the user profile (PathProvider), portable mode uses "data" next to the executable.
        Both are probed, so the report says which one the program really chose.
    #>
    param([string]$ExecutableDirectory)
    return @(
        (Join-Path $env:ProgramData 'WindowsMaintenanceCenter'),
        (Join-Path $env:LOCALAPPDATA 'WindowsMaintenanceCenter'),
        (Join-Path $ExecutableDirectory 'data')
    )
}

function Get-StartMenuShortcuts {
    $roots = @(
        (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'),
        (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')
    )
    $found = @()
    foreach ($startMenuRoot in $roots) {
        if (Test-Path $startMenuRoot) {
            $found += Get-ChildItem -Path $startMenuRoot -Recurse -Filter '*Maintenance Center*.lnk' -ErrorAction SilentlyContinue
        }
    }
    return $found
}

if (-not (Test-Path -LiteralPath $Installer)) {
    throw "the installer $Installer does not exist - there is nothing to test (build the release first)"
}

$run = New-WmcEvidenceRun -Area 'installer' -TestId 'INS-CI' -Title 'Install, start, use probe, uninstall, portable probe' -StartedBy $StartedBy

foreach ($file in @($Installer, $PortableArtefact, $PublishedProgram)) {
    if (Test-Path -LiteralPath $file) {
        $hash = (Get-FileHash -Algorithm SHA256 -Path $file).Hash.ToLowerInvariant()
        Add-WmcMeasurement -Run $run -Name "sha256:$([System.IO.Path]::GetFileName($file))" -Value $hash -Source 'Get-FileHash SHA256'
    } else {
        Add-WmcMeasurement -Run $run -Name "sha256:$([System.IO.Path]::GetFileName($file))" -Value 'not present' -Source 'file system'
    }
}
Add-WmcMeasurement -Run $run -Name 'isAdministrator' -Value $run.Environment.isAdministrator -Source 'WindowsPrincipal'

# The uninstall entry is the honest source for "where did it go": reading it back instead of assuming
# a path keeps this test valid when the installer layout changes.
$appId = '{9F1C2A34-6F5B-4A72-9E3D-2D7A6A1C5B10}_is1'
$uninstallKeys = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$appId",
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$appId",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\$appId"
)

function Get-UninstallEntry {
    foreach ($key in $uninstallKeys) {
        if (Test-Path $key) {
            $entry = Get-ItemProperty -Path $key
            return [pscustomobject]@{
                Key             = $key
                InstallLocation = $entry.InstallLocation
                DisplayName     = $entry.DisplayName
                DisplayVersion  = $entry.DisplayVersion
                Publisher       = $entry.Publisher
                UninstallString = $entry.UninstallString
                EstimatedSize   = $entry.EstimatedSize
            }
        }
    }
    return $null
}

# ------------------------------------------------------------------------ 1. BEFORE
$beforeEntry = Get-UninstallEntry
if ($beforeEntry) {
    Add-Step 'precondition: nothing installed yet' 'FAIL' "an uninstall entry already exists: $($beforeEntry.Key)"
    $findings.Add('the machine already carried an installation of this product; the cycle would not start clean')
} else {
    Add-Step 'precondition: nothing installed yet' 'PASS' 'no uninstall entry for this product'
}

# ------------------------------------------------------------------------ 2. INSTALL
$installLog = Join-Path $logDirectory 'install.log'
$installArgs = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NOCANCEL', "/LOG=$installLog")
Write-Host '[INS-CI] installing silently' -ForegroundColor Cyan
$install = Start-Process -FilePath $Installer -ArgumentList $installArgs -PassThru
$installEnded = Wait-ProcessBounded -Process $install -Seconds $InstallBudgetSeconds
if (-not $installEnded) {
    Stop-Process -Id $install.Id -Force -ErrorAction SilentlyContinue
    Add-Step 'install (silent)' 'FAIL' "the setup did not finish within $InstallBudgetSeconds s and was ended"
    $findings.Add("the setup did not finish within $InstallBudgetSeconds s (budget per chapter 71: a test that cannot end is not a test)")
    Add-WmcMeasurement -Run $run -Name 'installExitCode' -Value 'not finished within budget' -Source 'setup /VERYSILENT'
} else {
    Add-WmcMeasurement -Run $run -Name 'installExitCode' -Value $install.ExitCode -Source 'setup /VERYSILENT'
    if ($install.ExitCode -eq 0) {
        Add-Step 'install (silent)' 'PASS' 'setup returned 0'
    } else {
        Add-Step 'install (silent)' 'FAIL' "setup returned $($install.ExitCode)"
        $findings.Add("the setup failed with exit code $($install.ExitCode)")
    }
}

# ------------------------------------------------------------------------ 3. LAYOUT
$entry = Get-UninstallEntry
$installRoot = $null
if (-not $entry) {
    Add-Step 'uninstall entry' 'FAIL' 'no uninstall entry after the install'
    $findings.Add('the installation left no uninstall entry, so it cannot be removed by the user')
} else {
    Add-Step 'uninstall entry' 'PASS' "$($entry.DisplayName) $($entry.DisplayVersion) in $($entry.Key)"
    Add-WmcMeasurement -Run $run -Name 'installLocation' -Value $entry.InstallLocation -Source 'uninstall entry'
    Add-WmcMeasurement -Run $run -Name 'displayVersion' -Value $entry.DisplayVersion -Source 'uninstall entry'
    Add-WmcMeasurement -Run $run -Name 'publisher' -Value $entry.Publisher -Source 'uninstall entry'
    Add-WmcMeasurement -Run $run -Name 'estimatedSizeKb' -Value $entry.EstimatedSize -Source 'uninstall entry'
    $installRoot = $entry.InstallLocation
}

$installedExe = if ($installRoot) { Join-Path $installRoot 'WindowsMaintenanceCenter.exe' } else { $null }
if ($installedExe -and (Test-Path -LiteralPath $installedExe)) {
    $installedHash = (Get-FileHash -Algorithm SHA256 -Path $installedExe).Hash.ToLowerInvariant()
    $installedVersion = (Get-Item $installedExe).VersionInfo.FileVersion
    Add-WmcMeasurement -Run $run -Name 'installedExeSha256' -Value $installedHash -Source 'Get-FileHash SHA256'
    Add-WmcMeasurement -Run $run -Name 'installedFileVersion' -Value $installedVersion -Source 'FileVersionInfo'

    if (Test-Path -LiteralPath $PublishedProgram) {
        $publishedHash = (Get-FileHash -Algorithm SHA256 -Path $PublishedProgram).Hash.ToLowerInvariant()
        if ($publishedHash -eq $installedHash) {
            Add-Step 'installed program is the built one' 'PASS' 'SHA-256 equals the published program'
        } else {
            Add-Step 'installed program is the built one' 'FAIL' 'SHA-256 differs from the published program'
            $findings.Add('the installed executable is not byte-identical to the published one')
        }
    } else {
        Add-Step 'installed program is the built one' 'SKIPPED' "no published program at $PublishedProgram"
        $openPoints.Add('the installed executable could not be compared to the published program (not present on this machine)')
    }

    if ($installedVersion -and $installedVersion.StartsWith($run.Version.version)) {
        Add-Step 'installed version matches the build' 'PASS' $installedVersion
    } else {
        Add-Step 'installed version matches the build' 'FAIL' "file version $installedVersion vs. build $($run.Version.version)"
        $findings.Add("the installed file carries version '$installedVersion' instead of '$($run.Version.version)'")
    }

    # An installed copy must not carry the portable marker next to it and must not behave portable.
    if (Test-Path -LiteralPath (Join-Path $installRoot 'WindowsMaintenanceCenter.portable')) {
        Add-Step 'installed copy is not portable' 'FAIL' 'the marker file was installed'
        $findings.Add('the portable marker file was installed into the program folder')
    } elseif (Test-Path -LiteralPath (Join-Path $installRoot 'data')) {
        Add-Step 'installed copy is not portable' 'FAIL' 'a data folder exists next to the installed program'
        $findings.Add('a data folder exists next to the installed program before it was even started')
    } else {
        Add-Step 'installed copy is not portable' 'PASS' 'no marker and no data folder beside the program'
    }
} else {
    Add-Step 'installed program file' 'FAIL' "no program at $installedExe"
    $findings.Add('the installation produced no executable at the location its own uninstall entry names')
}

$shortcuts = Get-StartMenuShortcuts
if ($shortcuts.Count -gt 0) {
    Add-Step 'Start menu entry' 'PASS' (($shortcuts | ForEach-Object { $_.FullName }) -join '; ')
} else {
    Add-Step 'Start menu entry' 'FAIL' 'no shortcut found'
    $findings.Add('the installation created no Start menu entry')
}

# ------------------------------------------------------------------------ 4. START and use probe
if ($installedExe -and (Test-Path -LiteralPath $installedExe)) {
    $process = Start-Process -FilePath $installedExe -PassThru
    Start-Sleep -Seconds $StartWaitSeconds

    if ($process.HasExited) {
        Add-Step 'program starts' 'FAIL' "the program ended after $StartWaitSeconds s with exit code $($process.ExitCode)"
        $findings.Add("the installed program did not stay running (exit code $($process.ExitCode))")
    } else {
        Add-Step 'program starts' 'PASS' "still running after $StartWaitSeconds s (pid $($process.Id))"
        Add-WmcMeasurement -Run $run -Name 'startedProcessId' -Value $process.Id -Source 'Process'
        Add-WmcMeasurement -Run $run -Name 'workingSetBytes' -Value $process.WorkingSet64 -Source 'Process'

        $seen = @()
        foreach ($candidate in (Get-DataRoots -ExecutableDirectory $installRoot)) {
            if (Test-Path -LiteralPath $candidate) {
                $logDirectory = Join-Path $candidate 'logs'
                $logs = @()
                if (Test-Path $logDirectory) {
                    $logs = @(Get-ChildItem -Path $logDirectory -Filter '*.log' -ErrorAction SilentlyContinue |
                        Sort-Object LastWriteTime -Descending | Select-Object -First 5)
                }
                $seen += [pscustomobject]@{ path = $candidate; logs = $logs }
                Add-WmcMeasurement -Run $run -Name 'dataFolderAfterStart' -Value "$candidate ($($logs.Count) log file(s))" -Source 'file system'
            }
        }

        $machineData = $seen | Where-Object { $_.path -like "$env:ProgramData*" }
        $besideProgram = $seen | Where-Object { $_.path -like "$installRoot*" }

        if (-not $machineData) {
            Add-Step 'use probe: machine data folder' 'FAIL' 'the program created no folder below %ProgramData%'
            $findings.Add('the installed program did not create its data folder below %ProgramData%')
        } elseif ($machineData.logs.Count -gt 0) {
            Add-Step 'use probe: log written' 'PASS' $machineData.path
            $newest = $machineData.logs | Sort-Object LastWriteTime -Descending | Select-Object -First 1
            $tail = Get-Content -Path $newest.FullName -Tail 30 -ErrorAction SilentlyContinue
            Write-WmcEvidenceText -Run $run -Name "startup-$($newest.Name).txt" -Lines @($tail) `
                -Description 'last lines of the log the started program wrote' | Out-Null
        } else {
            Add-Step 'use probe: log written' 'FAIL' 'no log file appeared'
            $findings.Add('the program created no log file, so its startup work is not documented')
        }

        if ($besideProgram) {
            Add-Step 'use probe: installed copy stays out of its own folder' 'FAIL' 'the program wrote data next to its executable'
            $findings.Add('the installed program wrote into its program folder instead of the data folder')
        } else {
            Add-Step 'use probe: installed copy stays out of its own folder' 'PASS' 'nothing was written next to the executable'
        }

        $closed = $false
        try {
            $closed = $process.CloseMainWindow() -and $process.WaitForExit(15000)
        } catch {
            $closed = $process.HasExited
        }

        if ($closed) {
            Add-Step 'program closes' 'PASS' "exit code $($process.ExitCode)"
        } else {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            Add-Step 'program closes' 'NOT VERIFIED' 'the program did not close on request - it was ended'
            $openPoints.Add('the started program did not close on request within 15 s (a session without a user cannot use the close button)')
        }
    }
} else {
    Add-Step 'program starts' 'SKIPPED' 'there is no installed program to start'
}

# ------------------------------------------------------------------------ 5. UNINSTALL
$uninstallLog = Join-Path $logDirectory 'uninstall.log'
if ($entry -and $entry.UninstallString) {
    $uninstaller = ($entry.UninstallString -replace '"', '').Trim()
    # Inno's uninstaller copies itself into the temporary folder and starts that copy. The process
    # started here therefore ends early; the uninstall itself is checked afterwards by looking at the
    # registry, the files and the shortcuts - that is the proof, not the exit code.
    $uninstall = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=$uninstallLog") -PassThru
    if (-not (Wait-ProcessBounded -Process $uninstall -Seconds $InstallBudgetSeconds)) {
        Stop-Process -Id $uninstall.Id -Force -ErrorAction SilentlyContinue
        Add-Step 'uninstall (silent)' 'FAIL' "the uninstaller did not finish within $InstallBudgetSeconds s"
        $findings.Add("the uninstaller did not finish within $InstallBudgetSeconds s")
    } else {
        Add-Step 'uninstall (silent)' 'PASS' (Split-Path $uninstaller -Leaf)
    }
} else {
    Add-Step 'uninstall (silent)' 'FAIL' 'no uninstall string to run'
    $findings.Add('there was no uninstall command, so the installation could not be removed')
}

Start-Sleep -Seconds 3
$afterEntry = Get-UninstallEntry
if ($afterEntry) {
    Add-Step 'uninstall entry removed' 'FAIL' "still there: $($afterEntry.Key)"
    $findings.Add('the uninstall left its own entry behind')
} else {
    Add-Step 'uninstall entry removed' 'PASS' 'no entry left in the uninstall registry'
}

if ($installRoot -and (Test-Path -LiteralPath $installRoot)) {
    $leftovers = @(Get-ChildItem -Path $installRoot -Recurse -File -ErrorAction SilentlyContinue)
    if ($leftovers.Count -eq 0) {
        Add-Step 'program files removed' 'PASS' 'the program folder is empty'
    } else {
        Add-Step 'program files removed' 'FAIL' "$($leftovers.Count) file(s) left in $installRoot"
        $findings.Add("$($leftovers.Count) file(s) stayed in the installation folder")
        Write-WmcEvidenceText -Run $run -Name 'leftovers.txt' -Lines @($leftovers | ForEach-Object { $_.FullName }) `
            -Description 'files that survived the uninstall' | Out-Null
    }
} else {
    Add-Step 'program files removed' 'PASS' 'the program folder is gone'
}

$shortcutsAfter = Get-StartMenuShortcuts
if ($shortcutsAfter.Count -eq 0) {
    Add-Step 'Start menu entry removed' 'PASS' 'no shortcut left'
} else {
    Add-Step 'Start menu entry removed' 'FAIL' "$($shortcutsAfter.Count) shortcut(s) left"
    $findings.Add('the uninstall left a Start menu entry behind')
}

$kept = @()
foreach ($candidate in @(
    (Join-Path $env:ProgramData 'WindowsMaintenanceCenter'),
    (Join-Path $env:LOCALAPPDATA 'WindowsMaintenanceCenter'))) {
    if (Test-Path -LiteralPath $candidate) {
        $count = @(Get-ChildItem -Path $candidate -Recurse -File -ErrorAction SilentlyContinue).Count
        $kept += "$candidate ($count file(s))"
        Add-WmcMeasurement -Run $run -Name 'dataKeptAfterUninstall' -Value $candidate -Source 'file system after uninstall'
    }
}
if ($kept.Count -gt 0) {
    Add-Step 'application data after uninstall' 'PASS' ((($kept -join '; ')) + ' - kept on purpose, the uninstaller asks before deleting')
    $openPoints.Add('settings and audit trail stay on the machine after an uninstall; deleting them is the user''s decision at uninstall time')
} else {
    Add-Step 'application data after uninstall' 'PASS' 'no data folder existed'
}

# ------------------------------------------------------------------------ 6. PORTABLE artefact
# This is the check that was missing until 2026-09-22: the artefact handed out is ONE file, so its
# portability cannot depend on a file beside it. It is copied into an empty folder without a marker,
# started, and must keep its data next to itself.
if (Test-Path -LiteralPath $PortableArtefact) {
    $probeDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("wmc-portable-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force -Path $probeDirectory | Out-Null
    $probeExe = Join-Path $probeDirectory 'WindowsMaintenanceCenter-Portable-x64.exe'
    Copy-Item -LiteralPath $PortableArtefact -Destination $probeExe -Force

    try {
        $portableProcess = Start-Process -FilePath $probeExe -PassThru
        Start-Sleep -Seconds $StartWaitSeconds

        if ($portableProcess.HasExited) {
            Add-Step 'portable artefact starts' 'FAIL' "the portable program ended with exit code $($portableProcess.ExitCode)"
            $findings.Add("the portable artefact did not stay running (exit code $($portableProcess.ExitCode))")
        } else {
            Add-Step 'portable artefact starts' 'PASS' "still running after $StartWaitSeconds s (pid $($portableProcess.Id))"

            $portableData = Join-Path $probeDirectory 'data'
            if (Test-Path -LiteralPath $portableData) {
                $files = @(Get-ChildItem -Path $portableData -Recurse -File -ErrorAction SilentlyContinue)
                Add-Step 'portable artefact keeps its data next to itself' 'PASS' "$($files.Count) file(s) in data"
                Add-WmcMeasurement -Run $run -Name 'portableDataFiles' -Value $files.Count -Source 'file system'
                Add-WmcMeasurement -Run $run -Name 'portableDataFolder' -Value $portableData -Source 'file system'
            } else {
                Add-Step 'portable artefact keeps its data next to itself' 'FAIL' "no data folder appeared in $probeDirectory"
                $findings.Add('the portable artefact wrote nothing next to itself - it is not portable (see PortableMode)')
            }

            $portableClosed = $false
            try {
                $portableClosed = $portableProcess.CloseMainWindow() -and $portableProcess.WaitForExit(15000)
            } catch {
                $portableClosed = $portableProcess.HasExited
            }

            if (-not $portableClosed) {
                Stop-Process -Id $portableProcess.Id -Force -ErrorAction SilentlyContinue
            }
        }
    } finally {
        Remove-Item -LiteralPath $probeDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Add-Step 'portable artefact' 'SKIPPED' "no artefact at $PortableArtefact"
    $openPoints.Add('the portable artefact was not present, so its data location was not checked')
}

# ------------------------------------------------------------------------ 7. What stays open
$openPoints.Add('the reboot after the installation (chapter 62) was not performed by this script')
$openPoints.Add('the upgrade over an older version and the repair installation need an older installer to upgrade from')
$openPoints.Add('the installation on D:, E: and F: (chapter 74) needs a machine that has those drives')
$openPoints.Add('"USE" in the sense of chapter 62 means a person working through analysis and maintenance in the interface')

$status = if ($findings.Count -gt 0) { 'FAILED' } else { 'PASSED' }
$summary = if ($findings.Count -gt 0) {
    "Installation cycle FAILED at $($findings.Count) point(s)"
} else {
    'Installation cycle PASSED: install, layout, start, use probe, uninstall (data kept on purpose) and the portable artefact stores its data next to itself'
}

$transcript = $steps | ForEach-Object { "{0,-9} {1} {2}" -f $_.result, $_.step, $_.detail }
Write-WmcEvidenceText -Run $run -Name 'steps.txt' -Lines @($transcript) -Description 'every step of the cycle with its result' | Out-Null

if (Test-Path $installLog) { Add-WmcEvidenceFile -Run $run -Path $installLog -Description 'Inno Setup install log' | Out-Null }
if (Test-Path $uninstallLog) { Add-WmcEvidenceFile -Run $run -Path $uninstallLog -Description 'Inno Setup uninstall log' | Out-Null }

Complete-WmcEvidenceRun -Run $run -Status $status -Summary $summary `
    -Findings $findings.ToArray() -OpenPoints $openPoints.ToArray() | Out-Null

if ($status -eq 'FAILED') { exit 1 }
exit 0
