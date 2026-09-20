using System.Globalization;

namespace WindowsMaintenanceCenter.Core.Values;

/// <summary>
/// Formats a measured byte count for display.
///
/// Why this exists as one function: sizes were formatted inline with a hard-coded "MB" and a
/// hard-coded "UNKNOWN" fallback in the view models (visible English text inside a German UI, and a
/// value that pretended to be a measurement). A byte count is shown in the unit that fits it, the
/// number is formatted with the current culture, and a value that was not measured renders as the
/// caller's own "not available" text - the caller decides the words, this type only does the maths.
/// </summary>
public static class SizeText
{
    private static readonly string[] Units = { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };

    /// <summary>Formats a byte count, for example <c>1,5 GiB</c> in a German culture.</summary>
    public static string Format(ulong bytes, CultureInfo? culture = null)
    {
        var current = culture ?? CultureInfo.CurrentCulture;
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024d && unit < Units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        // Bytes are whole numbers; larger units carry one decimal because that is the digit a user
        // compares ("1,5 GiB" against a 2 GiB free space). The unit symbol is not translated: KiB
        // and MiB are the same in every language.
        var format = unit == 0 ? "0" : "0.#";
        return $"{(value).ToString(format, current)} {Units[unit]}";
    }

    /// <summary>
    /// Formats a measurement, or returns <paramref name="notAvailableText"/> when it was not
    /// measured. Both signed and unsigned sizes occur in the models (directory sizes are signed,
    /// download sizes are not), so both overloads exist instead of a conversion at every call site.
    /// A negative size is not a size: it renders as not available rather than as a negative number.
    /// </summary>
    public static string Format(Measured<ulong> measured, CultureInfo? culture, string notAvailableText) =>
        measured.Value is { } bytes ? Format(bytes, culture) : notAvailableText;

    /// <inheritdoc cref="Format(Measured{ulong}, CultureInfo?, string)"/>
    public static string Format(Measured<long> measured, CultureInfo? culture, string notAvailableText) =>
        measured.Value is { } bytes && bytes >= 0 ? Format((ulong)bytes, culture) : notAvailableText;
}
