using System.Globalization;
using System.Text;

namespace WindowsMaintenanceCenter.Core.Values;

/// <summary>
/// Makes a text that came from outside fit to be written into a document (spec chapter 80, SEC-14
/// "report injection": the report must stay readable, must not execute anything and must not be
/// re-directed).
///
/// Why this lives in Core and not in the report writer: every delivered document shares the risk - the
/// text report, the HTML report, the JSON report, the audit log. One rule, one place; otherwise the
/// documents drift apart and the weakest one decides.
///
/// Two kinds of characters steer a reader instead of informing him:
///
/// * **Control characters** (C0/C1: ESC, BEL, CR, TAB...). A carriage return inside a value starts a
///   new line in a text report, so a device name could add a record that nobody wrote. This part
///   existed from the beginning.
/// * **Format characters** (Unicode category Cf: the bidi overrides U+202A-U+202E, the isolates
///   U+2066-U+2069, zero width space U+200B, the direction marks U+200E/U+200F, the BOM U+FEFF, the
///   soft hyphen U+00AD, and the invisible tag characters U+E0001/U+E0020-U+E007F). They are *not*
///   control characters - no serialiser escapes them, they are legal JSON and legal HTML - and they
///   reorder or hide the visible text. A device name could therefore make a report *read* as a success
///   while the values say something else. Found on 2026-09-22 while checking why the JSON report
///   carried no sanitation at all.
///
/// Such a character is shown as its code point (<c>\u202E</c>) instead of being obeyed: a reader who
/// knows what to look for still finds it, and it cannot do anything.
///
/// Deliberately *not* escaped: ordinary letters, digits, punctuation and non-BMP characters, including
/// emoji from a device name. The rule is "escape what steers the reader", not "remove what is unusual".
/// </summary>
public static class SafeText
{
    /// <summary>Escapes control and format characters; keeps everything else, including empty text.</summary>
    public static string Sanitise(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var firstUnsafe = IndexOfFirstUnsafe(text);
        if (firstUnsafe < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 16);
        builder.Append(text, 0, firstUnsafe);
        for (var index = firstUnsafe; index < text.Length; index++)
        {
            var character = text[index];
            if (IsUnsafe(text, index))
            {
                // Both halves of an invisible astral character are escaped, so the reader sees that
                // something was there instead of losing it silently.
                builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>Index of the first character that must not be obeyed, or -1.</summary>
    private static int IndexOfFirstUnsafe(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (IsUnsafe(text, index))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// True when the character at <paramref name="index"/> must not be obeyed. A surrogate pair is
    /// looked up as one character, so an emoji stays an emoji while a lone half is escaped - text that
    /// cannot be encoded in UTF-8 must not reach a file that claims to be UTF-8.
    /// </summary>
    public static bool IsUnsafe(string text, int index)
    {
        if (index < 0 || index >= text.Length)
        {
            return false;
        }

        return CharUnicodeInfo.GetUnicodeCategory(text, index) switch
        {
            UnicodeCategory.Control => true,
            UnicodeCategory.Format => true,
            UnicodeCategory.Surrogate => true,
            UnicodeCategory.LineSeparator => true,
            UnicodeCategory.ParagraphSeparator => true,
            _ => false,
        };
    }

    /// <summary>Convenience for a single character; a pair must be checked with the string overload.</summary>
    public static bool IsUnsafe(char character) =>
        char.IsControl(character) || char.GetUnicodeCategory(character) == UnicodeCategory.Format;

    /// <summary>True when the text contains a character that <see cref="Sanitise"/> would escape.</summary>
    public static bool ContainsUnsafe(string? text) =>
        !string.IsNullOrEmpty(text) && IndexOfFirstUnsafe(text) >= 0;
}
