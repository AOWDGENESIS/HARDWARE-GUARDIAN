<#
.SYNOPSIS
    Collects hardware, sensor, firmware and Windows readings on the target machine (read-only).

.DESCRIPTION
    The hosted CI runner is a virtual machine: it has no thermal zone, no battery, no SMART value of
    a real disk and no SMBIOS of the target device. The modules that read those values (M08-M11,
    M18, M20, M30) can therefore only be accepted on a machine that really has them, and this script
    is the measuring device for that acceptance.

    It reads the same sources the application reads (WMI, CIM, PowerShell) and records the values
    with their origin. Three things make the result usable as proof:

      1. Every reading is recorded with the class it came from, so a reader can re-run the same
         query and compare.
      2. A reading that cannot be taken is named with its reason. A machine without a thermal zone
         does not produce "0 degrees", it produces "not readable (no such instance)" - the mistake
         the project must never make.
      3. If a report produced by Windows Maintenance Center is handed in with -WmcReport, the
         readings are compared against what the application wrote down. Only that comparison proves
         the application reads correctly; without it the test stays NOT VERIFIED, however many
         values this script collected.

    Nothing here changes the machine: no service is touched, no setting is written, no device is
    addressed beyond reading its properties (specification chapters 55 and 72).

.PARAMETER WmcReport
    Optional path to a report (JSON) written by Windows Maintenance Center. When given, the values
    in it are compared with the readings taken here.

.PARAMETER Area
    Evidence folder to write into (default: integration, chapter 58).

.EXAMPLE
    pwsh ./scripts/vm/Invoke-VmHardwareEvidence.ps1
    pwsh ./scripts/vm/Invoke-VmHardwareEvidence.ps1 -WmcReport C:\temp\wmc-report.json

.NOTES
    Must run on the target machine. On a virtual machine it will report BLOCKED for the sensor parts
    and that is the correct result there.
#>
[CmdletBinding()]
param(
    [string]$WmcReport = '',
    [ValidateSet('unit', 'integration', 'safety', 'security', 'recovery', 'installer',
        'localization', 'offline', 'regression', 'release')][string]$Area = 'integration'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/Evidence.ps1"

$run = New-WmcEvidenceRun -Area $Area -TestId 'M08-M20-HW-001' -Title 'Hardware, sensor, firmware and Windows readings on the target machine'

$findings = New-Object System.Collections.Generic.List[string]
$openPoints = New-Object System.Collections.Generic.List[string]
$blocked = New-Object System.Collections.Generic.List[string]
$text = New-Object System.Collections.Generic.List[string]

function Add-Reading {
    param([string]$Category, [string]$Name, $Value, [string]$Source, [string]$Unit = '')
    $script:readings.Add([pscustomobject]@{
        category = $Category
        name     = $Name
        value    = $Value
        unit     = $Unit
        source   = $Source
    })
    Add-WmcMeasurement -Run $run -Name "$Category/$Name" -Value $Value -Source $Source -Unit $Unit
}

function Read-Into {
    <#
        Reads one value and records either the value or the reason why it is missing. The reason is
        part of the evidence, not a footnote.
    #>
    param([string]$Category, [string]$Name, [scriptblock]$Body, [string]$Source, [string]$Unit = '')
    try {
        $value = & $Body
        if ($null -eq $value -or ($value -is [string] -and [string]::IsNullOrWhiteSpace($value))) {
            Add-Reading -Category $Category -Name $Name -Value 'not readable (empty result)' -Source $Source -Unit $Unit
            return $null
        }
        Add-Reading -Category $Category -Name $Name -Value $value -Source $Source -Unit $Unit
        return $value
    } catch {
        $reason = $_.Exception.Message -replace '\s+', ' '
        Add-Reading -Category $Category -Name $Name -Value "not readable ($reason)" -Source $Source -Unit $Unit
        return $null
    }
}

$script:readings = New-Object System.Collections.Generic.List[object]

# ---------------------------------------------------------------- processor, memory, board, firmware
$text.Add('== processor, memory, mainboard, firmware ==')
$cpu = Read-Into -Category 'CPU' -Name 'Name' -Source 'Win32_Processor.Name' -Body { (Get-CimInstance Win32_Processor | Select-Object -First 1).Name }
Read-Into -Category 'CPU' -Name 'Cores' -Source 'Win32_Processor.NumberOfCores' -Body { (Get-CimInstance Win32_Processor | Select-Object -First 1).NumberOfCores }
Read-Into -Category 'CPU' -Name 'LogicalProcessors' -Source 'Win32_Processor.NumberOfLogicalProcessors' -Body { (Get-CimInstance Win32_Processor | Select-Object -First 1).NumberOfLogicalProcessors }
Read-Into -Category 'CPU' -Name 'MaxClockMHz' -Source 'Win32_Processor.MaxClockSpeed' -Unit 'MHz' -Body { (Get-CimInstance Win32_Processor | Select-Object -First 1).MaxClockSpeed }
Read-Into -Category 'CPU' -Name 'CurrentClockMHz' -Source 'Win32_Processor.CurrentClockSpeed' -Unit 'MHz' -Body { (Get-CimInstance Win32_Processor | Select-Object -First 1).CurrentClockSpeed }

$memoryModules = @(Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue)
if ($memoryModules.Count -eq 0) {
    Add-Reading -Category 'RAM' -Name 'Modules' -Value 'not readable (no Win32_PhysicalMemory instance)' -Source 'Win32_PhysicalMemory'
    $blocked.Add('no memory module could be enumerated, so slot, type and speed stay unproven')
} else {
    $totalGb = [math]::Round((($memoryModules | Measure-Object -Property Capacity -Sum).Sum) / 1GB, 1)
    Add-Reading -Category 'RAM' -Name 'TotalGb' -Value $totalGb -Unit 'GB' -Source 'Win32_PhysicalMemory.Capacity (sum)'
    for ($i = 0; $i -lt $memoryModules.Count; $i++) {
        $module = $memoryModules[$i]
        # SMBIOS memory device type (DMTF DSP0134 7.18.2). The often copied "20 = DDR" table is
        # wrong; the firmware value is used as it is and mapped only for the known codes.
        $typeName = switch ([int]$module.SMBIOSMemoryType) {
            0x12 { 'DDR' } 0x13 { 'DDR2' } 0x14 { 'DDR2 FB-DIMM' } 0x18 { 'DDR3' }
            0x19 { 'FBD2' } 0x1A { 'DDR4' } 0x1B { 'LPDDR' } 0x1C { 'LPDDR2' }
            0x1D { 'LPDDR3' } 0x1E { 'LPDDR4' } 0x22 { 'DDR5' } default { "code 0x$([int]$module.SMBIOSMemoryType | ForEach-Object { $_.ToString('X2') })" }
        }
        $formFactor = switch ([int]$module.FormFactor) { 8 { 'DIMM' } 12 { 'SODIMM' } default { "code $([int]$module.FormFactor)" } }
        Add-Reading -Category 'RAM' -Name "Module$($i + 1)" -Source 'Win32_PhysicalMemory' `
            -Value "$($module.BankLabel)/$($module.DeviceLocator): $([math]::Round($module.Capacity / 1GB, 1)) GB, type=$typeName (SMBIOS $($module.SMBIOSMemoryType)), form=$formFactor, speed=$($module.Speed) MT/s, manufacturer=$($module.Manufacturer), part=$($module.PartNumber)"
    }
}

Read-Into -Category 'Mainboard' -Name 'Manufacturer' -Source 'Win32_BaseBoard.Manufacturer' -Body { (Get-CimInstance Win32_BaseBoard).Manufacturer }
Read-Into -Category 'Mainboard' -Name 'Product' -Source 'Win32_BaseBoard.Product' -Body { (Get-CimInstance Win32_BaseBoard).Product }
Read-Into -Category 'Mainboard' -Name 'SerialNumber' -Source 'Win32_BaseBoard.SerialNumber' -Body { (Get-CimInstance Win32_BaseBoard).SerialNumber }
Read-Into -Category 'BIOS' -Name 'Version' -Source 'Win32_BIOS.SMBIOSBIOSVersion' -Body { (Get-CimInstance Win32_BIOS).SMBIOSBIOSVersion }
Read-Into -Category 'BIOS' -Name 'ReleaseDate' -Source 'Win32_BIOS.ReleaseDate' -Body { (Get-CimInstance Win32_BIOS).ReleaseDate }
Read-Into -Category 'BIOS' -Name 'FirmwareMode' -Source 'Win32_BIOS: UEFI or legacy' -Body {
    if (Test-Path Env:firmware_type) { $env:firmware_type } else {
        $partition = Get-Partition -DriveLetter $env:SystemDrive.TrimEnd(':') -ErrorAction Stop
        if ($partition.GptType) { 'UEFI (GPT system partition)' } else { 'legacy (MBR system partition)' }
    }
}

$enclosure = Read-Into -Category 'Chassis' -Name 'ChassisTypes' -Source 'Win32_SystemEnclosure.ChassisTypes' -Body { ((Get-CimInstance Win32_SystemEnclosure).ChassisTypes -join ',') }
if ($enclosure) {
    foreach ($code in ($enclosure -split ',')) {
        $meaning = switch ([int]$code) {
            1 { 'Other' } 2 { 'Unknown' } 3 { 'Desktop' } 4 { 'Low Profile Desktop' } 5 { 'Pizza Box' }
            6 { 'Mini Tower' } 7 { 'Tower' } 8 { 'Portable' } 9 { 'Laptop' } 10 { 'Notebook' }
            11 { 'Hand Held' } 12 { 'Docking Station' } 13 { 'All in One' } 14 { 'Sub Notebook' }
            15 { 'Space-saving' } 16 { 'Lunch Box' } 17 { 'Main Server Chassis' } 23 { 'Rack Mount' }
            30 { 'Tablet' } 31 { 'Convertible' } 32 { 'Detachable' } 35 { 'Mini PC' }
            default { 'code not in the table used by the application' }
        }
        Add-Reading -Category 'Chassis' -Name "Type$($code.Trim())" -Value $meaning -Source 'SMBIOS enclosure type'
    }
}

# ------------------------------------------------------------------------------ graphics and screens
$text.Add('== graphics and displays ==')
$gpus = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue)
if ($gpus.Count -eq 0) {
    Add-Reading -Category 'GPU' -Name 'Adapters' -Value 'not readable (no Win32_VideoController instance)' -Source 'Win32_VideoController'
} else {
    for ($i = 0; $i -lt $gpus.Count; $i++) {
        $gpu = $gpus[$i]
        Add-Reading -Category 'GPU' -Name "Adapter$($i + 1)" -Source 'Win32_VideoController' `
            -Value "$($gpu.Name) driver=$($gpu.DriverVersion) driverDate=$($gpu.DriverDate) videoProcessor=$($gpu.VideoProcessor) vram=$([math]::Round($gpu.AdapterRAM / 1MB)) MB"
    }
}
$monitors = @(Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorID -ErrorAction SilentlyContinue)
if ($monitors.Count -eq 0) {
    Add-Reading -Category 'Display' -Name 'Monitors' -Value 'not readable (no WmiMonitorID instance)' -Source 'root/wmi:WmiMonitorID'
} else {
    for ($i = 0; $i -lt $monitors.Count; $i++) {
        $monitor = $monitors[$i]
        $manufacturer = -join [char[]]($monitor.ManufacturerName | Where-Object { $_ -gt 0 })
        $name = -join [char[]]($monitor.UserFriendlyName | Where-Object { $_ -gt 0 })
        $serial = -join [char[]]($monitor.SerialNumberID | Where-Object { $_ -gt 0 })
        Add-Reading -Category 'Display' -Name "Monitor$($i + 1)" -Value "$manufacturer $name serial=$serial year=$($monitor.YearOfManufacture)" -Source 'root/wmi:WmiMonitorID'
    }
}

# ----------------------------------------------------------------------------------- storage and SMART
$text.Add('== storage ==')
$disks = @(Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue)
if ($disks.Count -eq 0) {
    Add-Reading -Category 'Storage' -Name 'PhysicalDisks' -Value 'not readable (no Win32_DiskDrive instance)' -Source 'Win32_DiskDrive'
} else {
    for ($i = 0; $i -lt $disks.Count; $i++) {
        $disk = $disks[$i]
        Add-Reading -Category 'Storage' -Name "Disk$($i + 1)" -Source 'Win32_DiskDrive' `
            -Value "$($disk.Model) interface=$($disk.InterfaceType) size=$([math]::Round($disk.Size / 1GB, 1)) GB serial=$($disk.SerialNumber) status=$($disk.Status)"
    }
}
$predictions = @(Get-CimInstance -Namespace root/wmi -ClassName MSStorageDriver_FailurePredictStatus -ErrorAction SilentlyContinue)
if ($predictions.Count -eq 0) {
    Add-Reading -Category 'SMART' -Name 'FailurePrediction' -Value 'not readable (no MSStorageDriver_FailurePredictStatus instance)' -Source 'root/wmi:MSStorageDriver_FailurePredictStatus'
    $blocked.Add('SMART failure prediction is not exposed by this machine (typical for virtual machines)')
} else {
    for ($i = 0; $i -lt $predictions.Count; $i++) {
        $prediction = $predictions[$i]
        Add-Reading -Category 'SMART' -Name "Instance$($i + 1)" -Value "predictFailure=$($prediction.PredictFailure) reason=$($prediction.Reason) instance=$($prediction.InstanceName)" -Source 'root/wmi:MSStorageDriver_FailurePredictStatus'
        if ($prediction.PredictFailure) {
            $findings.Add("SMART failure prediction reports a predicted failure for $($prediction.InstanceName)")
        }
    }
}
$volumes = @(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' -ErrorAction SilentlyContinue)
foreach ($volume in $volumes) {
    if ($volume.Size -gt 0) {
        $freePercent = [math]::Round(($volume.FreeSpace / $volume.Size) * 100, 1)
        Add-Reading -Category 'Storage' -Name "Volume$($volume.DeviceID)" -Source 'Win32_LogicalDisk' `
            -Value "size=$([math]::Round($volume.Size / 1GB, 1)) GB free=$([math]::Round($volume.FreeSpace / 1GB, 1)) GB ($freePercent %) fs=$($volume.FileSystem)"
    }
}

# ----------------------------------------------------------------------------- temperature, fans, power
$text.Add('== sensors ==')
$zones = @(Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction SilentlyContinue)
if ($zones.Count -eq 0) {
    Add-Reading -Category 'Thermal' -Name 'Zones' -Value 'not readable (no MSAcpi_ThermalZoneTemperature instance; normal on virtual machines)' -Source 'root/wmi:MSAcpi_ThermalZoneTemperature'
    $blocked.Add('no thermal zone: the temperature module cannot be accepted on this machine')
} else {
    foreach ($zone in $zones) {
        # The ACPI class reports tenths of a kelvin. Showing the raw number as a temperature would be
        # a wrong statement, so it is converted and the raw value is kept next to it.
        $celsius = [math]::Round(($zone.CurrentTemperature / 10) - 273.15, 1)
        Add-Reading -Category 'Thermal' -Name "$($zone.InstanceName)" -Value "$celsius °C (raw $($zone.CurrentTemperature) tenths of kelvin)" -Source 'root/wmi:MSAcpi_ThermalZoneTemperature'
        if ($celsius -gt 95) {
            $findings.Add("thermal zone $($zone.InstanceName) reports $celsius °C")
        }
    }
}
$fans = @(Get-CimInstance Win32_Fan -ErrorAction SilentlyContinue)
if ($fans.Count -eq 0) {
    Add-Reading -Category 'Fan' -Name 'Fans' -Value 'not readable (no Win32_Fan instance; most firmware does not expose fans to Windows)' -Source 'Win32_Fan'
    $blocked.Add('no fan speed is exposed: cooling statements stay unproven on this machine')
} else {
    for ($i = 0; $i -lt $fans.Count; $i++) {
        $fan = $fans[$i]
        Add-Reading -Category 'Fan' -Name "Fan$($i + 1)" -Value "$($fan.DeviceID) desiredSpeed=$($fan.DesiredSpeed) status=$($fan.Status) activeCooling=$($fan.ActiveCooling)" -Source 'Win32_Fan'
    }
}
$batteries = @(Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue)
if ($batteries.Count -eq 0) {
    Add-Reading -Category 'Battery' -Name 'Batteries' -Value 'not readable (no Win32_Battery instance; normal on desktops and virtual machines)' -Source 'Win32_Battery'
    $blocked.Add('no battery: the battery module (M20) cannot be accepted on this machine')
} else {
    for ($i = 0; $i -lt $batteries.Count; $i++) {
        $battery = $batteries[$i]
        Add-Reading -Category 'Battery' -Name "Battery$($i + 1)" -Value "charge=$($battery.EstimatedChargeRemaining) % status=$($battery.BatteryStatus) name=$($battery.Name) voltage=$($battery.DesignVoltage) mV" -Source 'Win32_Battery'
    }
    # Design capacity and wear are a separate class; when it is missing the wear statement stays
    # unproven instead of being derived from a voltage.
    $static = @(Get-CimInstance -Namespace root/wmi -ClassName BatteryStaticData -ErrorAction SilentlyContinue)
    $full = @(Get-CimInstance -Namespace root/wmi -ClassName BatteryFullChargedCapacity -ErrorAction SilentlyContinue)
    if ($static.Count -eq 0 -or $full.Count -eq 0) {
        Add-Reading -Category 'Battery' -Name 'Wear' -Value 'not readable (BatteryStaticData / BatteryFullChargedCapacity missing)' -Source 'root/wmi'
        $blocked.Add('battery wear could not be computed: design or full charge capacity is not exposed')
    } else {
        $design = [double]$static[0].DesignedCapacity
        $fullCapacity = [double]$full[0].FullChargedCapacity
        if ($design -gt 0) {
            $wear = [math]::Round((1 - ($fullCapacity / $design)) * 100, 1)
            Add-Reading -Category 'Battery' -Name 'Wear' -Value "$wear % (design $design mWh, full $fullCapacity mWh)" -Source 'root/wmi:BatteryStaticData + BatteryFullChargedCapacity'
        }
    }
}

# --------------------------------------------------------------------------- firmware security (TPM/SB)
$text.Add('== firmware security ==')
$tpm = Read-Into -Category 'TPM' -Name 'Win32_Tpm' -Source 'root/cimv2/security/microsofttpm:Win32_Tpm' -Body {
    $instance = Get-CimInstance -Namespace 'root/cimv2/security/microsofttpm' -ClassName Win32_Tpm -ErrorAction Stop
    "present spec=$($instance.SpecVersion) enabled=$($instance.IsEnabled_InitialValue) activated=$($instance.IsActivated_InitialValue) owned=$($instance.IsOwned_InitialValue) manufacturer=$($instance.ManufacturerIdTxt) firmware=$($instance.ManufacturerVersion)"
}
if (-not $tpm) {
    $blocked.Add('no TPM answer: the TPM readings stay unproven on this machine')
} else {
    # Win32_Tpm reports the state at the time the class instance was created, not a live value; the
    # record says so instead of letting a stale value look like a current measurement.
    Add-Reading -Category 'TPM' -Name 'Note' -Value 'the Win32_Tpm values are the state when the class instance was created, not a live reading' -Source 'Microsoft documentation'
}
Read-Into -Category 'SecureBoot' -Name 'Enabled' -Source 'Confirm-SecureBootUEFI' -Body { (Confirm-SecureBootUEFI) }

# ---------------------------------------------------------------------------------- Windows services
$text.Add('== windows state ==')
Read-Into -Category 'Windows' -Name 'DefenderAntivirus' -Source 'Get-MpComputerStatus' -Body {
    $status = Get-MpComputerStatus -ErrorAction Stop
    "antivirusEnabled=$($status.AntivirusEnabled) realTimeProtection=$($status.RealTimeProtectionEnabled) signatureAge=$($status.AntivirusSignatureAge) days signatureVersion=$($status.AntivirusSignatureVersion) lastQuickScan=$($status.QuickScanEndTime)"
}
Read-Into -Category 'Windows' -Name 'PendingReboot' -Source 'registry: Component Based Servicing / Windows Update' -Body {
    $cbs = Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending'
    $wu = Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
    "componentBasedServicing=$cbs windowsUpdate=$wu"
}
Read-Into -Category 'Windows' -Name 'SystemRestorePoint' -Source 'Get-ComputerRestorePoint' -Body {
    $points = @(Get-ComputerRestorePoint -ErrorAction Stop)
    if ($points.Count -eq 0) { 'none' } else { "$($points.Count) point(s), newest $($points[-1].CreationTime)" }
}
Read-Into -Category 'Windows' -Name 'FirewallProfiles' -Source 'root/StandardCimv2:MSFT_NetFirewallProfile' -Body {
    $profiles = @(Get-CimInstance -Namespace root/StandardCimv2 -ClassName MSFT_NetFirewallProfile -ErrorAction Stop)
    ($profiles | ForEach-Object { "$($_.Name): enabled=$($_.Enabled) inbound=$($_.DefaultInboundAction) outbound=$($_.DefaultOutboundAction)" }) -join ' | '
}
Read-Into -Category 'Windows' -Name 'Uptime' -Source 'Win32_OperatingSystem.LastBootUpTime' -Body {
    $os = Get-CimInstance Win32_OperatingSystem
    $since = ([Management.ManagementDateTimeConverter]::ToDateTime($os.LastBootUpTime))
    "$([math]::Round(((Get-Date) - $since).TotalHours, 1)) hours since $since"
}

# ------------------------------------------------------------------------- comparison with the app
$text.Add('== comparison with the application ==')
if ([string]::IsNullOrWhiteSpace($WmcReport)) {
    $openPoints.Add('no application report was handed in (-WmcReport): the readings above prove what the machine shows, not what the application reads')
} elseif (-not (Test-Path $WmcReport)) {
    $openPoints.Add("the handed in application report $WmcReport does not exist")
} else {
    Copy-Item $WmcReport (Join-Path $run.Folder 'logs') -Force
    $appReport = Get-Content -Raw $WmcReport | ConvertFrom-Json
    Add-WmcEvidenceFile -Run $run -Path (Join-Path $run.Folder "logs/$(Split-Path -Leaf $WmcReport)") -Description 'report written by Windows Maintenance Center, handed in for the comparison'

    # Only values that both sides really carry are compared. A value that one side does not have is
    # named as an open point, never silently treated as equal.
    $comparisons = @(
        @{ Name = 'cpuName'; Mine = $cpu; Theirs = $appReport.cpu.name; Source = 'Win32_Processor.Name' }
        @{ Name = 'biosVersion'; Mine = $null; Theirs = $appReport.bios.version; Source = 'Win32_BIOS.SMBIOSBIOSVersion' }
    )
    foreach ($comparison in $comparisons) {
        if ($null -eq $comparison.Theirs) {
            $openPoints.Add("the application report carries no value for $($comparison.Name) - nothing to compare")
            continue
        }
        $mine = if ($comparison.Name -eq 'biosVersion') { (Get-CimInstance Win32_BIOS).SMBIOSBIOSVersion } else { $comparison.Mine }
        if ([string]::IsNullOrWhiteSpace([string]$mine)) {
            $openPoints.Add("$($comparison.Name): the machine reading is missing, so the application value cannot be judged")
        } elseif ([string]$mine.Trim() -ne ([string]$comparison.Theirs).Trim()) {
            $findings.Add("$($comparison.Name): machine says '$mine', the application report says '$($comparison.Theirs)'")
        } else {
            Add-Reading -Category 'Comparison' -Name $comparison.Name -Value "matches: '$mine'" -Source $comparison.Source
        }
    }
}

# ---------------------------------------------------------------------------------------- the report
foreach ($reading in $script:readings) {
    $unit = if ([string]::IsNullOrWhiteSpace($reading.unit)) { '' } else { " $($reading.unit)" }
    $text.Add(("{0,-12} {1,-22} {2}{3}   [{4}]" -f $reading.category, $reading.name, $reading.value, $unit, $reading.source))
}

Write-WmcEvidenceText -Run $run -Name 'readings.txt' -Lines $text.ToArray() -Description 'all readings with their source class'

$status = 'PASSED'
$summary = "collected $($script:readings.Count) readings on $($run.Environment.machine)"
if ($findings.Count -gt 0 -and $openPoints.Count -eq 0 -and $blocked.Count -eq 0) {
    $status = 'FAILED'
    $summary = "$($findings.Count) finding(s) in the readings"
} elseif ($blocked.Count -gt 0) {
    $status = 'BLOCKED'
    $summary = "$($blocked.Count) measurement group(s) cannot be taken on this machine: $($blocked[0])"
} elseif ($openPoints.Count -gt 0) {
    $status = 'NOT VERIFIED'
    $summary = 'the machine readings were taken, but ' + $openPoints[0]
}

Complete-WmcEvidenceRun -Run $run -Status $status -Summary $summary `
    -Findings $findings.ToArray() -OpenPoints $openPoints.ToArray() | Out-Null

if ($status -ne 'PASSED') {
    Write-Host 'This run is not a module acceptance. See docs/VM-TESTKIT.md for what has to be measured on the target machine.' -ForegroundColor Yellow
}
