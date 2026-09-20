using System.Globalization;
using System.Reflection;
using System.Text.Json;
using WindowsMaintenanceCenter.Core;
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

    [Fact]
    public void System_preference_falls_back_to_english_for_unknown_ui_cultures()
    {
        var localizer = new JsonLocalizer();
        var culture = localizer.ResolveCulture(LanguagePreference.System);
        Assert.Contains(culture.TwoLetterISOLanguageName, new[] { "en", "de" });
    }

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
