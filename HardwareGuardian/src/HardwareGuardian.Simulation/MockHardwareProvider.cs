using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Simulation;

/// <summary>
/// Fixture values for simulation mode and for the automated tests (spec section 44).
///
/// THIS IS NOT A MEASUREMENT. Every value produced here carries the origin
/// <see cref="DataSource.LocalData"/> with the detail "simulation fixture" and the provider reports
/// <see cref="IsSimulation"/> = true, so the UI can label the whole session as simulation and the
/// update engine refuses to act on it (BLOCKED / SIMULATION_MODE).
///
/// The fixture mirrors the documented reference machine so that layout, sorting and formatting can
/// be checked without hardware. Detection correctness can only be proven on the real machine.
/// </summary>
public sealed class MockHardwareProvider : IHardwareProvider
{
    private readonly IClock _clock;

    public MockHardwareProvider(IClock clock)
    {
        _clock = clock;
    }

    public string ProviderName => "MockHardwareProvider (simulation fixture)";

    public bool IsAvailable => true;

    public bool IsSimulation => true;

    private ValueOrigin Origin(string source) => ValueOrigin.Create(DataSource.LocalData, SensorQuality.High, _clock.Now, $"simulation fixture: {source} (not a measurement)");

    private TextInfo Text(string source, string? value, string? unknownReason = null)
    {
        var origin = Origin(source);
        return value is null ? TextInfo.Unknown(origin, unknownReason ?? "not part of this fixture") : TextInfo.Known(value, origin);
    }

    private Measured<T> MeasuredValue<T>(string source, T? value, string unknownReason) where T : struct =>
        value.HasValue ? Measured<T>.Known(value.Value, Origin(source)) : Measured<T>.NotAvailable(unknownReason, Origin(source));

    public Task<SystemIdentity> GetSystemIdentityAsync(CancellationToken cancellationToken) => Task.FromResult(new SystemIdentity
    {
        // The model is prefixed so that a screenshot of the simulation can never be mistaken for
        // the detection result of a real machine.
        ComputerModel = Text("Win32_ComputerSystem.Model", "SIMULATION — GIGABYTE B450M S2H"),
        Manufacturer = Text("Win32_ComputerSystem.Manufacturer", "SIMULATION — GIGABYTE"),
        SystemType = Text("Win32_ComputerSystem.SystemType", "x64-based PC"),
        SystemFamily = Text("Win32_ComputerSystem.SystemFamily", "B450"),
        OemString = Text("Win32_ComputerSystem.OEMStringArray", "SIMULATION — no measurement was taken on this machine"),
        SerialNumber = Text("Win32_ComputerSystemProduct.IdentifyingNumber", "SIMULATED-SERIAL-0000"),
        ChassisType = Text("Win32_SystemEnclosure.ChassisTypes", "Desktop"),
    });

    public Task<IReadOnlyList<ProcessorInfo>> GetProcessorsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProcessorInfo>>(new[]
    {
        new ProcessorInfo
        {
            Name = Text("Win32_Processor.Name", "SIMULATION — AMD Ryzen 5 5600G with Radeon Graphics"),
            Manufacturer = Text("Win32_Processor.Manufacturer", "Advanced Micro Devices, Inc."),
            ProcessorId = Text("Win32_Processor.ProcessorId", "SIMULATED-CPU-ID"),
            SocketDesignation = Text("Win32_Processor.SocketDesignation", "AM4"),
            Architecture = Text("Win32_Processor.Architecture", "x64"),
            Cores = MeasuredValue<uint>("Win32_Processor.NumberOfCores", 6, "core count not reported"),
            LogicalProcessors = MeasuredValue<uint>("Win32_Processor.NumberOfLogicalProcessors", 12, "logical processor count not reported"),
            BaseClockMhz = MeasuredValue<uint>("Win32_Processor.MaxClockSpeed", 3700, "base clock not reported"),
            CurrentClockMhz = MeasuredValue<uint>("Win32_Processor.CurrentClockSpeed", 3900, "current clock not reported"),
            MaxClockMhz = Measured<uint>.NotAvailable("Win32_Processor does not expose a reliable boost clock", Origin("Win32_Processor")),
            LoadPercent = MeasuredValue<double>("Win32_PerfFormattedData_PerfOS_Processor", 12d, "load counter not read"),
            VoltageVolts = Measured<double>.NotAvailable("no supported voltage source", Origin("Win32_Processor")),
            PackagePowerWatts = Measured<double>.NotAvailable("no supported power source", Origin("Win32_Processor")),
            L3CacheKb = MeasuredValue<double>("Win32_Processor.L3CacheSize", 16384d, "L3 cache size not reported"),
            VirtualizationFirmwareEnabled = Text("Win32_Processor.VirtualizationFirmwareEnabled", "TRUE"),
            ThermalLimitNote = TextInfo.Unknown(Origin("Win32_Processor"), "thermal limits are only reported by vendor tools"),
        },
    });

    public Task<MemoryInfo> GetMemoryAsync(CancellationToken cancellationToken)
    {
        var modules = new List<MemoryModuleInfo>();
        var capacity = new[] { 8192UL, 8192UL, 8192UL };
        for (var index = 0; index < capacity.Length; index++)
        {
            modules.Add(new MemoryModuleInfo
            {
                BankLabel = Text("Win32_PhysicalMemory.BankLabel", $"BANK {index}"),
                DeviceLocator = Text("Win32_PhysicalMemory.DeviceLocator", $"DIMM {index}"),
                CapacityBytes = MeasuredValue<ulong>("Win32_PhysicalMemory.Capacity", capacity[index], "capacity not reported"),
                SpeedMhz = MeasuredValue<uint>("Win32_PhysicalMemory.Speed", 2666u, "speed not reported"),
                ConfiguredClockMhz = MeasuredValue<uint>("Win32_PhysicalMemory.ConfiguredClockSpeed", 2666u, "configured clock not reported"),
                DataWidthBits = MeasuredValue<ushort>("Win32_PhysicalMemory.DataWidth", (ushort)64, "data width not reported"),
                Manufacturer = Text("Win32_PhysicalMemory.Manufacturer", "SIMULATION — DIMM vendor"),
                PartNumber = Text("Win32_PhysicalMemory.PartNumber", $"SIMULATED-PART-{index}"),
                SerialNumber = Text("Win32_PhysicalMemory.SerialNumber", $"SIMULATED-DIMM-SERIAL-{index}"),
                FormFactor = Text("Win32_PhysicalMemory.FormFactor", "DIMM"),
                MemoryType = Text("Win32_PhysicalMemory.SMBIOSMemoryType", "DDR4"),
                IsEcc = false,
            });
        }

        return Task.FromResult(new MemoryInfo
        {
            Modules = modules,
            TotalPhysicalBytes = MeasuredValue<ulong>("Win32_ComputerSystem.TotalPhysicalMemory", 24UL * 1024 * 1024 * 1024, "total memory not reported"),
            AvailablePhysicalBytes = MeasuredValue<ulong>("Win32_OperatingSystem.FreePhysicalMemory", 14UL * 1024 * 1024 * 1024, "available memory not reported"),
            TotalSlots = MeasuredValue<uint>("Win32_PhysicalMemoryArray.MemoryDevices", 4u, "slot count not reported"),
            UsedSlots = MeasuredValue<uint>("Win32_PhysicalMemoryArray", 3u, "used slot count not reported"),
            MemoryUsagePercent = MeasuredValue<uint>("performance counter", 42u, "memory usage not derivable"),
        });
    }

    public Task<MotherboardInfo> GetMotherboardAsync(CancellationToken cancellationToken) => Task.FromResult(new MotherboardInfo
    {
        Manufacturer = Text("Win32_BaseBoard.Manufacturer", "GIGABYTE"),
        Product = Text("Win32_BaseBoard.Product", "B450M S2H"),
        Version = Text("Win32_BaseBoard.Version", "x.x"),
        SmbiosBoardRevision = Text("Win32_BaseBoard.Version", "x.x"),
        // In the fixture the SMBIOS revision carries no information, exactly as on many real boards.
        // Therefore the revision stays unverified and every firmware action remains BLOCKED.
        RevisionVerified = false,
        RevisionVerificationDetail = "simulation fixture: SMBIOS board version 'x.x' carries no revision information",
        SerialNumber = Text("Win32_BaseBoard.SerialNumber", "SIMULATED-BOARD-SERIAL"),
        Chipset = TextInfo.Unknown(Origin("Win32_BaseBoard"), "chipset is not exposed through WMI"),
        UefiMode = Text("Win32_ComputerSystem.BootupState", "Normal boot"),
        SecureBootState = Text("firmware API", "Enabled"),
        BoardIdentifiers = new[] { Text("Win32_BaseBoard.Tag", "Base Board") },
    });

    public Task<BiosIdentification> GetBiosAsync(CancellationToken cancellationToken) => Task.FromResult(new BiosIdentification
    {
        Manufacturer = Text("Win32_BIOS.Manufacturer", "American Megatrends International, LLC."),
        Version = Text("Win32_BIOS.SMBIOSBIOSVersion", "F67"),
        ReleaseDate = Text("Win32_BIOS.ReleaseDate", "2023-09-20"),
        SmbiosVersion = Text("Win32_BIOS.SMBIOSMajorVersion", "3.5"),
        SmbiosBiosRevision = Text("Win32_BIOS.Version", "ALASKA - 1072009"),
        SystemBiosMajorRelease = Text("Win32_BIOS.SystemBiosMajorVersion", "5"),
        EmbeddedControllerVersion = Text("Win32_BIOS.EmbeddedControllerMajorVersion", "255"),
        IsUefi = true,
        FirmwareType = Text("Win32_Firmware.SystemFirmwareType", "UEFI"),
        SecureBootEnabled = true,
        IsLegacyBiosCaution = false,
    });

    public Task<IReadOnlyList<GraphicsAdapterInfo>> GetGraphicsAdaptersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<GraphicsAdapterInfo>>(new[]
    {
        new GraphicsAdapterInfo
        {
            Name = Text("Win32_VideoController.Name", "SIMULATION — NVIDIA GeForce RTX 3060"),
            Manufacturer = Text("Win32_VideoController.AdapterCompatibility", "NVIDIA"),
            PnpDeviceId = Text("Win32_VideoController.PNPDeviceID", @"PCI\VEN_10DE&DEV_2504&SUBSYS_00000000"),
            VideoMemoryBytes = MeasuredValue<ulong>("Win32_VideoController.AdapterRAM", 12UL * 1024 * 1024 * 1024, "video memory not reported"),
            CurrentResolutionWidth = MeasuredValue<uint>("Win32_VideoController", 2560u, "resolution not reported"),
            CurrentResolutionHeight = MeasuredValue<uint>("Win32_VideoController", 1440u, "resolution not reported"),
            CurrentRefreshRate = MeasuredValue<uint>("Win32_VideoController", 144u, "refresh rate not reported"),
            DriverVersion = Text("Win32_VideoController.DriverVersion", "31.0.15.5161"),
            DriverDate = Text("Win32_VideoController.DriverDate", "2024-04-11"),
            TemperatureCelsius = Measured<double>.NotAvailable("no supported temperature source", Origin("nvidia-smi")),
            UtilizationPercent = Measured<double>.NotAvailable("no supported utilisation source", Origin("nvidia-smi")),
            VendorSubsystemId = Text("Win32_VideoController.PNPDeviceID", "10DE"),
            IsIntegratedGraphics = false,
        },
        new GraphicsAdapterInfo
        {
            Name = Text("Win32_VideoController.Name", "SIMULATION — AMD Radeon Graphics (integrated)"),
            Manufacturer = Text("Win32_VideoController.AdapterCompatibility", "Advanced Micro Devices, Inc."),
            PnpDeviceId = Text("Win32_VideoController.PNPDeviceID", @"PCI\VEN_1002&DEV_1638&SUBSYS_00000000"),
            VideoMemoryBytes = Measured<ulong>.NotAvailable("shared memory is not reported as dedicated video memory", Origin("Win32_VideoController")),
            DriverVersion = Text("Win32_VideoController.DriverVersion", "31.0.21910.5001"),
            DriverDate = Text("Win32_VideoController.DriverDate", "2024-03-14"),
            TemperatureCelsius = Measured<double>.NotAvailable("no supported temperature source", Origin("Win32_VideoController")),
            UtilizationPercent = Measured<double>.NotAvailable("no supported utilisation source", Origin("Win32_VideoController")),
            IsIntegratedGraphics = true,
        },
    });

    public Task<IReadOnlyList<StorageDeviceInfo>> GetStorageDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StorageDeviceInfo>>(new[]
    {
        new StorageDeviceInfo
        {
            FriendlyName = Text("MSFT_PhysicalDisk.FriendlyName", "SIMULATION — NVMe SSD 1000 GB"),
            Manufacturer = TextInfo.Unknown(Origin("MSFT_PhysicalDisk"), "MSFT_PhysicalDisk does not report the manufacturer"),
            Model = Text("MSFT_PhysicalDisk.FriendlyName", "SIMULATION — NVMe SSD 1000 GB"),
            SerialNumber = Text("MSFT_PhysicalDisk.SerialNumber", "SIMULATED-SSD-SERIAL"),
            FirmwareRevision = Text("MSFT_PhysicalDisk.FirmwareVersion", "SIM-FW-1.0"),
            BusType = Text("MSFT_PhysicalDisk.BusType", "NVMe"),
            MediaType = Text("MSFT_PhysicalDisk.MediaType", "SSD"),
            PartitionStyle = Text("Win32_DiskPartition", "GPT"),
            SizeBytes = MeasuredValue<ulong>("MSFT_PhysicalDisk.Size", 1000204886016UL, "size not reported"),
            FreeSpaceBytes = MeasuredValue<ulong>("Win32_LogicalDisk.FreeSpace", 412_000_000_000UL, "free space not reported"),
            HealthStatus = Text("MSFT_PhysicalDisk.HealthStatus", "Healthy"),
            PercentageUsed = MeasuredValue<byte>("MSFT_StorageReliabilityCounter.Wear", (byte)7, "wear indicator not available"),
            TemperatureCelsius = MeasuredValue<double>("MSFT_StorageReliabilityCounter.Temperature", 38d, "temperature not available"),
            PowerOnHours = MeasuredValue<ulong>("MSFT_StorageReliabilityCounter.PowerOnHours", 9120UL, "power on hours not available"),
            MediaErrorsTotal = MeasuredValue<ulong>("MSFT_StorageReliabilityCounter", 0UL, "media errors not available"),
            PowerCycleCount = MeasuredValue<uint>("MSFT_StorageReliabilityCounter.StartStopCycleCount", 640u, "power cycle count not available"),
            TrimEnabled = true,
            Volumes = new[]
            {
                new VolumeInfo
                {
                    DriveLetter = Text("Win32_LogicalDisk.DeviceID", "C:"),
                    Label = Text("Win32_LogicalDisk.VolumeName", "System"),
                    FileSystem = Text("Win32_LogicalDisk.FileSystem", "NTFS"),
                    SizeBytes = MeasuredValue<ulong>("Win32_LogicalDisk.Size", 1000204886016UL, "volume size not reported"),
                    FreeBytes = MeasuredValue<ulong>("Win32_LogicalDisk.FreeSpace", 412_000_000_000UL, "free space not reported"),
                    FreePercent = MeasuredValue<byte>("Win32_LogicalDisk", (byte)41, "free space percentage not derivable"),
                },
            },
            IsCritical = false,
            IsNvme = true,
            SmartAvailable = true,
        },
    });

    public Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<NetworkAdapterInfo>>(new[]
    {
        new NetworkAdapterInfo
        {
            Name = Text("Win32_NetworkAdapter.Name", "SIMULATION — Realtek Gaming 2.5GbE Family Controller"),
            Description = Text("Win32_NetworkAdapter.Description", "Realtek Gaming 2.5GbE Family Controller"),
            Manufacturer = Text("Win32_NetworkAdapter.Manufacturer", "Realtek"),
            MacAddress = Text("Win32_NetworkAdapter.MACAddress", "SIMULATED-MAC"),
            PnpDeviceId = Text("Win32_NetworkAdapter.PNPDeviceID", @"PCI\VEN_10EC&DEV_8125"),
            ConnectionState = Text("Win32_NetworkAdapter.NetConnectionStatus", "Connected"),
            SpeedBitsPerSecond = MeasuredValue<ulong>("Win32_NetworkAdapter.Speed", 1_000_000_000UL, "link speed not reported"),
            IsWireless = false,
            IsVirtual = false,
        },
        new NetworkAdapterInfo
        {
            Name = Text("Win32_NetworkAdapter.Name", "SIMULATION — Intel Wi-Fi 6 AX200"),
            Description = Text("Win32_NetworkAdapter.Description", "Intel(R) Wi-Fi 6 AX200 160MHz"),
            Manufacturer = Text("Win32_NetworkAdapter.Manufacturer", "Intel Corporation"),
            MacAddress = Text("Win32_NetworkAdapter.MACAddress", "SIMULATED-MAC-WLAN"),
            ConnectionState = Text("Win32_NetworkAdapter.NetConnectionStatus", "MediaDisconnected"),
            SpeedBitsPerSecond = Measured<ulong>.NotAvailable("link speed is only reported while connected", Origin("Win32_NetworkAdapter")),
            IsWireless = true,
            IsVirtual = false,
        },
    });

    public Task<IReadOnlyList<AudioDeviceInfo>> GetAudioDevicesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(new[]
    {
        new AudioDeviceInfo
        {
            Name = Text("Win32_SoundDevice.Name", "SIMULATION — Realtek High Definition Audio"),
            Manufacturer = Text("Win32_SoundDevice.Manufacturer", "Realtek"),
            PnpDeviceId = Text("Win32_SoundDevice.PNPDeviceID", @"HDAUDIO\FUNC_01"),
            Status = Text("Win32_SoundDevice.Status", "OK"),
        },
        new AudioDeviceInfo
        {
            Name = Text("Win32_SoundDevice.Name", "SIMULATION — NVIDIA High Definition Audio"),
            Manufacturer = Text("Win32_SoundDevice.Manufacturer", "NVIDIA"),
            Status = Text("Win32_SoundDevice.Status", "OK"),
        },
    });

    public Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MonitorInfo>>(new[]
    {
        new MonitorInfo
        {
            Name = Text("WmiMonitorID.UserFriendlyName", "SIMULATION — DELL U2720Q"),
            Manufacturer = Text("WmiMonitorID.ManufacturerName", "DEL"),
            ProductCode = Text("WmiMonitorID.ProductCodeID", "U2720Q"),
            SerialNumber = Text("WmiMonitorID.SerialNumberID", "SIMULATED-MONITOR-SERIAL"),
            ManufactureYear = MeasuredValue<uint>("WmiMonitorID.YearOfManufacture", 2021u, "manufacture year not reported"),
            HorizontalResolution = MeasuredValue<uint>("Win32_VideoController", 3840u, "resolution not reported"),
            VerticalResolution = MeasuredValue<uint>("Win32_VideoController", 2160u, "resolution not reported"),
            RefreshRate = MeasuredValue<uint>("Win32_VideoController", 60u, "refresh rate not reported"),
            ConnectionType = TextInfo.Unknown(Origin("WmiMonitorID"), "connection type is not exposed through WMI"),
        },
    });

    public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PrinterInfo>>(new[]
    {
        new PrinterInfo
        {
            Name = Text("Win32_Printer.Name", "SIMULATION — Microsoft Print to PDF"),
            DriverName = Text("Win32_Printer.DriverName", "Microsoft Print To PDF"),
            PortName = Text("Win32_Printer.PortName", "PORTPROMPT:"),
            Status = Text("Win32_Printer.Status", "Idle"),
            IsDefault = false,
            IsNetwork = false,
        },
    });

    /// <summary>The fixture describes a desktop system: no battery is reported, which is a valid result.</summary>
    public Task<BatteryInfo?> GetBatteryAsync(CancellationToken cancellationToken) => Task.FromResult<BatteryInfo?>(null);

    public Task<IReadOnlyList<PnpDeviceInfo>> GetPnpDevicesAsync(CancellationToken cancellationToken)
    {
        var origin = Origin("Win32_PnPEntity");
        var devices = new List<PnpDeviceInfo>
        {
            Device("PCI\\VEN_10DE&DEV_2504", "SIMULATION — NVIDIA GeForce RTX 3060", "Display", ComponentCategory.Graphics, problemCode: null),
            Device("PCI\\VEN_1002&DEV_1638", "SIMULATION — AMD Radeon Graphics", "Display", ComponentCategory.Graphics, problemCode: null),
            Device("PCI\\VEN_10EC&DEV_8125", "SIMULATION — Realtek Gaming 2.5GbE Family Controller", "Net", ComponentCategory.Network, problemCode: null),
            Device("PCI\\VEN_8086&DEV_2723", "SIMULATION — Intel Wi-Fi 6 AX200", "Net", ComponentCategory.Network, problemCode: 22),
            Device("USB\\VID_046D&PID_C52B", "SIMULATION — USB Input Device", "HIDClass", ComponentCategory.Usb, problemCode: null),
            Device("ROOT\\PRINTQUEUE\\0000", "SIMULATION — Microsoft Print to PDF", "PrintQueue", ComponentCategory.Printer, problemCode: null),
            Device("PCI\\VEN_1022&DEV_1483", "SIMULATION — AMD PSP 11.0 Device", "SecurityDevices", ComponentCategory.Chipset, problemCode: null),
        };

        return Task.FromResult<IReadOnlyList<PnpDeviceInfo>>(devices);

        PnpDeviceInfo Device(string instanceId, string name, string deviceClass, ComponentCategory category, uint? problemCode) => new()
        {
            Name = Text("Win32_PnPEntity.Name", name),
            DeviceInstanceId = Text("Win32_PnPEntity.PNPDeviceID", instanceId),
            Class = Text("Win32_PnPEntity.PNPClass", deviceClass),
            Manufacturer = Text("Win32_PnPEntity.Manufacturer", "SIMULATION"),
            HardwareIds = new[] { instanceId },
            ConfigManagerErrorCode = problemCode,
            IsPresent = true,
            IsDisabled = problemCode == 22,
            Status = Text("Win32_PnPEntity.Status", problemCode == 22 ? "Error" : "OK"),
            Category = category,
            IsPhantomDevice = false,
            ProblemCode = problemCode.HasValue
                ? Measured<uint>.Known(problemCode.Value, origin)
                : Measured<uint>.NotAvailable("no problem code reported", origin),
        };
    }

    public Task<IReadOnlyList<DriverRecord>> GetDriversAsync(CancellationToken cancellationToken)
    {
        var origin = Origin("Win32_PnPSignedDriver");
        var drivers = new List<DriverRecord>
        {
            Driver("SIMULATION — NVIDIA GeForce RTX 3060", @"PCI\VEN_10DE&DEV_2504", "NVIDIA", "31.0.15.5161", "nv_dispi.inf", "Display", ComponentCategory.Graphics, signed: true),
            Driver("SIMULATION — AMD Radeon Graphics", @"PCI\VEN_1002&DEV_1638", "Advanced Micro Devices, Inc.", "31.0.21910.5001", "u0380900.inf", "Display", ComponentCategory.Graphics, signed: true),
            Driver("SIMULATION — Realtek Gaming 2.5GbE Family Controller", @"PCI\VEN_10EC&DEV_8125", "Realtek", "10.68.423.2023", "oem12.inf", "Net", ComponentCategory.Network, signed: true),
            Driver("SIMULATION — Intel Wi-Fi 6 AX200", @"PCI\VEN_8086&DEV_2723", "Intel Corporation", "22.250.1.2", "oem31.inf", "Net", ComponentCategory.Network, signed: true),
            Driver("SIMULATION — Standard SATA AHCI Controller", @"PCI\VEN_1022&DEV_43EB", "Microsoft", "10.0.22621.1", "storahci.inf", "SCSIAdapter", ComponentCategory.Storage, signed: true),
            Driver("SIMULATION — AMD PSP 11.0 Device", @"PCI\VEN_1022&DEV_1483", "Advanced Micro Devices, Inc.", "5.24.0.0", "amdsps.inf", "System", ComponentCategory.System, signed: true),
            Driver("SIMULATION — Generic USB Hub", @"USB\VID_8087&PID_0029", "SIMULATED VENDOR", "1.0.0.0", "usbhub3.inf", "USB", ComponentCategory.Usb, signed: null),
        };

        return Task.FromResult<IReadOnlyList<DriverRecord>>(drivers);

        DriverRecord Driver(string name, string instanceId, string provider, string version, string inf, string deviceClass, ComponentCategory category, bool? signed) => new()
        {
            DeviceName = Text("Win32_PnPSignedDriver.DeviceName", name),
            DeviceInstanceId = Text("Win32_PnPSignedDriver.DeviceID", instanceId),
            DriverProvider = Text("Win32_PnPSignedDriver.DriverProviderName", provider),
            DriverVersion = Text("Win32_PnPSignedDriver.DriverVersion", version),
            DriverDate = Text("Win32_PnPSignedDriver.DriverDate", "2024-04-11"),
            DriverFileName = Text("Win32_PnPSignedDriver.InfName", inf),
            DeviceClass = Text("Win32_PnPSignedDriver.DeviceClass", deviceClass),
            InfName = Text("Win32_PnPSignedDriver.InfName", inf),
            HardwareId = Text("Win32_PnPSignedDriver.HardwareID", instanceId),
            IsSigned = signed,
            Signer = signed == true ? Text("Win32_PnPSignedDriver.Signer", "Microsoft Windows Hardware Compatibility Publisher") : TextInfo.Unknown(origin, "signature state is not reported for this device"),
            Status = Text("Win32_PnPSignedDriver.Status", "OK"),
            ProblemCode = Measured<uint>.NotAvailable("no problem code reported", origin),
            Category = category,
        };
    }

    public Task<IReadOnlyList<ThermalZoneReading>> GetThermalZonesAsync(CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.Create(DataSource.Wmi, SensorQuality.Limited, _clock.Now, "simulation fixture: MSAcpi_ThermalZoneTemperature (ACPI zone, not a core sensor)");

        return Task.FromResult<IReadOnlyList<ThermalZoneReading>>(new[]
        {
            new ThermalZoneReading
            {
                InstanceName = Text("MSAcpi_ThermalZoneTemperature.InstanceName", @"ACPI\ThermalZone\TZ01_0"),
                TemperatureCelsius = Measured<double>.Known(48.5, origin),
                CriticalTemperatureCelsius = Measured<double>.Known(95.0, origin),
            },
        });
    }

    public Task<WindowsIdentityInfo> GetWindowsIdentityAsync(CancellationToken cancellationToken) => Task.FromResult(new WindowsIdentityInfo
    {
        ProductName = Text("Win32_OperatingSystem.Caption", "Microsoft Windows 11 Pro (SIMULATION)"),
        Edition = Text("Win32_OperatingSystem.OperatingSystemSKU", "Professional"),
        DisplayVersion = Text("registry DisplayVersion", "24H2"),
        BuildNumber = Text("Win32_OperatingSystem.BuildNumber", "26100"),
        Revision = Text("Win32_OperatingSystem.Version", "10.0.26100"),
        Architecture = Text("Win32_OperatingSystem.OSArchitecture", "64-bit"),
        InstallDate = Text("Win32_OperatingSystem.InstallDate", "2023-11-02"),
        BootDevice = Text("Win32_OperatingSystem.BootDevice", @"\Device\HarddiskVolume1"),
        SystemDrive = Text("Win32_OperatingSystem.SystemDrive", "C:"),
        RegisteredOwnerPresent = Text("Win32_OperatingSystem.RegisteredUser", "reported (masked)"),
        UptimeHours = MeasuredValue<uint>("Win32_OperatingSystem.LastBootUpTime", 26u, "uptime not read"),
        IsWindows11 = true,
        SecureBootState = Text("firmware API", "Enabled"),
        ActivationState = TextInfo.Unknown(Origin("licence check"), "activation state requires an online licence check and is not reported"),
    });

    public Task<IReadOnlyList<StorageReliabilityCounter>> GetStorageReliabilityAsync(CancellationToken cancellationToken)
    {
        var origin = Origin("MSFT_StorageReliabilityCounter");

        return Task.FromResult<IReadOnlyList<StorageReliabilityCounter>>(new[]
        {
            new StorageReliabilityCounter
            {
                DeviceId = "SIMULATED-SSD-SERIAL",
                TemperatureCelsius = Measured<double>.Known(38d, origin),
                PercentageUsed = Measured<byte>.Known(7, origin),
                PowerOnHours = Measured<ulong>.Known(9120UL, origin),
                ReadErrorsTotal = Measured<ulong>.Known(0UL, origin),
                WriteErrorsTotal = Measured<ulong>.Known(0UL, origin),
                MediaErrorsTotal = Measured<ulong>.Known(0UL, origin),
                PowerCycleCount = Measured<uint>.Known(640u, origin),
                NvmeBytesWritten = Measured<ulong>.NotAvailable("bytes written are not exposed through MSFT_StorageReliabilityCounter", origin),
                NvmeBytesRead = Measured<ulong>.NotAvailable("bytes read are not exposed through MSFT_StorageReliabilityCounter", origin),
                WearLevelPercent = Measured<byte>.Known(93, origin),
            },
        });
    }
}
