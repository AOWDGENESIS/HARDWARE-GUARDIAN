using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Models;

/// <summary>Every hardware component is represented as one of these, including its provenance.</summary>
public sealed record HardwareComponent
{
    public string Id { get; init; } = string.Empty;

    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    /// <summary>Localisation key of the component class, e.g. <c>Component_Cpu</c>.</summary>
    public string CategoryKey { get; init; } = "Component_Unknown";

    public TextInfo Name { get; init; }

    public TextInfo Manufacturer { get; init; }

    public TextInfo Model { get; init; }

    public HealthStatus Status { get; init; } = HealthStatus.Unknown;

    public IReadOnlyList<ComponentProperty> Properties { get; init; } = Array.Empty<ComponentProperty>();

    public IReadOnlyList<string> HardwareIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CompatibleIds { get; init; } = Array.Empty<string>();

    public TextInfo DeviceInstanceId { get; init; }

    public DriverInfo? Driver { get; init; }

    public IReadOnlyList<Problem> Problems { get; init; } = Array.Empty<Problem>();

    /// <summary>Non-localised diagnostic notes, e.g. which sources were compared.</summary>
    public IReadOnlyList<string> DiagnosticNotes { get; init; } = Array.Empty<string>();

    /// <summary>Confirmed manufacturer support source, when one exists (spec section 48).</summary>
    public ManufacturerSourceRef? SupportSource { get; init; }

    /// <summary>Stable display order for the hardware view.</summary>
    public int SortOrder { get; init; }
}

/// <summary>A property row of a component. The label is a localisation key, the value is data.</summary>
public sealed record ComponentProperty
{
    public ComponentProperty(string labelKey, TextInfo value)
    {
        LabelKey = labelKey;
        Value = value;
        Quality = value.Origin.Quality;
        Origin = value.Origin;
    }

    public ComponentProperty(string labelKey, string? rawValue, ValueOrigin origin)
        : this(labelKey, TextInfo.From(rawValue, origin))
    {
    }

    public string LabelKey { get; }

    public TextInfo Value { get; }

    public ValueOrigin Origin { get; }

    public SensorQuality Quality { get; }

    public bool IsKnown => Value.IsKnown;

    public string Display => Value.Display;
}

/// <summary>Driver binding of a component as shown in the hardware/driver views.</summary>
public sealed record DriverInfo
{
    public TextInfo Provider { get; init; }

    public TextInfo Version { get; init; }

    public TextInfo Date { get; init; }

    public TextInfo DeviceClass { get; init; }

    public TextInfo InfName { get; init; }

    public TextInfo FileName { get; init; }

    public TextInfo Signer { get; init; }

    public TextInfo Status { get; init; }

    /// <summary>Verified signature state. <c>null</c> means "not verified", never "unsigned".</summary>
    public bool? IsSignatureVerified { get; init; }

    /// <summary>True when the driver is supplied by Microsoft with Windows (inbox).</summary>
    public bool IsInbox { get; init; }

    /// <summary>True when Windows installed a generic fallback driver instead of the vendor driver.</summary>
    public bool IsGenericFallback { get; init; }

    public VerificationLevel SignatureVerification { get; init; } = VerificationLevel.NotVerified;

    public TextInfo HardwareId { get; init; }

    public Measured<uint> ProblemCode { get; init; } = Measured<uint>.NotAvailable("no problem code reported");
}

/// <summary>Reference to a verified manufacturer source.</summary>
public sealed record ManufacturerSourceRef
{
    public string AdapterId { get; init; } = string.Empty;

    /// <summary>Localisation key of the adapter/source name, e.g. <c>Vendor_AMD</c>.</summary>
    public string DisplayNameKey { get; init; } = "Source_Unknown";

    public string? Url { get; init; }

    public SourceTrust Trust { get; init; } = SourceTrust.Unknown;

    public VerificationLevel Verification { get; init; } = VerificationLevel.NotVerified;

    public DateTimeOffset? RetrievedAt { get; init; }

    public string? Note { get; init; }

    public static ManufacturerSourceRef Unknown(string? note = null) => new()
    {
        AdapterId = "unknown",
        DisplayNameKey = "Source_Unknown",
        Trust = SourceTrust.Unknown,
        Verification = VerificationLevel.NotVerified,
        Note = note,
    };
}
