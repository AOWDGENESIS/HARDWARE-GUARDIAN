using System.Globalization;

namespace HardwareGuardian.Core.Values;

/// <summary>
/// Describes where a value came from, how trustworthy it is and when it was retrieved.
/// Every single value in Hardware Guardian carries an origin, so that no value can be
/// displayed without stating its provenance (spec sections 1.3, 8, 48, 50).
/// </summary>
public readonly record struct ValueOrigin
{
    public DataSource Source { get; init; }

    public SensorQuality Quality { get; init; }

    /// <summary>Optional technical detail, e.g. the WMI class or the API used. Never localised prose.</summary>
    public string? Detail { get; init; }

    public DateTimeOffset RetrievedAt { get; init; }

    public bool IsKnown => Source != DataSource.Unknown;

    public static ValueOrigin Unknown { get; } = new()
    {
        Source = DataSource.Unknown,
        Quality = SensorQuality.Unknown,
        Detail = null,
        RetrievedAt = default,
    };

    public static ValueOrigin Create(DataSource source, SensorQuality quality, DateTimeOffset retrievedAt, string? detail = null) => new()
    {
        Source = source,
        Quality = quality,
        Detail = detail,
        RetrievedAt = retrievedAt,
    };

    public static ValueOrigin Wmi(DateTimeOffset retrievedAt, string wmiClass, SensorQuality quality = SensorQuality.High) =>
        Create(DataSource.Wmi, quality, retrievedAt, wmiClass);

    public static ValueOrigin Registry(DateTimeOffset retrievedAt, string keyPath) =>
        Create(DataSource.Registry, SensorQuality.High, retrievedAt, keyPath);

    public static ValueOrigin WindowsApi(DateTimeOffset retrievedAt, string api) =>
        Create(DataSource.WindowsApi, SensorQuality.High, retrievedAt, api);

    public static ValueOrigin LocalFile(DateTimeOffset retrievedAt, string path) =>
        Create(DataSource.FileSystem, SensorQuality.High, retrievedAt, path);

    public static ValueOrigin Manufacturer(DateTimeOffset retrievedAt, string adapter, VerificationLevel level) =>
        Create(DataSource.OfficialManufacturer, level >= VerificationLevel.MetadataMatch ? SensorQuality.High : SensorQuality.Limited, retrievedAt, $"{adapter} ({level})");

    /// <summary>Short, non-localised provenance token for logs, CSV and JSON exports.</summary>
    public string Token() => Source switch
    {
        DataSource.Unknown => "UNKNOWN",
        DataSource.LocalData => "LOCAL_DATA",
        DataSource.Wmi => "WMI",
        DataSource.Registry => "REGISTRY",
        DataSource.WindowsApi => "WINDOWS_API",
        DataSource.Smbios => "SMBIOS",
        DataSource.Pnp => "PNP",
        DataSource.FileSystem => "FILE_SYSTEM",
        DataSource.WindowsUpdate => "WINDOWS_UPDATE",
        DataSource.MicrosoftUpdateCatalog => "MICROSOFT_UPDATE_CATALOG",
        DataSource.OfficialManufacturer => "OFFICIAL_MANUFACTURER",
        DataSource.VerifiedOem => "VERIFIED_OEM",
        DataSource.VendorTool => "VENDOR_TOOL",
        DataSource.UserProvided => "USER_PROVIDED",
        DataSource.ThirdParty => "THIRD_PARTY",
        _ => "UNKNOWN",
    };
}

/// <summary>
/// A measurement that may or may not exist. There is no implicit default value: a value that
/// could not be read stays unknown and carries the reason why (spec sections 1.3 and 50).
/// </summary>
public readonly record struct Measured<T> where T : struct
{
    private Measured(T? value, ValueOrigin origin, string? unknownReason)
    {
        Value = value;
        Origin = origin;
        UnknownReason = unknownReason;
    }

    public T? Value { get; }

    public ValueOrigin Origin { get; }

    /// <summary>Technical reason why the value is not available. Never guessed, never a substitute value.</summary>
    public string? UnknownReason { get; }

    public bool HasValue => Value.HasValue;

    public bool IsUnknown => !Value.HasValue;

    public static Measured<T> Known(T value, ValueOrigin origin) => new(value, origin, null);

    public static Measured<T> NotAvailable(string reason, ValueOrigin? origin = null) => new(null, origin ?? ValueOrigin.Unknown, reason);

    public static Measured<T> Missing(string reason) => new(null, ValueOrigin.Unknown, reason);

    /// <summary>Renders the value or the literal <c>UNKNOWN</c>. Never invents a substitute value.</summary>
    public string Display(CultureInfo? culture = null, string? format = null) =>
        Value.HasValue
            ? ((IFormattable)Value.Value).ToString(format, culture ?? CultureInfo.CurrentCulture)
            : "UNKNOWN";

    public override string ToString() => Value.HasValue
        ? ((IFormattable)Value.Value).ToString(null, CultureInfo.InvariantCulture)
        : "UNKNOWN";
}

/// <summary>
/// A textual fact (manufacturer, model, serial, ...) with provenance. Empty or whitespace values
/// are treated as unknown instead of being shown as an empty field.
/// </summary>
public readonly record struct TextInfo
{
    public string? Value { get; init; }

    public ValueOrigin Origin { get; init; }

    public string? UnknownReason { get; init; }

    public bool IsKnown => !string.IsNullOrWhiteSpace(Value);

    public static TextInfo Known(string value, ValueOrigin origin) => new()
    {
        Value = value.Trim(),
        Origin = origin,
    };

    public static TextInfo Unknown(ValueOrigin? origin = null, string? reason = null) => new()
    {
        Value = null,
        Origin = origin ?? ValueOrigin.Unknown,
        UnknownReason = reason,
    };

    /// <summary>Creates a value from raw provider output, mapping empty values to unknown.</summary>
    public static TextInfo From(string? value, ValueOrigin origin, string? unknownReason = null) =>
        string.IsNullOrWhiteSpace(value) ? Unknown(origin, unknownReason) : Known(value, origin);

    public string Display => IsKnown ? Value! : "UNKNOWN";

    public override string ToString() => Display;
}

/// <summary>
/// A user visible text that is stored as a localisation key plus arguments instead of a
/// hard coded string, so that every view, report and log line follows the selected language
/// (spec sections 41 and 42).
/// </summary>
public sealed class LocalizedText : IEquatable<LocalizedText>
{
    public LocalizedText(string key, params object?[] arguments)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Localisation key must not be empty.", nameof(key));
        }

        Key = key;
        Arguments = arguments ?? Array.Empty<object?>();
    }

    public string Key { get; }

    public IReadOnlyList<object?> Arguments { get; }

    public static LocalizedText Of(string key, params object?[] arguments) => new(key, arguments);

    public LocalizedText With(params object?[] arguments) => new(Key, arguments);

    public bool Equals(LocalizedText? other)
    {
        if (other is null)
        {
            return false;
        }

        if (!string.Equals(Key, other.Key, StringComparison.Ordinal) || Arguments.Count != other.Arguments.Count)
        {
            return false;
        }

        for (var i = 0; i < Arguments.Count; i++)
        {
            if (!Equals(Arguments[i], other.Arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as LocalizedText);

    public override int GetHashCode() => HashCode.Combine(Key, Arguments.Count);

    /// <summary>Falls back to the key: unresolved text must never look like real content.</summary>
    public override string ToString() => Key;
}
