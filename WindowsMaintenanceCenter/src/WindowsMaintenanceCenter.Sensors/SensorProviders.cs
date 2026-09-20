using System.Globalization;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Hardware.Wmi;

namespace WindowsMaintenanceCenter.Sensors;

/// <summary>
/// ACPI thermal zone provider. The value is a zone temperature, never a CPU core temperature,
/// therefore its quality is LIMITED and the UI must show the measurement point (spec section 8).
/// </summary>
public sealed class AcpiThermalZoneProvider : ISensorProvider
{
    private readonly IHardwareProvider _hardware;
    private readonly IClock _clock;

    public AcpiThermalZoneProvider(IHardwareProvider hardware, IClock clock)
    {
        _hardware = hardware;
        _clock = clock;
    }

    public SensorProviderInfo Info { get; } = new()
    {
        Id = "acpi-thermal-zone",
        DisplayNameKey = "SensorProvider_AcpiThermalZone",
        IsAvailable = true,
        MaxQuality = SensorQuality.Limited,
        RequiresAdministrator = false,
        Notes = new[] { "MSAcpi_ThermalZoneTemperature reports a zone, not a core sensor" },
    };

    public async Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken)
    {
        var zones = await _hardware.GetThermalZonesAsync(cancellationToken).ConfigureAwait(false);
        var readings = new List<SensorReading>();

        foreach (var zone in zones)
        {
            readings.Add(new SensorReading
            {
                Id = $"acpi-zone:{zone.InstanceName.Display}",
                NameKey = "Sensor_ThermalZone",
                Category = ComponentCategory.Sensor,
                Value = zone.TemperatureCelsius,
                Unit = "C",
                Origin = zone.TemperatureCelsius.Origin,
                Quality = SensorQuality.Limited,
                MeasurementPointKey = "SensorPoint_AcpiThermalZone",
                SampledAt = _clock.Now,
                NotSupported = !zone.TemperatureCelsius.HasValue,
            });
        }

        if (readings.Count == 0)
        {
            readings.Add(SensorReading.NotAvailable(
                "acpi-zone",
                "Sensor_ThermalZone",
                ComponentCategory.Sensor,
                "C",
                "no ACPI thermal zone was reported by the firmware",
                "SensorPoint_AcpiThermalZone"));
        }

        return readings;
    }
}

/// <summary>Storage temperature and wear as reported by the storage stack (higher quality).</summary>
public sealed class StorageSensorProvider : ISensorProvider
{
    private readonly IHardwareProvider _hardware;
    private readonly IClock _clock;

    public StorageSensorProvider(IHardwareProvider hardware, IClock clock)
    {
        _hardware = hardware;
        _clock = clock;
    }

    public SensorProviderInfo Info { get; } = new()
    {
        Id = "storage-reliability",
        DisplayNameKey = "SensorProvider_Storage",
        IsAvailable = true,
        MaxQuality = SensorQuality.High,
        RequiresAdministrator = false,
        Notes = new[] { "MSFT_StorageReliabilityCounter" },
    };

    public async Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken)
    {
        var counters = await _hardware.GetStorageReliabilityAsync(cancellationToken).ConfigureAwait(false);
        var readings = new List<SensorReading>();

        foreach (var counter in counters)
        {
            readings.Add(new SensorReading
            {
                Id = $"storage-temp:{counter.DeviceId}",
                NameKey = "Sensor_DiskTemperature",
                Category = ComponentCategory.Storage,
                ComponentId = counter.DeviceId,
                Value = counter.TemperatureCelsius,
                Unit = "C",
                Origin = counter.TemperatureCelsius.Origin,
                Quality = counter.TemperatureCelsius.HasValue ? SensorQuality.High : SensorQuality.Unknown,
                MeasurementPointKey = "SensorPoint_StorageDevice",
                SampledAt = _clock.Now,
                NotSupported = !counter.TemperatureCelsius.HasValue,
            });

            readings.Add(new SensorReading
            {
                Id = $"storage-wear:{counter.DeviceId}",
                NameKey = "Sensor_DiskWear",
                Category = ComponentCategory.Storage,
                ComponentId = counter.DeviceId,
                Value = counter.PercentageUsed.HasValue ? Measured<double>.Known(counter.PercentageUsed.Value!.Value, counter.PercentageUsed.Origin) : Measured<double>.Missing(counter.PercentageUsed.UnknownReason ?? "wear not reported"),
                Unit = "%",
                Origin = counter.PercentageUsed.Origin,
                Quality = counter.PercentageUsed.HasValue ? SensorQuality.High : SensorQuality.Unknown,
                MeasurementPointKey = "SensorPoint_StorageDevice",
                SampledAt = _clock.Now,
                NotSupported = !counter.PercentageUsed.HasValue,
            });
        }

        if (readings.Count == 0)
        {
            readings.Add(SensorReading.NotAvailable("storage-temp", "Sensor_DiskTemperature", ComponentCategory.Storage, "C", "the storage stack did not report reliability counters", "SensorPoint_StorageDevice"));
        }

        return readings;
    }
}

/// <summary>CPU load and memory usage through the performance data providers.</summary>
public sealed class PerformanceSensorProvider : ISensorProvider
{
    private readonly WmiReader _wmi;
    private readonly IHardwareProvider _hardware;
    private readonly IClock _clock;

    public PerformanceSensorProvider(WmiReader wmi, IHardwareProvider hardware, IClock clock)
    {
        _wmi = wmi;
        _hardware = hardware;
        _clock = clock;
    }

    public SensorProviderInfo Info { get; } = new()
    {
        Id = "performance-counters",
        DisplayNameKey = "SensorProvider_Performance",
        IsAvailable = true,
        MaxQuality = SensorQuality.High,
        RequiresAdministrator = false,
        Notes = new[] { "Win32_PerfFormattedData_PerfOS_Processor" },
    };

    public async Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>();

        var processors = await _wmi.QueryAsync("Win32_PerfFormattedData_PerfOS_Processor", cancellationToken: cancellationToken).ConfigureAwait(false);
        var total = processors.FirstOrDefault(p => string.Equals(p.GetString("Name"), "_Total", StringComparison.OrdinalIgnoreCase)) ?? processors.FirstOrDefault();

        var origin = ValueOrigin.Wmi(_clock.Now, "Win32_PerfFormattedData_PerfOS_Processor", SensorQuality.High);
        readings.Add(new SensorReading
        {
            Id = "cpu-load",
            NameKey = "Sensor_CpuLoad",
            Category = ComponentCategory.Cpu,
            Value = total is not null && total.TryGetUInt("PercentProcessorTime", out var load)
                ? Measured<double>.Known(load, origin)
                : Measured<double>.Missing("PercentProcessorTime not reported"),
            Unit = "%",
            Origin = origin,
            Quality = total is null ? SensorQuality.Unknown : SensorQuality.High,
            MeasurementPointKey = "SensorPoint_ProcessorTotal",
            SampledAt = _clock.Now,
            NotSupported = total is null,
        });

        var memory = await _hardware.GetMemoryAsync(cancellationToken).ConfigureAwait(false);
        readings.Add(new SensorReading
        {
            Id = "ram-usage",
            NameKey = "Sensor_RamUsage",
            Category = ComponentCategory.Memory,
            Value = memory.MemoryUsagePercent.HasValue
                ? Measured<double>.Known(memory.MemoryUsagePercent.Value!.Value, memory.MemoryUsagePercent.Origin)
                : Measured<double>.Missing(memory.MemoryUsagePercent.UnknownReason ?? "memory usage not derivable"),
            Unit = "%",
            Origin = memory.MemoryUsagePercent.Origin,
            Quality = memory.MemoryUsagePercent.HasValue ? SensorQuality.High : SensorQuality.Unknown,
            MeasurementPointKey = "SensorPoint_OperatingSystem",
            SampledAt = _clock.Now,
            NotSupported = !memory.MemoryUsagePercent.HasValue,
        });

        var cpu = (await _hardware.GetProcessorsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        readings.Add(new SensorReading
        {
            Id = "cpu-clock",
            NameKey = "Sensor_CpuClock",
            Category = ComponentCategory.Cpu,
            Value = cpu?.CurrentClockMhz.HasValue == true
                ? Measured<double>.Known(cpu.CurrentClockMhz.Value!.Value, cpu.CurrentClockMhz.Origin)
                : Measured<double>.Missing(cpu?.CurrentClockMhz.UnknownReason ?? "current clock not reported"),
            Unit = "MHz",
            Origin = cpu?.CurrentClockMhz.Origin ?? ValueOrigin.Unknown,
            Quality = cpu?.CurrentClockMhz.HasValue == true ? SensorQuality.High : SensorQuality.Unknown,
            MeasurementPointKey = "SensorPoint_ProcessorTotal",
            SampledAt = _clock.Now,
            NotSupported = cpu?.CurrentClockMhz.HasValue != true,
        });

        return readings;
    }
}

/// <summary>
/// Optional vendor tool provider (for example nvidia-smi). It is only used when the tool is
/// installed, and it says so in the UI. No bundled third-party driver component is required.
/// </summary>
public sealed class VendorToolSensorProvider : ISensorProvider
{
    private readonly IProcessRunner _runner;
    private readonly IEnvironmentProbe _environment;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;

    public VendorToolSensorProvider(IProcessRunner runner, IEnvironmentProbe environment, ISettingsService settings, IClock clock)
    {
        _runner = runner;
        _environment = environment;
        _settings = settings;
        _clock = clock;
    }

    public SensorProviderInfo Info
    {
        get
        {
            var tool = FindTool();
            return new SensorProviderInfo
            {
                Id = "vendor-tool",
                DisplayNameKey = "SensorProvider_VendorTool",
                IsAvailable = tool is not null && _settings.Current.UseVendorTools,
                MaxQuality = SensorQuality.High,
                RequiresAdministrator = false,
                UnavailableReason = tool is null
                    ? "no supported vendor tool found (nvidia-smi)"
                    : _settings.Current.UseVendorTools ? null : "disabled in settings",
                Notes = new[] { "Only documented vendor command line tools are used, never third-party libraries" },
            };
        }
    }

    public async Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken)
    {
        var tool = FindTool();
        if (tool is null || !_settings.Current.UseVendorTools)
        {
            return new[]
            {
                SensorReading.NotAvailable("gpu-temp", "Sensor_GpuTemperature", ComponentCategory.Graphics, "C", "no supported vendor tool available", "SensorPoint_VendorTool"),
                SensorReading.NotAvailable("gpu-load", "Sensor_GpuLoad", ComponentCategory.Graphics, "%", "no supported vendor tool available", "SensorPoint_VendorTool"),
            };
        }

        var result = await _runner.RunAsync(
            tool,
            new[] { "--query-gpu=temperature.gpu,utilization.gpu,clocks.current.graphics", "--format=csv,noheader,nounits" },
            new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(10) },
            cancellationToken).ConfigureAwait(false);

        var origin = ValueOrigin.Create(DataSource.VendorTool, SensorQuality.High, _clock.Now, Path.GetFileName(tool));
        var readings = new List<SensorReading>();

        if (!result.Succeeded)
        {
            readings.Add(SensorReading.NotAvailable("gpu-temp", "Sensor_GpuTemperature", ComponentCategory.Graphics, "C", $"vendor tool failed: {result.ErrorDetail ?? result.StandardError.Trim()}", "SensorPoint_VendorTool"));
            return readings;
        }

        var line = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        var parts = line?.Split(',').Select(p => p.Trim()).ToArray() ?? Array.Empty<string>();

        readings.Add(new SensorReading
        {
            Id = "gpu-temp",
            NameKey = "Sensor_GpuTemperature",
            Category = ComponentCategory.Graphics,
            Value = parts.Length > 0 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature)
                ? Measured<double>.Known(temperature, origin)
                : Measured<double>.Missing("vendor tool did not return a temperature"),
            Unit = "C",
            Origin = origin,
            Quality = parts.Length > 0 ? SensorQuality.High : SensorQuality.Unknown,
            MeasurementPointKey = "SensorPoint_VendorTool",
            SampledAt = _clock.Now,
        });

        readings.Add(new SensorReading
        {
            Id = "gpu-load",
            NameKey = "Sensor_GpuLoad",
            Category = ComponentCategory.Graphics,
            Value = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var utilization)
                ? Measured<double>.Known(utilization, origin)
                : Measured<double>.Missing("vendor tool did not return a utilisation value"),
            Unit = "%",
            Origin = origin,
            Quality = parts.Length > 1 ? SensorQuality.High : SensorQuality.Unknown,
            MeasurementPointKey = "SensorPoint_VendorTool",
            SampledAt = _clock.Now,
        });

        return readings;
    }

    private static string? FindTool()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
        };

        var system32DriverStore = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore");
        return candidates.FirstOrDefault(File.Exists) ?? TryFindInDriverStore(system32DriverStore);
    }

    private static string? TryFindInDriverStore(string driverStore)
    {
        try
        {
            return Directory.Exists(driverStore)
                ? Directory.EnumerateFiles(driverStore, "nvidia-smi.exe", SearchOption.AllDirectories).FirstOrDefault()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
