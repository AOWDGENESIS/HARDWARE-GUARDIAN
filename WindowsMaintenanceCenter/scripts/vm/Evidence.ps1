<#
    Shared helpers for the VM acceptance kit (WindowsMaintenanceCenter/scripts/vm).

    Why this file exists:
    The hosted CI runner has no sensors, no battery, no UAC prompt and no reboot. Everything the
    specification wants proven on the target machine has to be collected there - and a collected
    value is only worth something when the record says who measured it, on which machine, with which
    build, and what was not measurable. Chapter 71 of the specification therefore demands five
    fields (timestamp, version, build, environment, result) in every report, and chapter 5 says a
    test without proof counts as NOT VERIFIED.

    The rules this library enforces for every kit script:
      * Nothing is reported as PASSED that was not measured. A missing prerequisite (no sensor, no
        battery, no administrator rights) produces BLOCKED with the reason.
      * A value that could not be read is NOT VERIFIED, never zero and never "OK".
      * Every report carries the environment record, so a reader can see that e.g. a sensor test ran
        on a machine without a thermal zone.
      * Evidence files are copied next to the report and hashed, so the report can be checked later.

    Usage in a kit script:
        . "$PSScriptRoot/Evidence.ps1"
        $run = New-WmcEvidenceRun -Area 'integration' -TestId 'M08-R-001' -Title 'Sensors on real hardware'
        ... measure ...
        Add-WmcEvidenceFile -Run $run -Path $someLogPath
        Complete-WmcEvidenceRun -Run $run -Status 'BLOCKED' -Summary 'no thermal zone on this machine'
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The status values of the specification. NOT VERIFIED is not a state of the application; it is the
# verdict for a test that has no proof (chapter 5), and it is intentionally part of this list.
$script:WmcEvidenceStatus = @('PASSED', 'FAILED', 'BLOCKED', 'NOT VERIFIED')

# Every query against a WMI provider is bounded. Why: the first run of the installation cycle on the CI
# machine (run 35695298315) never finished - the step stood still for more than ten minutes in this
# environment record, and a provider that does not answer has to become a line in the record, not a
# hang. 20 seconds is far above a normal answer and far below a hopeful wait.
$script:WmcCimTimeoutSeconds = 20

function Get-WmcRepositoryRoot {
    <#
        The kit scripts live in scripts/vm/, so the repository root is two levels above them.
    #>
    [CmdletBinding()]
    param()
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}

function Get-WmcVersionRecord {
    <#
        Product version and build identity, read from eng/Version.props - the single source of truth
        in this repository. Never invented: when the file cannot be read, the field says so.
    #>
    [CmdletBinding()]
    param()

    $props = Join-Path (Get-WmcRepositoryRoot) 'eng/Version.props'
    $record = [ordered]@{
        version   = 'unknown (eng/Version.props was not readable)'
        product   = 'unknown'
        build     = 'unknown'
        buildDate = 'unspecified'
    }

    if (Test-Path $props) {
        $text = Get-Content -Raw $props
        if ($text -match '<VersionPrefix>(.*?)</VersionPrefix>') {
            $version = $Matches[1].Trim()
            if ($text -match '<VersionSuffix>(.*?)</VersionSuffix>' -and -not [string]::IsNullOrWhiteSpace($Matches[1])) {
                $version = "$version-$($Matches[1].Trim())"
            }
            $record.version = $version
        }
        if ($text -match '<ProductName>(.*?)</ProductName>') { $record.product = $Matches[1].Trim() }
    }

    # The build is the commit the kit was taken from when the kit runs inside a checkout. On a target
    # machine that runs a copied release directory there is no checkout, and then the build stays
    # "unknown" instead of being guessed from a file name.
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) {
        try {
            $revision = (& git -C (Get-WmcRepositoryRoot) rev-parse HEAD 2>$null | Select-Object -First 1)
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($revision)) {
                $record.build = $revision.Trim()
            }
        } catch {
            # Not a checkout, or git refused. "unknown" is the honest answer and stays.
        }
    }

    return $record
}

function Get-WmcEnvironmentRecord {
    <#
        The machine the evidence was collected on. Every value is measured here; what cannot be
        measured is named as not readable together with the reason, because an absent value in a
        report must not be mistaken for an absent problem.
    #>
    [CmdletBinding()]
    param()

    $probe = [ordered]@{}

    function Read-Value([string]$Name, [scriptblock]$Body) {
        try {
            $probe[$Name] = & $Body
        } catch {
            $probe[$Name] = "not readable ($($_.Exception.GetType().Name): $($_.Exception.Message))"
        }
    }

    Read-Value 'computer' { "$env:COMPUTERNAME (user $env:USERNAME)" }
    Read-Value 'operatingSystem' {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop -OperationTimeoutSec $script:WmcCimTimeoutSeconds
        "$($os.Caption) version=$($os.Version) build=$($os.BuildNumber) arch=$($os.OSArchitecture)"
    }
    Read-Value 'machine' {
        $system = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop -OperationTimeoutSec $script:WmcCimTimeoutSeconds
        "$($system.Manufacturer) $($system.Model) hypervisorPresent=$($system.HypervisorPresent)"
    }
    Read-Value 'bios' {
        $bios = Get-CimInstance Win32_BIOS -ErrorAction Stop -OperationTimeoutSec $script:WmcCimTimeoutSeconds
        "$($bios.Manufacturer) $($bios.SMBIOSBIOSVersion) released=$($bios.ReleaseDate)"
    }
    Read-Value 'cpu' {
        $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop -OperationTimeoutSec $script:WmcCimTimeoutSeconds | Select-Object -First 1
        "$($cpu.Name) cores=$($cpu.NumberOfCores) logical=$($cpu.NumberOfLogicalProcessors)"
    }
    Read-Value 'memoryGb' {
        [math]::Round((Get-CimInstance Win32_ComputerSystem -OperationTimeoutSec $script:WmcCimTimeoutSeconds).TotalPhysicalMemory / 1GB, 1)
    }
    Read-Value 'powerShell' { $PSVersionTable.PSVersion.ToString() }
    Read-Value 'dotnet' {
        $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $dotnet) { return 'not installed' }
        (& dotnet --version)
    }
    Read-Value 'isAdministrator' {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    Read-Value 'secureBoot' {
        # Available on UEFI with administrator rights only; a BIOS/legacy machine answers with an
        # error, and that error is the answer.
        (Confirm-SecureBootUEFI)
    }
    Read-Value 'tpm' {
        $tpm = Get-CimInstance -Namespace 'root/cimv2/security/microsofttpm' -ClassName Win32_Tpm -ErrorAction Stop -OperationTimeoutSec $script:WmcCimTimeoutSeconds
        "present spec=$($tpm.SpecVersion) enabled=$($tpm.IsEnabled_InitialValue) activated=$($tpm.IsActivated_InitialValue)"
    }
    Read-Value 'dataCenterRegion' { 'not collected (no telemetry, and this kit never sends anything anywhere)' }

    # What a machine of this kind commonly cannot provide. The scripts check these properties
    # themselves; the record states them up front so a reader does not have to guess.
    Read-Value 'battery' {
        $batteries = @(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue -OperationTimeoutSec $script:WmcCimTimeoutSeconds)
        if ($batteries.Count -eq 0) { 'none' } else { "$($batteries.Count) device(s)" }
    }
    Read-Value 'thermalZones' {
        $zones = @(Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction Stop -OperationTimeoutSec $script:WmcCimTimeoutSeconds)
        "$($zones.Count) zone(s)"
    }
    Read-Value 'videoController' {
        @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue -OperationTimeoutSec $script:WmcCimTimeoutSeconds | ForEach-Object { $_.Name }) -join ', '
    }
    Read-Value 'diskDrives' {
        @(Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue -OperationTimeoutSec $script:WmcCimTimeoutSeconds | ForEach-Object { "$($_.Model) ($($_.Size) bytes)" }) -join ' | '
    }

    return [pscustomobject]$probe
}

function New-WmcEvidenceRun {
    <#
        Creates the folder for one test: test-results/<area>/<utc>-<testId>/ with an environment
        record. The folder name carries the test id so that a reader finds a test result without
        opening every file.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][ValidateSet('unit', 'integration', 'safety', 'security', 'recovery',
            'installer', 'localization', 'offline', 'regression', 'release')][string]$Area,
        [Parameter(Mandatory)][string]$TestId,
        [Parameter(Mandatory)][string]$Title,
        [string]$StartedBy = $env:USERNAME
    )

    $timestamp = (Get-Date).ToUniversalTime()
    $safeId = $TestId -replace '[^A-Za-z0-9\-]', '_'
    $folder = Join-Path (Get-WmcRepositoryRoot) "test-results/$Area/$($timestamp.ToString('yyyyMMddTHHmmssZ'))-$safeId"
    New-Item -ItemType Directory -Force -Path (Join-Path $folder 'logs') | Out-Null

    $environment = Get-WmcEnvironmentRecord
    $environment | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 (Join-Path $folder 'environment.json')

    $run = [pscustomobject]@{
        Area        = $Area
        TestId      = $TestId
        Title       = $Title
        Folder      = $folder
        StartedAt   = $timestamp
        StartedBy   = $StartedBy
        Environment = $environment
        Version     = Get-WmcVersionRecord
        Evidence    = New-Object System.Collections.Generic.List[object]
        Measurements = New-Object System.Collections.Generic.List[object]
    }

    Write-Host "[$TestId] preparing $Area evidence in $folder" -ForegroundColor Cyan
    return $run
}

function Add-WmcEvidenceFile {
    <#
        Copies a file produced by the test (log, report, screenshot, installer log) next to the
        report and records its SHA-256, so the proof can be checked instead of trusted.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Run,
        [Parameter(Mandatory)][string]$Path,
        [string]$Description = ''
    )

    if (-not (Test-Path $Path)) {
        throw "the evidence file $Path does not exist - nothing was recorded"
    }

    $item = Get-Item $Path
    $target = Join-Path (Join-Path $Run.Folder 'logs') $item.Name
    Copy-Item -Path $item.FullName -Destination $target -Force
    $hash = (Get-FileHash -Algorithm SHA256 -Path $target).Hash.ToLowerInvariant()

    $Run.Evidence.Add([pscustomobject]@{
        file        = "logs/$($item.Name)"
        description = $Description
        sha256      = $hash
        bytes       = $item.Length
    })

    Write-Host "[$($Run.TestId)] evidence: logs/$($item.Name) sha256=$hash" -ForegroundColor DarkGray
    return $target
}

function Add-WmcMeasurement {
    <#
        Records one measured value. A measurement is a number or text that was read at this moment -
        not a conclusion. Conclusions belong into the summary of the report.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Run,
        [Parameter(Mandatory)][string]$Name,
        $Value,
        [string]$Source = '',
        [string]$Unit = ''
    )

    $Run.Measurements.Add([pscustomobject]@{
        name   = $Name
        value  = $Value
        unit   = $Unit
        source = $Source
    })
    Write-Host ("  {0} = {1}" -f $Name, $Value) -ForegroundColor DarkGray
}

function Write-WmcEvidenceText {
    <#
        Writes a plain text file into the run folder and registers it as evidence. Used for the
        human readable protocol that goes with the JSON report.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Run,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string[]]$Lines,
        [string]$Description = ''
    )

    $path = Join-Path $Run.Folder $Name
    $Lines | Set-Content -Encoding utf8 $path
    return (Add-WmcEvidenceFile -Run $Run -Path $path -Description $Description)
}

function Complete-WmcEvidenceRun {
    <#
        Writes report.json and report.txt for the run.

        The status has to be handed in by the test itself; this function never decides on its own.
        What it does enforce is the vocabulary: only PASSED, FAILED, BLOCKED and NOT VERIFIED exist,
        and a PASSED report without a single measurement or evidence file is refused - a success
        claim that nothing was measured for is exactly what chapter 86 forbids.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Run,
        [Parameter(Mandatory)][ValidateScript({ $script:WmcEvidenceStatus -contains $_ })][string]$Status,
        [Parameter(Mandatory)][string]$Summary,
        [string[]]$Findings = @(),
        [string[]]$OpenPoints = @()
    )

    if ($Status -eq 'PASSED' -and $Run.Evidence.Count -eq 0 -and $Run.Measurements.Count -eq 0) {
        throw "run $($Run.TestId) claims PASSED without a single piece of evidence or measurement"
    }

    # Everything below runs inside a guard that names the *statement* which failed. Run 35697744225
    # ended with "Argument types do not match" and the position of the call, not of the cause - a tool
    # that reports a failure has to say where it happened, otherwise the reader has to guess.
    try {

    $finished = (Get-Date).ToUniversalTime()
    $result = [ordered]@{
        status   = $Status
        summary  = $Summary
        findings = @($Findings)
        open     = @($OpenPoints)
    }

    $report = [ordered]@{
        timestamp   = $finished.ToString('yyyy-MM-ddTHH:mm:ssZ')
        testId      = $Run.TestId
        title       = $Run.Title
        area        = $Run.Area
        version     = $Run.Version.version
        build       = $Run.Version.build
        environment = $Run.Environment
        startedAt   = $Run.StartedAt.ToString('yyyy-MM-ddTHH:mm:ssZ')
        finishedAt  = $finished.ToString('yyyy-MM-ddTHH:mm:ssZ')
        startedBy   = $Run.StartedBy
        result      = $result
        evidence    = @($Run.Evidence)
        measurements = @($Run.Measurements)
    }

    $jsonPath = Join-Path $Run.Folder 'report.json'
    $report | ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 $jsonPath

    $text = New-Object System.Collections.Generic.List[string]
    $text.Add("test        : $($Run.TestId) - $($Run.Title)")
    $text.Add("area        : $($Run.Area)")
    $text.Add("version     : $($Run.Version.version)")
    $text.Add("build       : $($Run.Version.build)")
    $text.Add("started     : $($Run.StartedAt.ToString('yyyy-MM-ddTHH:mm:ssZ')) by $($Run.StartedBy)")
    $text.Add("finished    : $($finished.ToString('yyyy-MM-ddTHH:mm:ssZ'))")
    $text.Add("machine     : $($Run.Environment.machine)")
    $text.Add("os          : $($Run.Environment.operatingSystem)")
    $text.Add("administrator: $($Run.Environment.isAdministrator)")
    $text.Add("result      : $Status")
    $text.Add("summary     : $Summary")
    if ($Findings.Count -gt 0) {
        $text.Add('findings    :')
        foreach ($finding in $Findings) { $text.Add("  - $finding") }
    }
    if ($OpenPoints.Count -gt 0) {
        $text.Add('open        :')
        foreach ($open in $OpenPoints) { $text.Add("  - $open") }
    }
    if ($Run.Measurements.Count -gt 0) {
        $text.Add('measurements:')
        foreach ($measurement in $Run.Measurements) {
            $unit = if ([string]::IsNullOrWhiteSpace($measurement.unit)) { '' } else { " $($measurement.unit)" }
            $source = if ([string]::IsNullOrWhiteSpace($measurement.source)) { '' } else { "  [$($measurement.source)]" }
            $text.Add("  $($measurement.name) = $($measurement.value)$unit$source")
        }
    }
    if ($Run.Evidence.Count -gt 0) {
        $text.Add('evidence    :')
        foreach ($entry in $Run.Evidence) {
            $text.Add("  $($entry.file)  sha256=$($entry.sha256)")
        }
    }
    $textPath = Join-Path $Run.Folder 'report.txt'
    $text | Set-Content -Encoding utf8 $textPath

    $colour = switch ($Status) {
        'PASSED' { 'Green' }
        'FAILED' { 'Red' }
        'BLOCKED' { 'Yellow' }
        default { 'DarkYellow' }
    }
    Write-Host "[$($Run.TestId)] $Status - $Summary" -ForegroundColor $colour
    Write-Host "  report: $jsonPath" -ForegroundColor DarkGray
    return [pscustomobject]@{ Status = $Status; Report = $jsonPath; Text = $textPath }

    } catch {
        $where = $_.InvocationInfo
        $line = if ($where -and -not [string]::IsNullOrWhiteSpace($where.Line)) { $where.Line.Trim() } else { '<no statement information>' }
        throw "Complete-WmcEvidenceRun failed in line $($where.ScriptLineNumber): $line - $($_.Exception.Message)"
    }
}
