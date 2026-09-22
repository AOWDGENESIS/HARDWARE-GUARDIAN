using System.Reflection;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// The languages this build actually ships.
///
/// Why this exists: chapter 63 asks for four languages (de-DE, en-US, ja-JP, ru-RU). The interface may
/// only offer a language whose catalogue is really embedded - an offered language that silently falls
/// back to English is a wrong statement about the product, and the specification forbids exactly that
/// (chapters 86/96). So the list is *measured* from the assembly instead of being written down twice:
/// every embedded <c>Resources/&lt;code&gt;.json</c> is a shipped language, and nothing else is.
///
/// The consequence is the point: adding a language file is enough. No list has to be kept in step, and
/// a language file that is not embedded cannot be offered by accident either.
///
/// The localisation service loads the same set (<see cref="JsonLocalizer"/> uses
/// <see cref="ShippedCodes"/>), and the acceptance kit measures the result: a language whose catalogue
/// misses keys is reported by <c>tools/check-localization.py</c> and by the unit suite.
/// </summary>
public static class LanguageCatalog
{
    /// <summary>
    /// Two-letter codes of the shipped catalogues, in a stable order (English first, then alphabetical).
    /// Stable, because a settings file records the choice and two runs of the same build must offer the
    /// same list in the same order.
    /// </summary>
    public static IReadOnlyList<string> ShippedCodes { get; } = Discover();

    /// <summary>True when the build ships a catalogue for this preference (System is not a language of its own).</summary>
    public static bool IsShipped(LanguagePreference preference) =>
        CodeOf(preference) is { } code && ShippedCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

    /// <summary>The two-letter code of a preference, or null for <see cref="LanguagePreference.System"/>.</summary>
    public static string? CodeOf(LanguagePreference preference) => preference switch
    {
        LanguagePreference.German => "de",
        LanguagePreference.English => "en",
        LanguagePreference.Japanese => "ja",
        LanguagePreference.Russian => "ru",

        // System means "whatever Windows is set to"; which catalogue that is, is decided by the culture
        // at run time, so there is no code to hand back here.
        _ => null,
    };

    /// <summary>Every preference this build can offer: System, plus the languages whose catalogue exists.</summary>
    public static IReadOnlyList<LanguagePreference> OfferedPreferences()
    {
        // Written as `new[]` on purpose: the rest of the code base is written that way, and a form that
        // occurs once is a form nobody reviews. The order is the order the interface offers.
        var candidates = new[]
        {
            LanguagePreference.System,
            LanguagePreference.German,
            LanguagePreference.English,
            LanguagePreference.Japanese,
            LanguagePreference.Russian,
        };

        return candidates
            .Where(preference => preference == LanguagePreference.System || IsShipped(preference))
            .ToArray();
    }

    private static IReadOnlyList<string> Discover()
    {
        var names = typeof(LocalizedText).Assembly.GetManifestResourceNames();
        var codes = new List<string>();

        foreach (var name in names)
        {
            const string marker = ".Resources.";
            var start = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0 || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var code = name[(start + marker.Length)..^".json".Length];

            // A code is a short language tag: letters only, optionally a region ("pt-BR" would be two
            // letters, a dash and more letters). Anything else is not a catalogue of this project.
            if (code.Length is < 2 or > 6 || !code.All(character => char.IsAsciiLetter(character) || character == '-'))
            {
                continue;
            }

            if (!codes.Contains(code, StringComparer.OrdinalIgnoreCase))
            {
                codes.Add(code);
            }
        }

        if (codes.Count == 0)
        {
            // A build without a single catalogue is broken, and every text in the interface would show as
            // a missing-key marker. Saying so here is better than starting and looking translated-broken;
            // `JsonLocalizer.LoadEmbedded` throws for the same reason.
            throw new InvalidOperationException(
                "No localisation catalogue is embedded in this build (expected Resources/<language>.json). " +
                "The build is incomplete - see the EmbeddedResource entries of the Core project.");
        }

        return codes
            .OrderBy(code => string.Equals(code, "en", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(code => code, StringComparer.Ordinal)
            .ToArray();
    }
}
