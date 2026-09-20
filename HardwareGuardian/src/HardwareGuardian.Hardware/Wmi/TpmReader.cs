namespace HardwareGuardian.Hardware.Wmi;

/// <summary>
/// Raw TPM state as the firmware provider reported it. Every field is nullable on purpose: a
/// provider that does not answer a property must not be turned into "false". <see cref="QueryFailed"/>
/// separates "the provider could not be asked" from "there is no TPM instance".
/// </summary>
public sealed record TpmReading
{
    /// <summary>True when the WMI query itself failed (missing namespace, access denied, no WMI).</summary>
    public bool QueryFailed { get; init; }

    /// <summary>True when the provider returned a Win32_Tpm instance for this machine.</summary>
    public bool HasInstance { get; init; }

    public bool? Enabled { get; init; }

    public bool? Activated { get; init; }

    public bool? Owned { get; init; }

    /// <summary>TPM specification version as reported, for example <c>2.0, 0, 1.16</c>.</summary>
    public string? SpecificationVersion { get; init; }

    public string? ManufacturerId { get; init; }

    public string? ManufacturerVersion { get; init; }

    /// <summary>Why the state is what it is, or why it could not be read.</summary>
    public string Detail { get; init; } = string.Empty;
}

/// <summary>The meaning of a <see cref="TpmReading"/>. Deliberately small and free of text.</summary>
public enum TpmVerdict
{
    /// <summary>Enabled and activated.</summary>
    Ready,

    /// <summary>Enabled but not activated.</summary>
    EnabledNotActivated,

    /// <summary>Present but not enabled.</summary>
    Disabled,

    /// <summary>The provider reported no TPM instance for this machine.</summary>
    NotPresent,

    /// <summary>Nothing could be measured (query failed or the state was not reported).</summary>
    Unknown,
}

/// <summary>
/// Reads the Trusted Platform Module state (acceptance rule 87, DIAG-F-011) and turns it into a
/// verdict.
///
/// Why the split between reading and judging: the verdict is the part that a user reads as a
/// security statement, so it is a pure function that can be tested without a TPM. The documented
/// source of the properties is the Win32_Tpm class in
/// <c>Root\CIMV2\Security\MicrosoftTpm</c>
/// (<c>IsEnabled_InitialValue</c>, <c>IsActivated_InitialValue</c>, <c>IsOwned_InitialValue</c>,
/// <c>SpecVersion</c>, <c>ManufacturerIdTxt</c>, <c>ManufacturerVersion</c>).
///
/// Fail closed: when the namespace is missing (typical for Windows 10/11 in a VM without virtual
/// TPM) or a value was not reported, the verdict is <see cref="TpmVerdict.Unknown"/> - never
/// "disabled" and never "enabled". Reading is the only thing this type does; the TPM is never
/// cleared, prepared or changed (spec section 12).
/// </summary>
public static class TpmReader
{
    public const string Scope = @"\\.\root\CIMV2\Security\MicrosoftTpm";

    public const string WmiClass = "Win32_Tpm";

    /// <summary>Asks the firmware provider for the TPM state.</summary>
    public static async Task<TpmReading> ReadAsync(WmiReader wmi, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wmi);

        var rows = await wmi.QueryAsync(WmiClass, null, Scope, cancellationToken).ConfigureAwait(false);
        var error = wmi.LastError ?? string.Empty;

        if (rows.Count == 0)
        {
            return error.Length > 0
                ? new TpmReading { QueryFailed = true, Detail = error }
                : new TpmReading
                {
                    HasInstance = false,
                    Detail = "the firmware provider returned no TPM instance for this machine",
                };
        }

        var device = rows[0];

        return new TpmReading
        {
            HasInstance = true,
            Enabled = device.TryGetBool("IsEnabled_InitialValue", out var enabled) ? enabled : null,
            Activated = device.TryGetBool("IsActivated_InitialValue", out var activated) ? activated : null,
            Owned = device.TryGetBool("IsOwned_InitialValue", out var owned) ? owned : null,
            SpecificationVersion = device.GetString("SpecVersion"),
            ManufacturerId = device.GetString("ManufacturerIdTxt"),
            ManufacturerVersion = device.GetString("ManufacturerVersion"),
            Detail = "Win32_Tpm instance read via the firmware provider",
        };
    }

    /// <summary>
    /// Turns a reading into a verdict. No verdict is guessed: present without a readable state ends
    /// in <see cref="TpmVerdict.Unknown"/>.
    /// </summary>
    public static TpmVerdict Judge(TpmReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (reading.QueryFailed || !reading.HasInstance)
        {
            return reading.QueryFailed ? TpmVerdict.Unknown : TpmVerdict.NotPresent;
        }

        if (reading.Enabled == false)
        {
            return TpmVerdict.Disabled;
        }

        if (reading.Enabled == true)
        {
            return reading.Activated == false
                ? TpmVerdict.EnabledNotActivated
                : reading.Activated == true
                    ? TpmVerdict.Ready
                    : TpmVerdict.Unknown;
        }

        return TpmVerdict.Unknown;
    }

    /// <summary>
    /// Extracts the specification generation (<c>2.0</c> from <c>2.0, 0, 1.16</c>). Returns null when
    /// the value is missing or does not look like a version - the caller then shows the raw text.
    /// </summary>
    public static string? SpecificationMajor(string? specificationVersion)
    {
        if (string.IsNullOrWhiteSpace(specificationVersion))
        {
            return null;
        }

        var first = specificationVersion.Split(',')[0].Trim();
        return first.Length > 0 ? first : null;
    }
}
