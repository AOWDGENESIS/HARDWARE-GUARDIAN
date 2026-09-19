using HardwareGuardian.Core.Models;

namespace HardwareGuardian.Core.Abstractions;

/// <summary>
/// Hardware abstraction (spec section 44). The production application uses
/// <c>WindowsHardwareProvider</c>; tests and the simulation mode use <c>MockHardwareProvider</c>.
/// Implementations must never invent values: anything that cannot be read is reported as
/// unavailable together with the technical reason.
/// </summary>
public interface IHardwareProvider
{
    /// <summary>Technical provider name, e.g. <c>WindowsWmiProvider</c> or <c>MockProvider</c>.</summary>
    string ProviderName { get; }

    /// <summary>True when the provider can actually read this machine (false for WMI on non Windows hosts).</summary>
    bool IsAvailable { get; }

    /// <summary>True when this provider returns fixture data instead of real hardware.</summary>
    bool IsSimulation { get; }

    Task<SystemIdentity> GetSystemIdentityAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ProcessorInfo>> GetProcessorsAsync(CancellationToken cancellationToken);

    Task<MemoryInfo> GetMemoryAsync(CancellationToken cancellationToken);

    Task<MotherboardInfo> GetMotherboardAsync(CancellationToken cancellationToken);

    Task<BiosIdentification> GetBiosAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GraphicsAdapterInfo>> GetGraphicsAdaptersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<StorageDeviceInfo>> GetStorageDevicesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AudioDeviceInfo>> GetAudioDevicesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken);

    Task<BatteryInfo?> GetBatteryAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PnpDeviceInfo>> GetPnpDevicesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DriverRecord>> GetDriversAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ThermalZoneReading>> GetThermalZonesAsync(CancellationToken cancellationToken);

    Task<WindowsIdentityInfo> GetWindowsIdentityAsync(CancellationToken cancellationToken);

    /// <summary>Storage reliability counters (SMART/NVMe). Optional: not every device supports it.</summary>
    Task<IReadOnlyList<StorageReliabilityCounter>> GetStorageReliabilityAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reliability counters for one storage device, as far as the device exposes them.
/// </summary>
public sealed record StorageReliabilityCounter
{
    public string DeviceId { get; init; } = string.Empty;

    public Values.Measured<double> TemperatureCelsius { get; init; } = Values.Measured<double>.NotAvailable("not reported by device");

    public Values.Measured<byte> PercentageUsed { get; init; } = Values.Measured<byte>.NotAvailable("not reported by device");

    public Values.Measured<ulong> PowerOnHours { get; init; } = Values.Measured<ulong>.NotAvailable("not reported by device");

    public Values.Measured<ulong> ReadErrorsTotal { get; init; } = Values.Measured<ulong>.NotAvailable("not reported by device");

    public Values.Measured<ulong> WriteErrorsTotal { get; init; } = Values.Measured<ulong>.NotAvailable("not reported by device");

    public Values.Measured<ulong> MediaErrorsTotal { get; init; } = Values.Measured<ulong>.NotAvailable("not reported by device");

    public Values.Measured<uint> PowerCycleCount { get; init; } = Values.Measured<uint>.NotAvailable("not reported by device");

    public Values.Measured<ulong> NvmeBytesWritten { get; init; } = Values.Measured<ulong>.NotAvailable("not reported by device");

    public Values.Measured<ulong> NvmeBytesRead { get; init; } = Values.Measured<ulong>.NotAvailable("not reported by device");

    /// <summary>Raw "wear level" or "health" value when the device reports one, otherwise unknown.</summary>
    public Values.Measured<byte> WearLevelPercent { get; init; } = Values.Measured<byte>.NotAvailable("not reported by device");
}
