using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>Geographic/system identity of the machine as reported by Windows.</summary>
public sealed record SystemIdentity
{
    public TextInfo ComputerModel { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo SystemType { get; init; }

    /// <summary>Only used locally. Masked in reports by default (see docs/PRIVACY.md).</summary>
    public TextInfo SerialNumber { get; init; }

    public TextInfo ChassisType { get; init; }

    public TextInfo SystemFamily { get; init; }

    public TextInfo OemString { get; init; }
}

/// <summary>Physical memory summary.</summary>
public sealed record MemoryInfo
{
    public IReadOnlyList<MemoryModuleInfo> Modules { get; init; } = Array.Empty<MemoryModuleInfo>();

    public Measured<ulong> TotalPhysicalBytes { get; init; } = Measured<ulong>.NotAvailable("Win32_ComputerSystem.TotalPhysicalMemory not read");

    public Measured<ulong> AvailablePhysicalBytes { get; init; } = Measured<ulong>.NotAvailable("Win32_OperatingSystem.FreePhysicalMemory not read");

    public Measured<uint> TotalSlots { get; init; } = Measured<uint>.NotAvailable("Win32_PhysicalMemoryArray not read");

    public Measured<uint> UsedSlots { get; init; } = Measured<uint>.NotAvailable("Win32_PhysicalMemoryArray not read");

    public Measured<uint> MemoryUsagePercent { get; init; } = Measured<uint>.NotAvailable("performance counter not read");
}

/// <summary>A single installed memory module.</summary>
public sealed record MemoryModuleInfo
{
    public TextInfo BankLabel { get; init; }

    public TextInfo DeviceLocator { get; init; }

    public Measured<ulong> CapacityBytes { get; init; } = Measured<ulong>.NotAvailable("capacity not reported");

    public Measured<uint> SpeedMhz { get; init; } = Measured<uint>.NotAvailable("configured speed not reported");

    public Measured<uint> ConfiguredClockMhz { get; init; } = Measured<uint>.NotAvailable("configured clock not reported");

    public Measured<ushort> DataWidthBits { get; init; } = Measured<ushort>.NotAvailable("data width not reported");

    public TextInfo Manufacturer { get; init; }

    public TextInfo PartNumber { get; init; }

    public TextInfo SerialNumber { get; init; }

    public TextInfo FormFactor { get; init; }

    public TextInfo MemoryType { get; init; }

    public bool? IsEcc { get; init; }
}

/// <summary>Processor information. Never contains a fabricated boost clock or temperature.</summary>
public sealed record ProcessorInfo
{
    public TextInfo Name { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo ProcessorId { get; init; }

    public TextInfo SocketDesignation { get; init; }

    public TextInfo Architecture { get; init; }

    public Measured<uint> Cores { get; init; } = Measured<uint>.NotAvailable("core count not reported");

    public Measured<uint> LogicalProcessors { get; init; } = Measured<uint>.NotAvailable("logical processor count not reported");

    public Measured<uint> BaseClockMhz { get; init; } = Measured<uint>.NotAvailable("base clock not reported");

    public Measured<uint> CurrentClockMhz { get; init; } = Measured<uint>.NotAvailable("current clock not reported");

    /// <summary>Only present when the platform exposes a reliable maximum. Otherwise unknown.</summary>
    public Measured<uint> MaxClockMhz { get; init; } = Measured<uint>.NotAvailable("maximum clock not reported");

    public Measured<double> LoadPercent { get; init; } = Measured<double>.NotAvailable("load counter not read");

    /// <summary>CPU voltage is usually not exposed by Windows; typically unknown.</summary>
    public Measured<double> VoltageVolts { get; init; } = Measured<double>.NotAvailable("no supported voltage source");

    /// <summary>Package power is not exposed by Windows; typically unknown.</summary>
    public Measured<double> PackagePowerWatts { get; init; } = Measured<double>.NotAvailable("no supported power source");

    public Measured<double> L2CacheKb { get; init; } = Measured<double>.NotAvailable("cache size not reported");

    public Measured<double> L3CacheKb { get; init; } = Measured<double>.NotAvailable("cache size not reported");

    public TextInfo VirtualizationFirmwareEnabled { get; init; }

    public Measured<uint> MaxClockSource { get; init; } = Measured<uint>.NotAvailable("not read");

    public TextInfo ThermalLimitNote { get; init; }
}

/// <summary>Mainboard identification, including the revision that gates BIOS automation.</summary>
public sealed record MotherboardInfo
{
    public TextInfo Manufacturer { get; init; }

    public TextInfo Product { get; init; }

    public TextInfo Version { get; init; }

    /// <summary>Raw SMBIOS revision string (e.g. "x.x"), reported separately from the marketing revision.</summary>
    public TextInfo SmbiosBoardRevision { get; init; }

    /// <summary>True only when the revision could be identified unambiguously from at least one source.</summary>
    public bool RevisionVerified { get; init; }

    /// <summary>Technical note describing which sources were compared to verify the revision.</summary>
    public string? RevisionVerificationDetail { get; init; }

    public TextInfo SerialNumber { get; init; }

    public TextInfo Chipset { get; init; }

    public TextInfo UefiMode { get; init; }

    public TextInfo SecureBootState { get; init; }

    public IReadOnlyList<TextInfo> BoardIdentifiers { get; init; } = Array.Empty<TextInfo>();
}

/// <summary>BIOS/UEFI identification as read from SMBIOS.</summary>
public sealed record BiosIdentification
{
    public TextInfo Manufacturer { get; init; }

    public TextInfo Version { get; init; }

    public TextInfo ReleaseDate { get; init; }

    public TextInfo SmbiosVersion { get; init; }

    public TextInfo SmbiosBiosRevision { get; init; }

    public TextInfo SystemBiosMajorRelease { get; init; }

    public TextInfo EmbeddedControllerVersion { get; init; }

    /// <summary>UEFI when the firmware interface type is reported as UEFI (Win32_Firmware or SetupAPI).</summary>
    public bool? IsUefi { get; init; }

    public TextInfo FirmwareType { get; init; }

    public bool? SecureBootEnabled { get; init; }

    public bool? IsLegacyBiosCaution { get; init; }
}

/// <summary>A graphics adapter, including driver and (if exposed) temperature.</summary>
public sealed record GraphicsAdapterInfo
{
    public TextInfo Name { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo PnpDeviceId { get; init; }

    public Measured<ulong> VideoMemoryBytes { get; init; } = Measured<ulong>.NotAvailable("video memory not reported");

    public Measured<uint> CurrentResolutionWidth { get; init; } = Measured<uint>.NotAvailable("resolution not reported");

    public Measured<uint> CurrentResolutionHeight { get; init; } = Measured<uint>.NotAvailable("resolution not reported");

    public Measured<uint> CurrentRefreshRate { get; init; } = Measured<uint>.NotAvailable("refresh rate not reported");

    public TextInfo DriverVersion { get; init; }

    public TextInfo DriverDate { get; init; }

    public Measured<double> TemperatureCelsius { get; init; } = Measured<double>.NotAvailable("no supported temperature source");

    public Measured<double> UtilizationPercent { get; init; } = Measured<double>.NotAvailable("no supported utilisation source");

    public TextInfo VendorSubsystemId { get; init; }

    public bool IsIntegratedGraphics { get; init; }
}

/// <summary>Storage device with SMART/NVMe health data as far as the platform exposes it.</summary>
public sealed record StorageDeviceInfo
{
    public TextInfo FriendlyName { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo Model { get; init; }

    public TextInfo SerialNumber { get; init; }

    public TextInfo FirmwareRevision { get; init; }

    public TextInfo BusType { get; init; }

    public TextInfo MediaType { get; init; }

    public TextInfo PartitionStyle { get; init; }

    public Measured<ulong> SizeBytes { get; init; } = Measured<ulong>.NotAvailable("size not reported");

    public Measured<ulong> FreeSpaceBytes { get; init; } = Measured<ulong>.NotAvailable("free space not reported");

    /// <summary>Raw health status token as reported by the storage stack (e.g. "Healthy").</summary>
    public TextInfo HealthStatus { get; init; }

    public Measured<byte> PercentageUsed { get; init; } = Measured<byte>.NotAvailable("wear indicator not available");

    public Measured<double> TemperatureCelsius { get; init; } = Measured<double>.NotAvailable("temperature not available");

    public Measured<ulong> PowerOnHours { get; init; } = Measured<ulong>.NotAvailable("power on hours not available");

    public Measured<ulong> ReadErrorsTotal { get; init; } = Measured<ulong>.NotAvailable("read errors not available");

    public Measured<ulong> WriteErrorsTotal { get; init; } = Measured<ulong>.NotAvailable("write errors not available");

    public Measured<ulong> MediaErrorsTotal { get; init; } = Measured<ulong>.NotAvailable("media errors not available");

    public Measured<ulong> BytesWrittenTbw { get; init; } = Measured<ulong>.NotAvailable("TBW not available");

    public Measured<uint> PowerCycleCount { get; init; } = Measured<uint>.NotAvailable("power cycle count not available");

    public bool? TrimEnabled { get; init; }

    public IReadOnlyList<VolumeInfo> Volumes { get; init; } = Array.Empty<VolumeInfo>();

    /// <summary>True when the device reports a critical health condition that must be highlighted immediately.</summary>
    public bool IsCritical { get; init; }

    public bool IsNvme { get; init; }

    public bool SmartAvailable { get; init; }
}

/// <summary>A mounted volume on a storage device.</summary>
public sealed record VolumeInfo
{
    public TextInfo DriveLetter { get; init; }

    public TextInfo Label { get; init; }

    public TextInfo FileSystem { get; init; }

    public Measured<ulong> SizeBytes { get; init; } = Measured<ulong>.NotAvailable("volume size not reported");

    public Measured<ulong> FreeBytes { get; init; } = Measured<ulong>.NotAvailable("free space not reported");

    public Measured<byte> FreePercent { get; init; } = Measured<byte>.NotAvailable("free space not reported");
}

/// <summary>Network adapter including connection state.</summary>
public sealed record NetworkAdapterInfo
{
    public TextInfo Name { get; init; }

    public TextInfo Description { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo MacAddress { get; init; }

    public TextInfo PnpDeviceId { get; init; }

    public TextInfo AdapterType { get; init; }

    public TextInfo ConnectionState { get; init; }

    public Measured<ulong> SpeedBitsPerSecond { get; init; } = Measured<ulong>.NotAvailable("link speed not reported");

    public bool IsWireless { get; init; }

    public bool IsBluetooth { get; init; }

    public bool IsVirtual { get; init; }

    public TextInfo DriverVersion { get; init; }

    public TextInfo IpAddress { get; init; }

    public TextInfo Gateway { get; init; }

    public TextInfo DnsServers { get; init; }
}

/// <summary>Audio endpoint/device.</summary>
public sealed record AudioDeviceInfo
{
    public TextInfo Name { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo PnpDeviceId { get; init; }

    public TextInfo DriverVersion { get; init; }

    public TextInfo Status { get; init; }

    public bool IsCapture { get; init; }
}

/// <summary>Connected display.</summary>
public sealed record MonitorInfo
{
    public TextInfo Name { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo ProductCode { get; init; }

    public TextInfo SerialNumber { get; init; }

    public TextInfo PnpDeviceId { get; init; }

    public Measured<uint> ManufactureYear { get; init; } = Measured<uint>.NotAvailable("EDID manufacture year not reported");

    public Measured<uint> HorizontalResolution { get; init; } = Measured<uint>.NotAvailable("resolution not reported");

    public Measured<uint> VerticalResolution { get; init; } = Measured<uint>.NotAvailable("resolution not reported");

    public Measured<uint> RefreshRate { get; init; } = Measured<uint>.NotAvailable("refresh rate not reported");

    public TextInfo ConnectionType { get; init; }
}

/// <summary>Printer as reported by the print subsystem.</summary>
public sealed record PrinterInfo
{
    public TextInfo Name { get; init; }

    public TextInfo DriverName { get; init; }

    public TextInfo PortName { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo Status { get; init; }

    public bool IsDefault { get; init; }

    public bool IsNetwork { get; init; }
}

/// <summary>Battery / power source information (notebooks and UPS).</summary>
public sealed record BatteryInfo
{
    public TextInfo Name { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo Chemistry { get; init; }

    public Measured<uint> DesignCapacityMwh { get; init; } = Measured<uint>.NotAvailable("design capacity not reported");

    public Measured<uint> FullChargeCapacityMwh { get; init; } = Measured<uint>.NotAvailable("full charge capacity not reported");

    public Measured<uint> CurrentCapacityMwh { get; init; } = Measured<uint>.NotAvailable("current capacity not reported");

    public Measured<byte> ChargePercent { get; init; } = Measured<byte>.NotAvailable("charge level not reported");

    public Measured<int> EstimatedRuntimeMinutes { get; init; } = Measured<int>.NotAvailable("runtime estimate not reported");

    public Measured<uint> CycleCount { get; init; } = Measured<uint>.NotAvailable("cycle count not reported");

    public int? HealthPercent { get; init; }
}

/// <summary>Plug and play device with problem code and driver binding.</summary>
public sealed record PnpDeviceInfo
{
    public TextInfo Name { get; init; }

    public TextInfo DeviceInstanceId { get; init; }

    public TextInfo Class { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo ServiceOrDriver { get; init; }

    public IReadOnlyList<string> HardwareIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CompatibleIds { get; init; } = Array.Empty<string>();

    public uint? ConfigManagerErrorCode { get; init; }

    public bool? IsPresent { get; init; }

    public bool? IsDisabled { get; init; }

    public TextInfo Status { get; init; }

    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    /// <summary>True when the entry is a leftover from hardware that is no longer installed.</summary>
    public bool IsPhantomDevice { get; init; }

    public Measured<uint> ProblemCode { get; init; } = Measured<uint>.NotAvailable("no problem code reported");
}

/// <summary>A driver binding as reported by the Windows driver store / PnP subsystem.</summary>
public sealed record DriverRecord
{
    public TextInfo DeviceName { get; init; }

    public TextInfo DeviceInstanceId { get; init; }

    public TextInfo DriverProvider { get; init; }

    public TextInfo DriverVersion { get; init; }

    public TextInfo DriverDate { get; init; }

    public TextInfo DriverFileName { get; init; }

    public TextInfo DeviceClass { get; init; }

    public TextInfo InfName { get; init; }

    public TextInfo HardwareId { get; init; }

    public bool? IsSigned { get; init; }

    public TextInfo Signer { get; init; }

    public TextInfo Status { get; init; }

    public Measured<uint> ProblemCode { get; init; } = Measured<uint>.NotAvailable("no problem code reported");

    public bool IsInboxDriver { get; init; }

    public bool IsGenericFallback { get; init; }

    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    public bool IsPhantomDevice { get; init; }
}

/// <summary>Windows edition, version and build information.</summary>
public sealed record WindowsIdentityInfo
{
    public TextInfo ProductName { get; init; }

    public TextInfo Edition { get; init; }

    public TextInfo DisplayVersion { get; init; }

    public TextInfo BuildNumber { get; init; }

    public TextInfo Revision { get; init; }

    public TextInfo Architecture { get; init; }

    public TextInfo InstallDate { get; init; }

    public TextInfo BootDevice { get; init; }

    public TextInfo SystemDrive { get; init; }

    public TextInfo RegisteredOwnerPresent { get; init; }

    public Measured<uint> UptimeHours { get; init; } = Measured<uint>.NotAvailable("uptime not read");

    public bool? IsWindows11 { get; init; }

    public TextInfo SecureBootState { get; init; }

    public TextInfo ActivationState { get; init; }
}

/// <summary>ACPI thermal zone reading. Always limited accuracy: it is a zone, not a core sensor.</summary>
public sealed record ThermalZoneReading
{
    public TextInfo InstanceName { get; init; }

    public Measured<double> TemperatureCelsius { get; init; } = Measured<double>.NotAvailable("thermal zone not readable");

    public Measured<double> CriticalTemperatureCelsius { get; init; } = Measured<double>.NotAvailable("critical temperature not reported");
}
