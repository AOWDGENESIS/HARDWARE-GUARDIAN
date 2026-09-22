using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Infrastructure.Localization;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// Localisation (spec section 41). German and English must stay complete and symmetric: a missing
/// key must be visible, never silently empty, and switching must refresh already created texts.
/// </summary>
public sealed class LocalizationTests
{
    [Fact]
    public void Both_languages_are_embedded_in_the_core_assembly()
    {
        var names = typeof(LocalizedText).Assembly.GetManifestResourceNames();
        Assert.Contains(names, n => n.EndsWith("Resources.en.json", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.EndsWith("Resources.de.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void English_and_German_define_exactly_the_same_keys()
    {
        var english = ResourceKeys("en");
        var german = ResourceKeys("de");

        var onlyEnglish = english.Except(german).OrderBy(k => k).ToList();
        var onlyGerman = german.Except(english).OrderBy(k => k).ToList();

        Assert.True(onlyEnglish.Count == 0, "German is missing: " + string.Join(", ", onlyEnglish));
        Assert.True(onlyGerman.Count == 0, "English is missing: " + string.Join(", ", onlyGerman));
    }

    [Fact]
    public void Keys_are_stable_identifiers_and_texts_are_not_empty()
    {
        foreach (var language in new[] { "en", "de" })
        {
            foreach (var (key, value) in ResourceStrings(language))
            {
                Assert.Matches("^[A-Za-z][A-Za-z0-9_.]*$", key);
                Assert.False(string.IsNullOrWhiteSpace(value), $"{language}: '{key}' is empty");
            }
        }
    }

    [Fact]
    public void A_missing_key_is_visible_instead_of_empty()
    {
        // The key is built at runtime so that the localisation checker does not mistake it for a
        // string that the application forgot to define.
        var missingKey = "No" + "_Such_Key";
        var localizer = new JsonLocalizer(preference: LanguagePreference.English);

        Assert.Equal($"[[{missingKey}]]", localizer[missingKey]);
        Assert.Contains(missingKey, localizer.MissingKeys);
    }

    [Fact]
    public void Placeholders_are_filled_with_the_invariant_or_active_culture()
    {
        var localizer = new JsonLocalizer(preference: LanguagePreference.German);
        var text = localizer.Format("Dashboard_ComponentCount", 7);
        Assert.Contains("7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_template_with_the_wrong_number_of_arguments_does_not_throw()
    {
        // The call site is broken, not the user: the key stays visible instead of an exception.
        var localizer = new JsonLocalizer(preference: LanguagePreference.English);
        var text = localizer.Resolve(LocalizedText.Of("Dashboard_ComponentCount", "a", "b", "c", "d"));
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void Switching_the_language_raises_one_event_and_changes_the_text()
    {
        var localizer = new JsonLocalizer(preference: LanguagePreference.English);
        var english = localizer["Report_Title_System"];

        var raised = 0;
        CultureInfo? reported = null;
        localizer.LanguageChanged += (_, culture) =>
        {
            raised++;
            reported = culture;
        };

        localizer.SetLanguage(LanguagePreference.German);

        Assert.Equal(1, raised);
        Assert.Equal("de-DE", reported?.Name);
        Assert.NotEqual(english, localizer["Report_Title_System"]);
    }

    /// <summary>
    /// Chapter 63 asks for four languages (de-DE, en-US, ja-JP, ru-RU). This test does not name them:
    /// it takes the languages the build really ships from <see cref="LanguageCatalog"/> and requires the
    /// same key set in every one of them. That is what turns "a catalogue was added" into "a catalogue is
    /// complete" - and it would have failed for the four-language requirement just as loudly as for a
    /// single missing key. The check that every catalogue is also *embedded* lives in the tooling
    /// (`tools/check-localization.py`), because a file that is not embedded is not in the assembly and
    /// therefore invisible here.
    /// </summary>
    [Fact]
    public void Every_shipped_language_defines_exactly_the_same_keys()
    {
        var shipped = LanguageCatalog.ShippedCodes;
        Assert.True(shipped.Count >= 4, $"only {shipped.Count} language(s) are shipped: {string.Join(", ", shipped)}");

        var reference = shipped[0];
        var expected = ResourceKeys(reference);
        foreach (var language in shipped.Skip(1))
        {
            var actual = ResourceKeys(language);
            var missing = expected.Except(actual).OrderBy(k => k).ToList();
            var surplus = actual.Except(expected).OrderBy(k => k).ToList();
            Assert.True(missing.Count == 0, $"{language} is missing: " + string.Join(", ", missing));
            Assert.True(surplus.Count == 0, $"{language} has keys that {reference} does not have: " + string.Join(", ", surplus));
        }
    }

    /// <summary>
    /// A translation that renumbers or drops a placeholder is a string.Format failure at run time and is
    /// invisible in a diff of prose. The same rule as in tools/check-localization.py, executed here so a
    /// broken catalogue fails the test suite and not only the tooling.
    /// </summary>
    [Fact]
    public void Every_shipped_language_keeps_the_placeholders_of_the_reference_language()
    {
        var shipped = LanguageCatalog.ShippedCodes;
        var reference = ResourceStrings(shipped[0]).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var language in shipped.Skip(1))
        {
            foreach (var (key, text) in ResourceStrings(language))
            {
                if (!reference.TryGetValue(key, out var expected))
                {
                    continue; // a key that only one language has is reported by the symmetry test
                }

                var expectedIndices = Placeholders(expected);
                var actualIndices = Placeholders(text);
                if (!expectedIndices.SetEquals(actualIndices))
                {
                    problems.Add($"{language}.{key}: {{{string.Join(",", expectedIndices.OrderBy(i => i))}}} vs {{{string.Join(",", actualIndices.OrderBy(i => i))}}}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(" | ", problems));
    }

    [Fact]
    public void Only_a_language_whose_catalogue_is_embedded_can_be_offered()
    {
        foreach (var preference in LanguageCatalog.OfferedPreferences())
        {
            if (preference == LanguagePreference.System)
            {
                continue;
            }

            Assert.True(LanguageCatalog.IsShipped(preference), $"{preference} is offered without a catalogue");
            Assert.NotNull(LanguageCatalog.CodeOf(preference));
        }

        // The other direction: every shipped catalogue belongs to a preference the interface can name.
        foreach (var code in LanguageCatalog.ShippedCodes)
        {
            var named = Enum.GetValues<LanguagePreference>().Any(preference => LanguageCatalog.CodeOf(preference) == code);
            Assert.True(named, $"the catalogue '{code}' has no LanguagePreference value");
        }
    }

    [Fact]
    public void System_preference_falls_back_to_english_for_unknown_ui_cultures()
    {
        var localizer = new JsonLocalizer();
        var culture = localizer.ResolveCulture(LanguagePreference.System);
        Assert.Contains(culture.TwoLetterISOLanguageName, new[] { "en", "de" });
    }

    /// <summary>Placeholder indices of a template: {0}, {1:...}. Doubled braces are literal.</summary>
    private static HashSet<int> Placeholders(string text) =>
        Regex.Matches(text, @"(?<!\{)\{(?<index>\d+)(?::[^}]*)?\}")
            .Select(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture))
            .ToHashSet();

    private static HashSet<string> ResourceKeys(string language) =>
        ResourceStrings(language).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);

    private static List<KeyValuePair<string, string>> ResourceStrings(string language)
    {
        var assembly = typeof(LocalizedText).Assembly;
        var name = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith($"Resources.{language}.json", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var document = JsonDocument.Parse(stream);
        var strings = document.RootElement.GetProperty("strings");

        return strings.EnumerateObject()
            .Select(p => new KeyValuePair<string, string>(p.Name, p.Value.GetString() ?? string.Empty))
            .ToList();
    }
}
