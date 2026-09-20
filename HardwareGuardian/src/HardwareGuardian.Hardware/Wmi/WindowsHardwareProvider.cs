using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Hardware.Wmi;

/// <summary>
/// Production hardware provider (spec section 44). Reads through CIM/WMI and the storage stack.
/// Rules: every value carries its origin, everything unreadable stays UNKNOWN with a reason,
/// and no default value is ever substituted for a missing measurement.
/// </summary>
public sealed class WindowsHardwareProvider : IHardwareProvider
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";
    private readonly WmiReader _wmi;
    private readonly IClock _clock;

    public WindowsHardwareProvider(WmiReader wmi, IClock clock)
    {
        _wmi = wmi;
        _clock = clock;
    }

    public string ProviderName => "WindowsWmiProvider";

    public bool IsAvailable => OperatingSystem.IsWindows();

    public bool IsSimulation => false;

    public async Task<SystemIdentity> GetSystemIdentityAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var system = await _wmi.QueryFirstAsync("Win32_ComputerSystem", cancellationToken: cancellationToken).ConfigureAwait(false);
        var product = await _wmi.QueryFirstAsync("Win32_ComputerSystemProduct", cancellationToken: cancellationToken).ConfigureAwait(false);
        var enclosure = await _wmi.QueryFirstAsync("Win32_SystemEnclosure", cancellationToken: cancellationToken).ConfigureAwait(false);

        // ChassisTypes is an array of SMBIOS codes (uint16[]). Reading it as text produced the
        // string "System.UInt16[]" as a *known* value - a fabricated reading. The codes are decoded
        // with the documented table and the code stays part of the text.
        var chassisTypes = enclosure?.GetUIntArray("ChassisTypes") ?? Array.Empty<uint>();
        var chassisText = chassisTypes.Count == 0
            ? TextInfo.Unknown(origin, "Win32_SystemEnclosure.ChassisTypes was not reported")
            : TextInfo.Known(
                string.Join(", ", chassisTypes.Select(code => SmbiosCodes.Describe(SmbiosCodes.ChassisType(code), code))),
                origin);

        return new SystemIdentity
        {
            Manufacturer = Text(system, "Manufacturer", origin),
            ComputerModel = Text(system, "Model", origin),
            SystemType = Text(system, "SystemType", origin),
            SystemFamily = Text(system, "SystemFamily", origin),
            OemString = Text(system, "OEMStringArray", origin),
            SerialNumber = Text(product, "IdentifyingNumber", origin, "Win32_ComputerSystemProduct.IdentifyingNumber was not reported"),
            ChassisType = chassisText,
        };
    }

    public async Task<IReadOnlyList<ProcessorInfo>> GetProcessorsAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var objects = await _wmi.QueryAsync("Win32_Processor", cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<ProcessorInfo>();

        foreach (var item in objects)
        {
            list.Add(new ProcessorInfo
            {
                Name = Text(item, "Name", origin),
                Manufacturer = Text(item, "Manufacturer", origin),
                ProcessorId = Text(item, "ProcessorId", origin),
                SocketDesignation = Text(item, "SocketDesignation", origin),
                Architecture = Text(item, "Architecture", origin),
                Cores = UInt(item, "NumberOfCores", origin),
                LogicalProcessors = UInt(item, "NumberOfLogicalProcessors", origin),
                BaseClockMhz = UInt(item, "MaxClockSpeed", origin),
                CurrentClockMhz = UInt(item, "CurrentClockSpeed", origin),
                MaxClockMhz = Measured<uint>.Missing("Win32_Processor does not expose a reliable boost clock"),
                LoadPercent = item is not null && item.TryGetUInt("LoadPercentage", out var load)
                    ? Measured<double>.Known(load, origin)
                    : Measured<double>.Missing("Win32_Processor.LoadPercentage was not reported"),
                VoltageVolts = item is not null && item.TryGetUInt("CurrentVoltage", out var voltage) && voltage > 0
                    ? Measured<double>.Known(voltage / 10d, origin)
                    : Measured<double>.Missing("Win32_Processor.CurrentVoltage was not reported (often absent on modern platforms)"),
                PackagePowerWatts = Measured<double>.Missing("Windows does not expose CPU package power without a vendor sensor driver"),
                L2CacheKb = UInt(item, "L2CacheSize", origin) is { HasValue: true } l2 ? Measured<double>.Known(l2.Value!.Value, origin) : Measured<double>.Missing("L2 cache size not reported"),
                L3CacheKb = UInt(item, "L3CacheSize", origin) is { HasValue: true } l3 ? Measured<double>.Known(l3.Value!.Value, origin) : Measured<double>.Missing("L3 cache size not reported"),
                VirtualizationFirmwareEnabled = Text(item, "VirtualizationFirmwareEnabled", origin),
                ThermalLimitNote = TextInfo.Unknown(origin, "thermal limits are only reported by vendor tools"),
            });
        }

        return list;
    }

    public async Task<MemoryInfo> GetMemoryAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var moduleObjects = await _wmi.QueryAsync("Win32_PhysicalMemory", cancellationToken: cancellationToken).ConfigureAwait(false);
        var arrayObject = await _wmi.QueryFirstAsync("Win32_PhysicalMemoryArray", cancellationToken: cancellationToken).ConfigureAwait(false);
        var systemObject = await _wmi.QueryFirstAsync("Win32_ComputerSystem", cancellationToken: cancellationToken).ConfigureAwait(false);
        var osObject = await _wmi.QueryFirstAsync("Win32_OperatingSystem", cancellationToken: cancellationToken).ConfigureAwait(false);

        var modules = moduleObjects.Select(item => new MemoryModuleInfo
        {
            BankLabel = Text(item, "BankLabel", origin),
            DeviceLocator = Text(item, "DeviceLocator", origin),
            CapacityBytes = ULong(item, "Capacity", origin),
            SpeedMhz = UInt(item, "Speed", origin),
            ConfiguredClockMhz = UInt(item, "ConfiguredClockSpeed", origin),
            DataWidthBits = item is not null && item.TryGetUInt("DataWidth", out var width) ? Measured<ushort>.Known((ushort)width, origin) : Measured<ushort>.Missing("memory data width not reported"),
            Manufacturer = Text(item, "Manufacturer", origin),
            PartNumber = Text(item, "PartNumber", origin),
            SerialNumber = Text(item, "SerialNumber", origin),
            FormFactor = Code(item, "FormFactor", SmbiosCodes.MemoryFormFactor, origin),
            MemoryType = Code(item, "SMBIOSMemoryType", SmbiosCodes.MemoryType, origin),
            IsEcc = TryEcc(item),
        }).ToList();

        var totalMemory = ULong(systemObject, "TotalPhysicalMemory", origin);
        Measured<ulong> freeMemory = Measured<ulong>.Missing("Win32_OperatingSystem.FreePhysicalMemory not reported");
        if (osObject is not null && osObject.TryGetULong("FreePhysicalMemory", out var freeKb))
        {
            freeMemory = Measured<ulong>.Known(freeKb * 1024UL, origin);
        }

        Measured<uint> usage = Measured<uint>.Missing("memory usage could not be derived");
        if (totalMemory.HasValue && freeMemory.HasValue && totalMemory.Value > 0)
        {
            var used = totalMemory.Value - freeMemory.Value;
            usage = Measured<uint>.Known((uint)Math.Clamp(used * 100 / totalMemory.Value, 0, 100), origin);
        }

        return new MemoryInfo
        {
            Modules = modules,
            TotalPhysicalBytes = totalMemory,
            AvailablePhysicalBytes = freeMemory,
            TotalSlots = arrayObject is not null && arrayObject.TryGetUInt("MemoryDevices", out var devices) ? Measured<uint>.Known(devices, origin) : Measured<uint>.Missing("Win32_PhysicalMemoryArray.MemoryDevices not reported"),
            UsedSlots = Measured<uint>.Known((uint)modules.Count, origin),
            MemoryUsagePercent = usage,
        };
    }

    public async Task<MotherboardInfo> GetMotherboardAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var board = await _wmi.QueryFirstAsync("Win32_BaseBoard", cancellationToken: cancellationToken).ConfigureAwait(false);
        var firmware = await _wmi.QueryFirstAsync("Win32_ComputerSystem", cancellationToken: cancellationToken).ConfigureAwait(false);

        var revision = Text(board, "Version", origin);
        var product = Text(board, "Product", origin);
        var manufacturer = Text(board, "Manufacturer", origin);

        // The SMBIOS board version is often the only revision information available. It is only
        // treated as verified when the board identity itself is unambiguous.
        var revisionVerified = revision.IsKnown
            && !revision.Value!.Equals("x.x", StringComparison.OrdinalIgnoreCase)
            && !revision.Value!.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)
            && manufacturer.IsKnown
            && product.IsKnown;

        var detail = revisionVerified
            ? $"revision from Win32_BaseBoard.Version; board={product.Value}"
            : $"revision not unambiguous (Win32_BaseBoard.Version='{revision.Value ?? "empty"}')";

        var secureBootReading = SecureBootReader.Read();
        var secureBoot = secureBootReading.Value is { } secureBootValue
            ? Measured<bool>.Known(secureBootValue, ValueOrigin.WindowsApi(_clock.Now, "GetFirmwareEnvironmentVariable(UEFI)"))
            : Measured<bool>.Missing(secureBootReading.Detail);

        return new MotherboardInfo
        {
            Manufacturer = manufacturer,
            Product = product,
            Version = revision,
            SmbiosBoardRevision = revision,
            RevisionVerified = revisionVerified,
            RevisionVerificationDetail = detail,
            SerialNumber = Text(board, "SerialNumber", origin),
            Chipset = TextInfo.Unknown(origin, "chipset is not exposed through Win32_BaseBoard"),
            UefiMode = Text(firmware, "BootupState", origin),
            SecureBootState = secureBoot.HasValue
                ? TextInfo.Known(secureBoot.Value.Value ? "Enabled" : "Disabled", secureBoot.Origin)
                : TextInfo.Unknown(secureBoot.Origin, secureBoot.UnknownReason),
            BoardIdentifiers = new[]
            {
                Text(board, "Tag", origin),
                Text(board, "Product", origin),
            },
        };
    }

    public async Task<BiosIdentification> GetBiosAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var bios = await _wmi.QueryFirstAsync("Win32_BIOS", cancellationToken: cancellationToken).ConfigureAwait(false);
        var firmware = await _wmi.QueryFirstAsync("Win32_Firmware", cancellationToken: cancellationToken).ConfigureAwait(false);

        bool? uefi = null;
        var firmwareType = TextInfo.Unknown(origin, "Win32_Firmware did not report a firmware type");
        if (firmware is not null && firmware.TryGetUInt("SystemFirmwareType", out var type))
        {
            // 1 = BIOS, 2 = UEFI (Win32_Firmware.SystemFirmwareType)
            uefi = type == 2;
            firmwareType = TextInfo.Known(type == 2 ? "UEFI" : type == 1 ? "BIOS" : $"Unknown({type})", origin);
        }
        else if (Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Boot\EFI")))
        {
            // Presence of the EFI boot directory is a documented indicator, not a measurement of
            // the firmware type, therefore it is marked as limited quality.
            uefi = null;
            firmwareType = TextInfo.Known("UEFI (inferred from \\Windows\\Boot\\EFI)", origin with { Quality = SensorQuality.Limited });
        }

        return new BiosIdentification
        {
            Manufacturer = Text(bios, "Manufacturer", origin),
            Version = Text(bios, "SMBIOSBIOSVersion", origin),
            ReleaseDate = bios is not null && bios.TryGetDateTime("ReleaseDate", out var released)
                ? TextInfo.Known(released.ToString("yyyy-MM-dd"), origin)
                : TextInfo.Unknown(origin, "ReleaseDate not reported"),
            SmbiosVersion = bios is not null && bios.TryGetUInt("SMBIOSMajorVersion", out var major) && bios.TryGetUInt("SMBIOSMinorVersion", out var minor)
                ? TextInfo.Known($"{major}.{minor}", origin)
                : TextInfo.Unknown(origin, "SMBIOS version not reported"),
            SmbiosBiosRevision = Text(bios, "Version", origin),
            SystemBiosMajorRelease = bios is not null && bios.TryGetUInt("SystemBiosMajorVersion", out var biosMajor)
                ? TextInfo.Known(biosMajor.ToString(System.Globalization.CultureInfo.InvariantCulture), origin)
                : TextInfo.Unknown(origin, "SystemBiosMajorVersion not reported"),
            EmbeddedControllerVersion = bios is not null && bios.TryGetUInt("EmbeddedControllerMajorVersion", out var ecMajor)
                ? TextInfo.Known(ecMajor.ToString(System.Globalization.CultureInfo.InvariantCulture), origin)
                : TextInfo.Unknown(origin, "EmbeddedControllerMajorVersion not reported"),
            IsUefi = uefi,
            FirmwareType = firmwareType,
            SecureBootEnabled = SecureBootReader.Read().Value,
        };
    }

    public async Task<IReadOnlyList<GraphicsAdapterInfo>> GetGraphicsAdaptersAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var objects = await _wmi.QueryAsync("Win32_VideoController", cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<GraphicsAdapterInfo>();

        foreach (var item in objects)
        {
            var name = Text(item, "Name", origin);
            var memory = UInt(item, "AdapterRAM", origin);
            var adapterName = name.Value ?? string.Empty;
            var isIntegrated = name.IsKnown && (adapterName.Contains("UHD", StringComparison.OrdinalIgnoreCase)
                || adapterName.Contains("Radeon Vega", StringComparison.OrdinalIgnoreCase)
                || adapterName.Contains("Graphics", StringComparison.OrdinalIgnoreCase) && adapterName.Contains("Intel", StringComparison.OrdinalIgnoreCase));

            list.Add(new GraphicsAdapterInfo
            {
                Name = name,
                Manufacturer = Text(item, "AdapterCompatibility", origin),
                PnpDeviceId = Text(item, "PNPDeviceID", origin),
                VideoMemoryBytes = memory.HasValue ? Measured<ulong>.Known(memory.Value!.Value, origin) : Measured<ulong>.Missing("Win32_VideoController.AdapterRAM not reported"),
                CurrentResolutionWidth = UInt(item, "CurrentHorizontalResolution", origin),
                CurrentResolutionHeight = UInt(item, "CurrentVerticalResolution", origin),
                CurrentRefreshRate = UInt(item, "CurrentRefreshRate", origin),
                DriverVersion = Text(item, "DriverVersion", origin),
                DriverDate = item is not null && item.TryGetDateTime("DriverDate", out var driverDate) ? TextInfo.Known(driverDate.ToString("yyyy-MM-dd"), origin) : TextInfo.Unknown(origin, "driver date not reported"),
                TemperatureCelsius = Measured<double>.Missing("no supported GPU temperature source (vendor tool required)"),
                UtilizationPercent = Measured<double>.Missing("no supported GPU utilisation source (vendor tool required)"),
                VendorSubsystemId = Text(item, "PNPDeviceID", origin),
                IsIntegratedGraphics = isIntegrated,
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<StorageDeviceInfo>> GetStorageDevicesAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var physicalDisks = await _wmi.QueryAsync("MSFT_PhysicalDisk", scope: StorageScope, cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<StorageDeviceInfo>();

        if (physicalDisks.Count > 0)
        {
            var logicalDisks = await _wmi.QueryAsync("Win32_LogicalDisk", "DriveType=3", cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var disk in physicalDisks)
            {
                var friendlyName = Text(disk, "FriendlyName", origin);
                var mediaType = disk.TryGetUInt("MediaType", out var media)
                    ? TextInfo.Known(media switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => $"Unknown({media})" }, origin)
                    : TextInfo.Unknown(origin, "MSFT_PhysicalDisk.MediaType not reported");
                var busType = disk.TryGetUInt("BusType", out var bus)
                    ? TextInfo.Known(bus switch { 7 => "USB", 8 => "RAID", 11 => "SATA", 17 => "NVMe", 10 => "SAS", 3 => "ATA", _ => $"Bus({bus})" }, origin)
                    : TextInfo.Unknown(origin, "MSFT_PhysicalDisk.BusType not reported");

                var size = ULong(disk, "Size", origin);
                var health = disk.TryGetUInt("HealthStatus", out var healthValue)
                    ? TextInfo.Known(healthValue switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => $"Unknown({healthValue})" }, origin)
                    : TextInfo.Unknown(origin, "MSFT_PhysicalDisk.HealthStatus not reported");

                var volumes = new List<VolumeInfo>();
                foreach (var logical in logicalDisks)
                {
                    var freeSpace = ULong(logical, "FreeSpace", origin);
                    var volumeSize = ULong(logical, "Size", origin);
                    volumes.Add(new VolumeInfo
                    {
                        DriveLetter = Text(logical, "DeviceID", origin),
                        Label = Text(logical, "VolumeName", origin),
                        FileSystem = Text(logical, "FileSystem", origin),
                        SizeBytes = volumeSize,
                        FreeBytes = freeSpace,
                        FreePercent = volumeSize.HasValue && freeSpace.HasValue && volumeSize.Value > 0
                            ? Measured<byte>.Known((byte)Math.Clamp(freeSpace.Value * 100 / volumeSize.Value, 0, 100), origin)
                            : Measured<byte>.Missing("free space percentage could not be derived"),
                    });
                }

                var serial = Text(disk, "SerialNumber", origin);
                list.Add(new StorageDeviceInfo
                {
                    FriendlyName = friendlyName,
                    Manufacturer = TextInfo.Unknown(origin, "MSFT_PhysicalDisk does not report the manufacturer"),
                    Model = friendlyName,
                    SerialNumber = serial,
                    FirmwareRevision = Text(disk, "FirmwareVersion", origin),
                    BusType = busType,
                    MediaType = mediaType,
                    PartitionStyle = TextInfo.Unknown(origin, "partition style is reported per disk through Win32_DiskPartition"),
                    SizeBytes = size,
                    FreeSpaceBytes = volumes.Count > 0 && volumes.All(v => v.FreeBytes.HasValue)
                        ? Measured<ulong>.Known(volumes.Sum(v => v.FreeBytes.Value!.Value), origin)
                        : Measured<ulong>.Missing("free space could not be summed up for this device"),
                    HealthStatus = health,
                    PercentageUsed = Measured<byte>.Missing("SMART wear indicator needs MSFT_StorageReliabilityCounter"),
                    TemperatureCelsius = Measured<double>.Missing("SMART temperature needs MSFT_StorageReliabilityCounter"),
                    PowerOnHours = Measured<ulong>.Missing("SMART power on hours need MSFT_StorageReliabilityCounter"),
                    ReadErrorsTotal = Measured<ulong>.Missing("SMART counters need MSFT_StorageReliabilityCounter"),
                    WriteErrorsTotal = Measured<ulong>.Missing("SMART counters need MSFT_StorageReliabilityCounter"),
                    MediaErrorsTotal = Measured<ulong>.Missing("SMART counters need MSFT_StorageReliabilityCounter"),
                    BytesWrittenTbw = Measured<ulong>.Missing("SMART counters need MSFT_StorageReliabilityCounter"),
                    PowerCycleCount = Measured<uint>.Missing("SMART counters need MSFT_StorageReliabilityCounter"),
                    Volumes = volumes,
                    IsCritical = disk.TryGetUInt("HealthStatus", out var hs) && hs == 2,
                    IsNvme = busType.IsKnown && busType.Value!.Contains("NVMe", StringComparison.OrdinalIgnoreCase),
                    SmartAvailable = false,
                });
            }

            return list;
        }

        // Fallback for systems where the storage namespace is unavailable (for example on older
        // Windows builds or without the StorCIM provider): report what Win32_DiskDrive can tell.
        var diskDrives = await _wmi.QueryAsync("Win32_DiskDrive", cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var drive in diskDrives)
        {
            var interfaceType = Text(drive, "InterfaceType", origin);
            list.Add(new StorageDeviceInfo
            {
                FriendlyName = Text(drive, "Caption", origin),
                Manufacturer = Text(drive, "Manufacturer", origin),
                Model = Text(drive, "Model", origin),
                SerialNumber = Text(drive, "SerialNumber", origin),
                FirmwareRevision = Text(drive, "FirmwareRevision", origin),
                BusType = interfaceType,
                MediaType = Text(drive, "MediaType", origin),
                PartitionStyle = TextInfo.Unknown(origin, "partition style not reported by Win32_DiskDrive"),
                SizeBytes = ULong(drive, "Size", origin),
                FreeSpaceBytes = Measured<ulong>.Missing("free space is only available per volume"),
                HealthStatus = Text(drive, "Status", origin),
                IsNvme = interfaceType.IsKnown && interfaceType.Value!.Contains("NVMe", StringComparison.OrdinalIgnoreCase),
                SmartAvailable = false,
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<StorageReliabilityCounter>> GetStorageReliabilityAsync(CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.Wmi(_clock.Now, "MSFT_StorageReliabilityCounter", SensorQuality.High);
        var counters = await _wmi.QueryAsync("MSFT_StorageReliabilityCounter", scope: StorageScope, cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<StorageReliabilityCounter>();

        foreach (var counter in counters)
        {
            var deviceId = counter.GetString("DeviceId") ?? counter.GetString("ObjectId") ?? string.Empty;

            list.Add(new StorageReliabilityCounter
            {
                DeviceId = deviceId,
                TemperatureCelsius = counter.TryGetUInt("Temperature", out var temperature) && temperature > 0
                    ? Measured<double>.Known(temperature, origin)
                    : Measured<double>.Missing("temperature not reported by the device"),
                PercentageUsed = counter.TryGetUInt("Wear", out var wear)
                    ? Measured<byte>.Known((byte)Math.Clamp(wear, 0, 100), origin)
                    : Measured<byte>.Missing("wear indicator not reported by the device"),
                PowerOnHours = counter.TryGetULong("PowerOnHours", out var hours)
                    ? Measured<ulong>.Known(hours, origin)
                    : Measured<ulong>.Missing("power on hours not reported by the device"),
                ReadErrorsTotal = counter.TryGetULong("ReadErrorsTotal", out var readErrors)
                    ? Measured<ulong>.Known(readErrors, origin)
                    : Measured<ulong>.Missing("read errors not reported by the device"),
                WriteErrorsTotal = counter.TryGetULong("WriteErrorsTotal", out var writeErrors)
                    ? Measured<ulong>.Known(writeErrors, origin)
                    : Measured<ulong>.Missing("write errors not reported by the device"),
                MediaErrorsTotal = counter.TryGetULong("ReadErrorsUncorrected", out var uncorrectedRead) && counter.TryGetULong("WriteErrorsUncorrected", out var uncorrectedWrite)
                    ? Measured<ulong>.Known(uncorrectedRead + uncorrectedWrite, origin)
                    : Measured<ulong>.Missing("uncorrected media errors not reported by the device"),
                PowerCycleCount = counter.TryGetUInt("StartStopCycleCount", out var cycles)
                    ? Measured<uint>.Known(cycles, origin)
                    : Measured<uint>.Missing("power cycle count not reported by the device"),
                NvmeBytesWritten = Measured<ulong>.Missing("bytes written are not exposed through MSFT_StorageReliabilityCounter"),
                NvmeBytesRead = Measured<ulong>.Missing("bytes read are not exposed through MSFT_StorageReliabilityCounter"),
                WearLevelPercent = Measured<byte>.Missing("wear level is not exposed separately on all devices"),
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var adapters = await _wmi.QueryAsync("Win32_NetworkAdapter", "PhysicalAdapter=TRUE", cancellationToken: cancellationToken).ConfigureAwait(false);
        var configurations = await _wmi.QueryAsync("Win32_NetworkAdapterConfiguration", cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<NetworkAdapterInfo>();

        foreach (var adapter in adapters)
        {
            var name = Text(adapter, "Name", origin);
            var description = Text(adapter, "Description", origin);
            var adapterName = name.Value ?? string.Empty;
            var adapterDescription = description.Value ?? string.Empty;
            var isWireless = (name.IsKnown && adapterName.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase))
                || (description.IsKnown && (adapterDescription.Contains("Wireless", StringComparison.OrdinalIgnoreCase) || adapterDescription.Contains("WiFi", StringComparison.OrdinalIgnoreCase)));
            var isBluetooth = description.IsKnown && adapterDescription.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

            var configuration = configurations.FirstOrDefault(c =>
                c.GetString("Index") is { } index &&
                adapter.TryGetUInt("Index", out var adapterIndex) &&
                index == adapterIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));

            list.Add(new NetworkAdapterInfo
            {
                Name = name,
                Description = description,
                Manufacturer = Text(adapter, "Manufacturer", origin),
                MacAddress = Text(adapter, "MACAddress", origin),
                PnpDeviceId = Text(adapter, "PNPDeviceID", origin),
                AdapterType = Text(adapter, "AdapterType", origin),
                ConnectionState = adapter.TryGetUInt("NetConnectionStatus", out var status)
                    ? TextInfo.Known(status switch { 0 => "Disconnected", 2 => "Connected", 7 => "MediaDisconnected", _ => $"Status({status})" }, origin)
                    : TextInfo.Unknown(origin, "NetConnectionStatus not reported"),
                SpeedBitsPerSecond = ULong(adapter, "Speed", origin),
                IsWireless = isWireless,
                IsBluetooth = isBluetooth,
                IsVirtual = description.IsKnown && (adapterDescription.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || adapterDescription.Contains("VPN", StringComparison.OrdinalIgnoreCase)),
                DriverVersion = TextInfo.Unknown(origin, "driver version is read from the driver inventory"),
                IpAddress = configuration is not null ? Text(configuration, "IPAddress", origin) : TextInfo.Unknown(origin, "adapter configuration not found"),
                Gateway = configuration is not null ? Text(configuration, "DefaultIPGateway", origin) : TextInfo.Unknown(origin, "adapter configuration not found"),
                DnsServers = configuration is not null ? Text(configuration, "DNSServerSearchOrder", origin) : TextInfo.Unknown(origin, "adapter configuration not found"),
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<AudioDeviceInfo>> GetAudioDevicesAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var objects = await _wmi.QueryAsync("Win32_SoundDevice", cancellationToken: cancellationToken).ConfigureAwait(false);
        return objects.Select(item => new AudioDeviceInfo
        {
            Name = Text(item, "Name", origin),
            Manufacturer = Text(item, "Manufacturer", origin),
            PnpDeviceId = Text(item, "PNPDeviceID", origin),
            DriverVersion = TextInfo.Unknown(origin, "driver version is read from the driver inventory"),
            Status = Text(item, "Status", origin),
            IsCapture = false,
        }).ToList();
    }

    public async Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.Wmi(_clock.Now, @"root\wmi:WmiMonitorID", SensorQuality.High);
        var monitors = await _wmi.QueryAsync("WmiMonitorID", scope: @"root\wmi", cancellationToken: cancellationToken).ConfigureAwait(false);
        var videoControllers = await _wmi.QueryAsync("Win32_VideoController", cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<MonitorInfo>();

        foreach (var monitor in monitors)
        {
            var name = Decode(monitor.GetStringArray("UserFriendlyName"));
            var controller = videoControllers.FirstOrDefault();

            list.Add(new MonitorInfo
            {
                Name = string.IsNullOrWhiteSpace(name) ? TextInfo.Unknown(origin, "monitor name not reported") : TextInfo.Known(name, origin),
                Manufacturer = DecodeArray(monitor, "ManufacturerName", origin),
                ProductCode = DecodeArray(monitor, "ProductCodeID", origin),
                SerialNumber = DecodeArray(monitor, "SerialNumberID", origin),
                PnpDeviceId = TextInfo.Unknown(origin, "monitor device instance id is not exposed by WmiMonitorID"),
                ManufactureYear = monitor.TryGetUInt("YearOfManufacture", out var year) ? Measured<uint>.Known(year, origin) : Measured<uint>.Missing("YearOfManufacture not reported"),
                HorizontalResolution = controller is not null ? UInt(controller, "CurrentHorizontalResolution", origin) : Measured<uint>.Missing("no video controller reported"),
                VerticalResolution = controller is not null ? UInt(controller, "CurrentVerticalResolution", origin) : Measured<uint>.Missing("no video controller reported"),
                RefreshRate = controller is not null ? UInt(controller, "CurrentRefreshRate", origin) : Measured<uint>.Missing("no video controller reported"),
                ConnectionType = TextInfo.Unknown(origin, "connection type is not exposed through WMI"),
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var objects = await _wmi.QueryAsync("Win32_Printer", cancellationToken: cancellationToken).ConfigureAwait(false);
        return objects.Select(item => new PrinterInfo
        {
            Name = Text(item, "Name", origin),
            DriverName = Text(item, "DriverName", origin),
            PortName = Text(item, "PortName", origin),
            Manufacturer = Text(item, "Local", origin),
            Status = Text(item, "Status", origin),
            IsDefault = item.TryGetBool("Default", out var isDefault) && isDefault,
            IsNetwork = item.TryGetBool("Network", out var isNetwork) && isNetwork,
        }).ToList();
    }

    public async Task<BatteryInfo?> GetBatteryAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var battery = await _wmi.QueryFirstAsync("Win32_Battery", cancellationToken: cancellationToken).ConfigureAwait(false);
        if (battery is null)
        {
            return null;
        }

        uint? design = battery.TryGetUInt("DesignCapacity", out var designCapacity) ? designCapacity : null;
        uint? full = battery.TryGetUInt("FullChargeCapacity", out var fullCapacity) ? fullCapacity : null;

        return new BatteryInfo
        {
            Name = Text(battery, "Name", origin),
            Manufacturer = TextInfo.Unknown(origin, "Win32_Battery does not report the manufacturer"),
            Chemistry = Text(battery, "Chemistry", origin),
            DesignCapacityMwh = design.HasValue ? Measured<uint>.Known(design.Value, origin) : Measured<uint>.Missing("design capacity not reported"),
            FullChargeCapacityMwh = full.HasValue ? Measured<uint>.Known(full.Value, origin) : Measured<uint>.Missing("full charge capacity not reported"),
            CurrentCapacityMwh = Measured<uint>.Missing("Win32_Battery does not report the current capacity in mWh"),
            ChargePercent = battery.TryGetUInt("EstimatedChargeRemaining", out var charge) ? Measured<byte>.Known((byte)Math.Clamp(charge, 0, 100), origin) : Measured<byte>.Missing("charge level not reported"),
            EstimatedRuntimeMinutes = battery.TryGetUInt("EstimatedRunTime", out var runtime) && runtime > 0 ? Measured<int>.Known((int)runtime, origin) : Measured<int>.Missing("runtime estimate not reported"),
            CycleCount = battery.TryGetUInt("CycleCount", out var cycles) ? Measured<uint>.Known(cycles, origin) : Measured<uint>.Missing("cycle count not reported"),
            HealthPercent = design.HasValue && full.HasValue && design.Value > 0 ? (int)(full.Value * 100 / design.Value) : null,
        };
    }

    public async Task<IReadOnlyList<PnpDeviceInfo>> GetPnpDevicesAsync(CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.Wmi(_clock.Now, "Win32_PnPEntity", SensorQuality.High);
        var devices = await _wmi.QueryAsync("Win32_PnPEntity", cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<PnpDeviceInfo>();

        foreach (var device in devices)
        {
            var instanceId = device.GetString("PNPDeviceID") ?? device.GetString("DeviceID") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                continue;
            }

            uint? errorCode = device.TryGetUInt("ConfigManagerErrorCode", out var code) ? code : null;
            var present = device.TryGetBool("Present", out var isPresent) ? isPresent : (bool?)null;

            list.Add(new PnpDeviceInfo
            {
                Name = Text(device, "Name", origin),
                DeviceInstanceId = TextInfo.Known(instanceId, origin),
                Class = Text(device, "PNPClass", origin),
                Manufacturer = Text(device, "Manufacturer", origin),
                ServiceOrDriver = Text(device, "Service", origin),
                HardwareIds = device.GetStringArray("HardwareID"),
                CompatibleIds = device.GetStringArray("CompatibleID"),
                ConfigManagerErrorCode = errorCode,
                IsPresent = present,
                IsDisabled = errorCode == 22,
                Status = Text(device, "Status", origin),
                Category = MapPnpCategory(device.GetString("PNPClass")),
                IsPhantomDevice = present == false,
                ProblemCode = errorCode.HasValue ? Measured<uint>.Known(errorCode.Value, origin) : Measured<uint>.Missing("no problem code reported"),
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<DriverRecord>> GetDriversAsync(CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.Wmi(_clock.Now, "Win32_PnPSignedDriver", SensorQuality.High);
        var drivers = await _wmi.QueryAsync("Win32_PnPSignedDriver", cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<DriverRecord>();

        foreach (var driver in drivers)
        {
            var deviceId = driver.GetString("DeviceID") ?? string.Empty;
            bool? signed = driver.TryGetBool("IsSigned", out var isSigned) ? isSigned : null;
            uint? problem = driver.TryGetUInt("ConfigManagerErrorCode", out var code) ? code : null;

            list.Add(new DriverRecord
            {
                DeviceName = Text(driver, "DeviceName", origin),
                DeviceInstanceId = TextInfo.From(deviceId, origin, "DeviceID not reported"),
                DriverProvider = Text(driver, "DriverProviderName", origin),
                DriverVersion = Text(driver, "DriverVersion", origin),
                DriverDate = driver.TryGetDateTime("DriverDate", out var driverDate) ? TextInfo.Known(driverDate.ToString("yyyy-MM-dd"), origin) : TextInfo.Unknown(origin, "DriverDate not reported"),
                DriverFileName = Text(driver, "InfName", origin),
                DeviceClass = Text(driver, "DeviceClass", origin),
                InfName = Text(driver, "InfName", origin),
                HardwareId = Text(driver, "HardwareID", origin),
                IsSigned = signed,
                Signer = Text(driver, "Signer", origin),
                Status = TextInfo.Unknown(origin, "device status is reported by Win32_PnPEntity"),
                ProblemCode = problem.HasValue ? Measured<uint>.Known(problem.Value, origin) : Measured<uint>.Missing("no problem code reported"),
                IsInboxDriver = false,
                IsGenericFallback = false,
                Category = MapCategoryFromClass(driver.GetString("DeviceClass")),
                IsPhantomDevice = false,
            });
        }

        return list;
    }

    public async Task<IReadOnlyList<ThermalZoneReading>> GetThermalZonesAsync(CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.Wmi(_clock.Now, @"root\wmi:MSAcpi_ThermalZoneTemperature", SensorQuality.Limited);
        var zones = await _wmi.QueryAsync("MSAcpi_ThermalZoneTemperature", scope: @"root\wmi", cancellationToken: cancellationToken).ConfigureAwait(false);
        var list = new List<ThermalZoneReading>();

        foreach (var zone in zones)
        {
            Measured<double> temperature = Measured<double>.Missing("thermal zone value not readable");
            if (zone.TryGetUInt("CurrentTemperature", out var raw) && raw > 0)
            {
                // ACPI reports tenths of Kelvin. This is a zone, not a core sensor: LIMITED quality.
                temperature = Measured<double>.Known(Math.Round(raw / 10d - 273.15d, 1), origin);
            }

            Measured<double> critical = Measured<double>.Missing("critical trip point not reported");
            if (zone.TryGetUInt("CriticalTripPoint", out var rawCritical) && rawCritical > 0)
            {
                critical = Measured<double>.Known(Math.Round(rawCritical / 10d - 273.15d, 1), origin);
            }

            list.Add(new ThermalZoneReading
            {
                InstanceName = Text(zone, "InstanceName", origin),
                TemperatureCelsius = temperature,
                CriticalTemperatureCelsius = critical,
            });
        }

        return list;
    }

    public async Task<WindowsIdentityInfo> GetWindowsIdentityAsync(CancellationToken cancellationToken)
    {
        var origin = Pc();
        var os = await _wmi.QueryFirstAsync("Win32_OperatingSystem", cancellationToken: cancellationToken).ConfigureAwait(false);

        Measured<uint> uptime = Measured<uint>.Missing("LastBootUpTime not reported");
        if (os is not null && os.TryGetDateTime("LastBootUpTime", out var lastBoot))
        {
            var elapsed = DateTime.UtcNow - lastBoot.ToUniversalTime();
            if (elapsed.TotalHours >= 0)
            {
                uptime = Measured<uint>.Known((uint)Math.Min(elapsed.TotalHours, uint.MaxValue), origin);
            }
        }

        string? build = os?.GetString("BuildNumber");
        var isWindows11 = build is null ? (bool?)null : int.TryParse(build, out var buildNumber) && buildNumber >= 22000;

        return new WindowsIdentityInfo
        {
            ProductName = Text(os, "Caption", origin),
            Edition = Text(os, "OperatingSystemSKU", origin),
            DisplayVersion = await ReadDisplayVersionAsync(cancellationToken).ConfigureAwait(false),
            BuildNumber = Text(os, "BuildNumber", origin),
            Revision = Text(os, "Version", origin),
            Architecture = Text(os, "OSArchitecture", origin),
            InstallDate = os is not null && os.TryGetDateTime("InstallDate", out var installed) ? TextInfo.Known(installed.ToString("yyyy-MM-dd"), origin) : TextInfo.Unknown(origin, "InstallDate not reported"),
            BootDevice = Text(os, "BootDevice", origin),
            SystemDrive = Text(os, "SystemDrive", origin),
            RegisteredOwnerPresent = TextInfo.Known(
                string.IsNullOrWhiteSpace(os?.GetString("RegisteredUser")) ? "not reported" : "reported (masked)",
                origin with { Quality = SensorQuality.Limited }),
            UptimeHours = uptime,
            IsWindows11 = isWindows11,
            SecureBootState = ReadSecureBootText(origin),
            ActivationState = TextInfo.Unknown(origin, "activation state requires an online licence check and is not reported"),
        };
    }

    private TextInfo Pc() => ValueOrigin.Wmi(_clock.Now, "root\\cimv2", SensorQuality.High);

    private static TextInfo Text(WmiObject? source, string property, ValueOrigin origin, string? unknownReason = null)
    {
        if (source is null)
        {
            return TextInfo.Unknown(origin, unknownReason ?? "WMI class not available on this system");
        }

        return source.TryGetString(property, out var value)
            ? TextInfo.Known(value, origin)
            : TextInfo.Unknown(origin, unknownReason ?? $"{property} not reported");
    }

    /// <summary>
    /// Reads a property that contains a documented code and names it, keeping the code visible:
    /// <c>DDR4 (SMBIOS code 26)</c>. An unreadable property stays UNKNOWN with the property name as
    /// the reason; a code outside the documented table is reported as such instead of being guessed.
    /// </summary>
    private static TextInfo Code(
        WmiObject? source,
        string property,
        Func<uint, string> nameOf,
        ValueOrigin origin)
    {
        if (source is null || !source.TryGetUInt(property, out var code))
        {
            return TextInfo.Unknown(origin, $"{property} was not reported");
        }

        return TextInfo.Known(SmbiosCodes.Describe(nameOf(code), code), origin);
    }

    private static Measured<uint> UInt(WmiObject? source, string property, ValueOrigin origin) =>
        source is not null && source.TryGetUInt(property, out var value)
            ? Measured<uint>.Known(value, origin)
            : Measured<uint>.Missing($"{property} not reported");

    private static Measured<ulong> ULong(WmiObject? source, string property, ValueOrigin origin) =>
        source is not null && source.TryGetULong(property, out var value)
            ? Measured<ulong>.Known(value, origin)
            : Measured<ulong>.Missing($"{property} not reported");

    private static bool? TryEcc(WmiObject? module)
    {
        if (module is null || !module.TryGetUInt("DataWidth", out var dataWidth) || !module.TryGetUInt("TotalWidth", out var totalWidth))
        {
            return null;
        }

        return totalWidth > dataWidth;
    }

    /// <summary>
    /// Secure Boot as text. A state that was not measured becomes UNKNOWN with the reason from
    /// <see cref="SecureBootReader"/> - "Enabled" is only printed when the firmware said so.
    /// </summary>
    private TextInfo ReadSecureBootText(ValueOrigin origin)
    {
        var reading = SecureBootReader.Read();
        var api = origin with { Source = "GetFirmwareEnvironmentVariable(UEFI)" };
        return reading.Value is { } value
            ? TextInfo.Known(value ? "Enabled" : "Disabled", api)
            : TextInfo.Unknown(api, reading.Detail);
    }

    private static TextInfo DecodeArray(WmiObject monitor, string property, ValueOrigin origin)
    {
        var decoded = Decode(monitor.GetStringArray(property));
        return string.IsNullOrWhiteSpace(decoded) ? TextInfo.Unknown(origin, $"{property} not reported") : TextInfo.Known(decoded, origin);
    }

    private static string Decode(IReadOnlyList<string> characters) => string.Concat(characters).Trim('\0', ' ');

    private TextInfo ReadDisplayVersion()
    {
        // Handled through the registry in the Windows module; kept here as a documented unknown.
        return TextInfo.Unknown(ValueOrigin.Registry(_clock.Now, @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\DisplayVersion"), "DisplayVersion is read by the Windows module");
    }

    private Task<TextInfo> ReadDisplayVersionAsync(CancellationToken cancellationToken) => Task.FromResult(ReadDisplayVersion());

    private static ComponentCategory MapPnpCategory(string? pnpClass) => pnpClass?.ToUpperInvariant() switch
    {
        "PROCESSOR" => ComponentCategory.Cpu,
        "DISPLAY" => ComponentCategory.Graphics,
        "NET" => ComponentCategory.Network,
        "MEDIA" => ComponentCategory.Audio,
        "USB" => ComponentCategory.Usb,
        "SYSTEM" => ComponentCategory.System,
        "MONITOR" => ComponentCategory.Monitor,
        "PRINTER" or "PRINTQUEUE" => ComponentCategory.Printer,
        "BATTERY" => ComponentCategory.Battery,
        "HDC" or "SCSIADAPTER" or "DISKDRIVE" => ComponentCategory.Storage,
        "BLUETOOTH" => ComponentCategory.Network,
        null or "" => ComponentCategory.Unknown,
        _ => ComponentCategory.Pci,
    };

    private static ComponentCategory MapCategoryFromClass(string? deviceClass) => MapPnpCategory(deviceClass);
}

