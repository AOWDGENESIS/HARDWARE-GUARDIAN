using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Models;

/// <summary>
/// A live sensor reading. The source and its quality are mandatory: an ACPI thermal zone
/// value may never be presented as a precise CPU core temperature (spec sections 8 and 35).
/// </summary>
public sealed record SensorReading
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Localisation key of the reading name, e.g. <c>Sensor_CpuTemperature</c>.</summary>
    public string NameKey { get; init; } = "Sensor_Unknown";

    /// <summary>Component category the reading belongs to.</summary>
    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    public string? ComponentId { get; init; }

    public Measured<double> Value { get; init; } = Measured<double>.NotAvailable("no sensor provider");

    /// <summary>Unit token: <c>C</c>, <c>%</c>, <c>MHz</c>, <c>V</c>, <c>W</c>, <c>RPM</c>, <c>GB</c>.</summary>
    public string Unit { get; init; } = string.Empty;

    public ValueOrigin Origin { get; init; } = ValueOrigin.Unknown;

    public SensorQuality Quality { get; init; } = SensorQuality.Unknown;

    /// <summary>Localisation key describing the measurement point, e.g. <c>SensorPoint_AcpiThermalZone</c>.</summary>
    public string MeasurementPointKey { get; init; } = "SensorPoint_Unknown";

    public DateTimeOffset SampledAt { get; init; }

    /// <summary>True when the platform does not expose this reading at all on this machine.</summary>
    public bool NotSupported { get; init; }

    public static SensorReading NotAvailable(string id, string nameKey, ComponentCategory category, string unit, string reason, string measurementPointKey = "SensorPoint_Unknown") => new()
    {
        Id = id,
        NameKey = nameKey,
        Category = category,
        Unit = unit,
        Value = Measured<double>.NotAvailable(reason),
        Quality = SensorQuality.Unknown,
        MeasurementPointKey = measurementPointKey,
        NotSupported = true,
    };
}

/// <summary>A sensor provider that can be plugged into the sensor service.</summary>
public sealed record SensorProviderInfo
{
    public string Id { get; init; } = string.Empty;

    public string DisplayNameKey { get; init; } = "SensorProvider_Unknown";

    public bool IsAvailable { get; init; }

    public SensorQuality MaxQuality { get; init; } = SensorQuality.Unknown;

    public bool RequiresAdministrator { get; init; }

    public string? UnavailableReason { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>Aggregated live sensor state shown by the sensor view.</summary>
public sealed record SensorSnapshot
{
    public DateTimeOffset SampledAt { get; init; }

    public IReadOnlyList<SensorReading> Readings { get; init; } = Array.Empty<SensorReading>();

    public IReadOnlyList<SensorProviderInfo> Providers { get; init; } = Array.Empty<SensorProviderInfo>();

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Sensor_Summary_None");
}
