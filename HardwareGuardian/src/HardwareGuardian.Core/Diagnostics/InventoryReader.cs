using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Diagnostics;

/// <summary>Raw inventory plus the problems that occurred while reading it.</summary>
public sealed record InventoryResult
{
    public SystemIdentity System { get; init; } = new();

    public BiosIdentification Bios { get; init; } = new();

    public MotherboardInfo Motherboard { get; init; } = new();

    public IReadOnlyList<ProcessorInfo> Processors { get; init; } = Array.Empty<ProcessorInfo>();

    public MemoryInfo Memory { get; init; } = new();

    public IReadOnlyList<GraphicsAdapterInfo> Graphics { get; init; } = Array.Empty<GraphicsAdapterInfo>();

    public IReadOnlyList<StorageDeviceInfo> Storage { get; init; } = Array.Empty<StorageDeviceInfo>();

    public IReadOnlyList<NetworkAdapterInfo> Network { get; init; } = Array.Empty<NetworkAdapterInfo>();

    public IReadOnlyList<AudioDeviceInfo> Audio { get; init; } = Array.Empty<AudioDeviceInfo>();

    public IReadOnlyList<MonitorInfo> Monitors { get; init; } = Array.Empty<MonitorInfo>();

    public IReadOnlyList<PrinterInfo> Printers { get; init; } = Array.Empty<PrinterInfo>();

    public BatteryInfo? Battery { get; init; }

    public IReadOnlyList<PnpDeviceInfo> PnpDevices { get; init; } = Array.Empty<PnpDeviceInfo>();

    public IReadOnlyList<DriverRecord> Drivers { get; init; } = Array.Empty<DriverRecord>();

    public IReadOnlyList<ThermalZoneReading> ThermalZones { get; init; } = Array.Empty<ThermalZoneReading>();

    public WindowsIdentityInfo Windows { get; init; } = new();

    public IReadOnlyList<StorageReliabilityCounter> StorageReliability { get; init; } = Array.Empty<StorageReliabilityCounter>();

    public IReadOnlyList<ProblemDraft> Problems { get; init; } = Array.Empty<ProblemDraft>();

    /// <summary>Number of provider calls that returned usable data.</summary>
    public int SuccessfulReads { get; init; }

    public int FailedReads { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Reads every provider method exactly once and converts provider failures into explicit
/// "unknown" findings. A failed read never silently becomes an empty value (spec section 1.3).
/// </summary>
public sealed class InventoryReader
{
    /// <summary>
    /// Number of progress steps one inventory read reports: one per read method of
    /// <see cref="IHardwareProvider"/> (seventeen at the time of writing). A test asserts this
    /// against the interface, so adding a read method cannot leave the progress bar wrong.
    /// </summary>
    public const int StepCount = 17;

    private readonly IHardwareProvider _provider;
    private readonly ILiveProtocol _protocol;
    private readonly IProgressReporter _progress;

    public InventoryReader(IHardwareProvider provider, ILiveProtocol protocol, IProgressReporter progress)
    {
        _provider = provider;
        _protocol = protocol;
        _progress = progress;
    }

    public async Task<InventoryResult> ReadAsync(CancellationToken cancellationToken)
    {
        var problems = new List<ProblemDraft>();
        var notes = new List<string>();
        var successes = 0;
        var failures = 0;

        if (!_provider.IsAvailable)
        {
            _protocol.Error("SYS", LocalizedText.Of("Protocol_ProviderUnavailable", _provider.ProviderName));
            notes.Add($"provider {_provider.ProviderName} reports IsAvailable=false");
            problems.Add(ProviderUnavailableDraft(_provider.ProviderName));
            return new InventoryResult { Problems = problems, Notes = notes, FailedReads = 1 };
        }

        async Task<T> ReadAsync<T>(string module, string actionKey, string source, Func<CancellationToken, Task<T>> read, T emptyValue)
        {
            _progress.ReportStep(actionKey);
            try
            {
                var result = await read(cancellationToken).ConfigureAwait(false);
                successes++;
                _protocol.Success(module, LocalizedText.Of(actionKey));
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures++;
                var detail = $"{source}: {ex.GetType().Name}: {ex.Message}";
                notes.Add(detail);
                _protocol.Error(module, LocalizedText.Of(actionKey), detail);
                problems.Add(ReadFailureDraft(module, actionKey, source, ex));
                return emptyValue;
            }
        }

        var result = new InventoryResult
        {
            System = await ReadAsync("SYS", "Protocol_Read_System", "IHardwareProvider.GetSystemIdentityAsync", _provider.GetSystemIdentityAsync, new SystemIdentity()).ConfigureAwait(false),
            Bios = await ReadAsync("BIOS", "Protocol_Read_Bios", "IHardwareProvider.GetBiosAsync", _provider.GetBiosAsync, new BiosIdentification()).ConfigureAwait(false),
            Motherboard = await ReadAsync("BOARD", "Protocol_Read_Motherboard", "IHardwareProvider.GetMotherboardAsync", _provider.GetMotherboardAsync, new MotherboardInfo()).ConfigureAwait(false),
            Processors = await ReadAsync("CPU", "Protocol_Read_Processors", "IHardwareProvider.GetProcessorsAsync", _provider.GetProcessorsAsync, Array.Empty<ProcessorInfo>()).ConfigureAwait(false),
            Memory = await ReadAsync("RAM", "Protocol_Read_Memory", "IHardwareProvider.GetMemoryAsync", _provider.GetMemoryAsync, new MemoryInfo()).ConfigureAwait(false),
            Graphics = await ReadAsync("GPU", "Protocol_Read_Graphics", "IHardwareProvider.GetGraphicsAdaptersAsync", _provider.GetGraphicsAdaptersAsync, Array.Empty<GraphicsAdapterInfo>()).ConfigureAwait(false),
            Storage = await ReadAsync("STORAGE", "Protocol_Read_Storage", "IHardwareProvider.GetStorageDevicesAsync", _provider.GetStorageDevicesAsync, Array.Empty<StorageDeviceInfo>()).ConfigureAwait(false),
            StorageReliability = await ReadAsync("STORAGE", "Protocol_Read_StorageHealth", "IHardwareProvider.GetStorageReliabilityAsync", _provider.GetStorageReliabilityAsync, Array.Empty<StorageReliabilityCounter>()).ConfigureAwait(false),
            Network = await ReadAsync("NET", "Protocol_Read_Network", "IHardwareProvider.GetNetworkAdaptersAsync", _provider.GetNetworkAdaptersAsync, Array.Empty<NetworkAdapterInfo>()).ConfigureAwait(false),
            Audio = await ReadAsync("AUDIO", "Protocol_Read_Audio", "IHardwareProvider.GetAudioDevicesAsync", _provider.GetAudioDevicesAsync, Array.Empty<AudioDeviceInfo>()).ConfigureAwait(false),
            Monitors = await ReadAsync("MONITOR", "Protocol_Read_Monitors", "IHardwareProvider.GetMonitorsAsync", _provider.GetMonitorsAsync, Array.Empty<MonitorInfo>()).ConfigureAwait(false),
            Printers = await ReadAsync("PRINT", "Protocol_Read_Printers", "IHardwareProvider.GetPrintersAsync", _provider.GetPrintersAsync, Array.Empty<PrinterInfo>()).ConfigureAwait(false),
            PnpDevices = await ReadAsync("PNP", "Protocol_Read_Pnp", "IHardwareProvider.GetPnpDevicesAsync", _provider.GetPnpDevicesAsync, Array.Empty<PnpDeviceInfo>()).ConfigureAwait(false),
            Drivers = await ReadAsync("DRIVER", "Protocol_Read_Drivers", "IHardwareProvider.GetDriversAsync", _provider.GetDriversAsync, Array.Empty<DriverRecord>()).ConfigureAwait(false),
            ThermalZones = await ReadAsync("SENSOR", "Protocol_Read_ThermalZones", "IHardwareProvider.GetThermalZonesAsync", _provider.GetThermalZonesAsync, Array.Empty<ThermalZoneReading>()).ConfigureAwait(false),
            Windows = await ReadAsync("WINDOWS", "Protocol_Read_WindowsIdentity", "IHardwareProvider.GetWindowsIdentityAsync", _provider.GetWindowsIdentityAsync, new WindowsIdentityInfo()).ConfigureAwait(false),
            Problems = problems,
            Notes = notes,
            SuccessfulReads = successes,
            FailedReads = failures,
        };

        BatteryInfo? battery;
        try
        {
            _progress.ReportStep("Protocol_Read_Battery");
            battery = await _provider.GetBatteryAsync(cancellationToken).ConfigureAwait(false);
            successes++;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            failures++;
            var detail = $"IHardwareProvider.GetBatteryAsync: {ex.GetType().Name}: {ex.Message}";
            notes.Add(detail);
            _protocol.Error("BAT", LocalizedText.Of("Protocol_Read_Battery"), detail);
            battery = null;
        }

        return result with
        {
            Battery = battery,
            SuccessfulReads = successes,
            FailedReads = failures,
            Notes = notes,
            Problems = problems,
        };
    }

    private static ProblemDraft ProviderUnavailableDraft(string providerName) => new()
    {
        IdPrefix = "HW-SYS",
        Category = ComponentCategory.System,
        Severity = Severity.Error,
        Title = LocalizedText.Of("Problem_ProviderUnavailable_Title"),
        Description = LocalizedText.Of("Problem_ProviderUnavailable_Description", providerName),
        Evidence = $"IHardwareProvider.IsAvailable=false (provider={providerName})",
        Impact = LocalizedText.Of("Problem_ProviderUnavailable_Impact"),
        RecommendedAction = LocalizedText.Of("Problem_ProviderUnavailable_Action"),
        RequiresAdministrator = false,
        References = new[] { "IHardwareProvider" },
    };

    private static ProblemDraft ReadFailureDraft(string module, string actionKey, string source, Exception ex) => new()
    {
        IdPrefix = ProblemIdPrefixFor(module),
        Category = CategoryFor(module),
        Severity = Severity.Warning,
        Title = LocalizedText.Of("Problem_ReadFailed_Title", source),
        Description = LocalizedText.Of("Problem_ReadFailed_Description", ex.GetType().Name),
        Evidence = $"{source} -> {ex.GetType().FullName}: {ex.Message}",
        Impact = LocalizedText.Of("Problem_ReadFailed_Impact"),
        RecommendedAction = LocalizedText.Of("Problem_ReadFailed_Action"),
        References = new[] { source, actionKey },
    };

    private static string ProblemIdPrefixFor(string module) => module switch
    {
        "CPU" => "HW-CPU",
        "BIOS" => "BIOS",
        "BOARD" => "HW-BOARD",
        "RAM" => "HW-RAM",
        "GPU" => "HW-GPU",
        "STORAGE" => "HW-STORAGE",
        "NET" => "HW-NET",
        "AUDIO" => "HW-AUDIO",
        "MONITOR" => "HW-MON",
        "PRINT" => "HW-PRINT",
        "PNP" => "HW-PNP",
        "DRIVER" => "DRV",
        "SENSOR" => "SENSOR",
        "WINDOWS" => "WIN",
        "BAT" => "HW-BAT",
        _ => "HW-SYS",
    };

    private static ComponentCategory CategoryFor(string module) => module switch
    {
        "CPU" => ComponentCategory.Cpu,
        "BIOS" => ComponentCategory.Bios,
        "BOARD" => ComponentCategory.Motherboard,
        "RAM" => ComponentCategory.Memory,
        "GPU" => ComponentCategory.Graphics,
        "STORAGE" => ComponentCategory.Storage,
        "NET" => ComponentCategory.Network,
        "AUDIO" => ComponentCategory.Audio,
        "MONITOR" => ComponentCategory.Monitor,
        "PRINT" => ComponentCategory.Printer,
        "PNP" => ComponentCategory.Pci,
        "DRIVER" => ComponentCategory.Driver,
        "SENSOR" => ComponentCategory.Sensor,
        "WINDOWS" => ComponentCategory.Windows,
        "BAT" => ComponentCategory.Battery,
        _ => ComponentCategory.System,
    };
}
