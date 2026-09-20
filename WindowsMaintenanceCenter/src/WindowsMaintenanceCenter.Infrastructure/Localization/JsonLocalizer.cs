using System.Globalization;
using System.Reflection;
using System.Text.Json;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Infrastructure.Localization;

/// <summary>
/// Localisation from embedded JSON resources (spec section 41).
///
/// Sources of truth, in this order:
///   1. embedded <c>Resources/&lt;language&gt;.json</c> of the Core assembly (always present, also portable),
///   2. optional override files <c>&lt;configuration&gt;/localization/&lt;language&gt;.json</c>, so an
///      administrator can correct or extend wording without a new build.
///
/// A missing key never becomes an empty string: the caller sees a visible marker such as
/// <c>[[Some_Key]]</c> and the key is recorded in <see cref="MissingKeys"/> for the diagnostics view.
/// Language switching takes effect immediately, including for texts that were already created,
/// because every text is resolved at display time.
/// </summary>
public sealed class JsonLocalizer : ILocalizer
{
    private const string OverrideSubdirectory = "localization";
    private const string MissingFormat = "[[{0}]]";

    private readonly Dictionary<string, Dictionary<string, string>> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly string? _overrideDirectory;
    private readonly ISettingsService? _settings;

    private LanguagePreference _preference;
    private CultureInfo _culture;

    public JsonLocalizer(string? overrideDirectory = null, ISettingsService? settings = null, LanguagePreference preference = LanguagePreference.System)
    {
        _overrideDirectory = overrideDirectory;
        _settings = settings;

        foreach (var language in new[] { "en", "de" })
        {
            var strings = LoadEmbedded(language);
            ApplyOverrides(language, strings);
            _catalogs[language] = strings;
        }

        _preference = preference;
        _culture = ResolveCulture(preference);
    }

    public event EventHandler<CultureInfo>? LanguageChanged;

    public CultureInfo Culture => _culture;

    public LanguagePreference Preference => _preference;

    public IReadOnlyCollection<string> Keys
    {
        get
        {
            lock (_gate)
            {
                return _catalogs.SelectMany(c => c.Value.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
            }
        }
    }

    /// <summary>
    /// Keys that were requested but not found in the active language. Shown in the diagnostics view
    /// instead of hiding the gap - a visible marker in the UI and a list here are the honest variant.
    /// </summary>
    public IReadOnlyCollection<string> MissingKeys
    {
        get
        {
            lock (_gate)
            {
                return _missing.OrderBy(k => k, StringComparer.Ordinal).ToList();
            }
        }
    }

    public string this[string key] => Format(key, Array.Empty<object?>());

    public string Format(string key, params object?[] arguments)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Format(CultureInfo.InvariantCulture, MissingFormat, "empty_key");
        }

        if (!TryGetTemplate(key, out var template))
        {
            MarkMissing(key);
            return string.Format(CultureInfo.InvariantCulture, MissingFormat, key);
        }

        if (arguments is null || arguments.Length == 0 || !template.Contains('{', StringComparison.Ordinal))
        {
            return template;
        }

        try
        {
            return string.Format(_culture, template, arguments);
        }
        catch (FormatException)
        {
            // A template whose placeholders do not match the call site must never crash the UI.
            MarkMissing(key);
            return string.Format(CultureInfo.InvariantCulture, MissingFormat, key);
        }
    }

    public string Resolve(LocalizedText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Format(text.Key, text.Arguments.ToArray());
    }

    public string Resolve(LocalizedText? text, string fallbackKey, params object?[] arguments)
    {
        if (text is null)
        {
            return Format(fallbackKey, arguments);
        }

        if (!TryGetTemplate(text.Key, out _))
        {
            MarkMissing(text.Key);
            return Format(fallbackKey, arguments);
        }

        return Format(text.Key, text.Arguments.ToArray());
    }

    public bool HasKey(string key) => TryGetTemplate(key, out _);

    public void SetLanguage(LanguagePreference preference)
    {
        if (_preference == preference)
        {
            return;
        }

        _preference = preference;
        _culture = ResolveCulture(preference);

        if (_settings is not null && _settings.Current.Language != preference)
        {
            // Persisting is best effort: a read only settings file must not prevent switching.
            _ = TryPersistAsync(preference);
        }

        LanguageChanged?.Invoke(this, _culture);
    }

    public CultureInfo ResolveCulture(LanguagePreference preference) => preference switch
    {
        LanguagePreference.German => new CultureInfo("de-DE"),
        LanguagePreference.English => new CultureInfo("en-US"),
        _ => ResolveSystemCulture(),
    };

    private static CultureInfo ResolveSystemCulture()
    {
        var ui = CultureInfo.CurrentUICulture;
        var twoLetter = ui.TwoLetterISOLanguageName;

        // Only the two shipped languages are honoured. Anything else falls back to English
        // instead of showing a half translated interface.
        return twoLetter switch
        {
            "de" => new CultureInfo("de-DE"),
            "en" => new CultureInfo("en-US"),
            _ => new CultureInfo("en-US"),
        };
    }

    private async Task TryPersistAsync(LanguagePreference preference)
    {
        try
        {
            if (_settings is null)
            {
                return;
            }

            await _settings.UpdateAsync(current => current with { Language = preference }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Deliberately swallowed: failing to persist must not break the running session.
        }
    }

    private bool TryGetTemplate(string key, out string template)
    {
        template = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var language = _culture.TwoLetterISOLanguageName;
        lock (_gate)
        {
            if (_catalogs.TryGetValue(language, out var catalog) && catalog.TryGetValue(key, out var value))
            {
                template = value;
                return true;
            }

            // Last resort: the English string, clearly marked as fallback in the diagnostics list.
            if (!string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
                && _catalogs.TryGetValue("en", out var english)
                && english.TryGetValue(key, out var fallback))
            {
                MarkMissing(key);
                template = fallback;
                return true;
            }
        }

        return false;
    }

    private void MarkMissing(string key)
    {
        lock (_gate)
        {
            _missing.Add(key);
        }
    }

    private static Dictionary<string, string> LoadEmbedded(string language)
    {
        var assembly = typeof(LocalizedText).Assembly;
        var resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith($".Resources.{language}.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded resource for language '{language}' is missing. The build is incomplete.");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' could not be opened.");
        return Parse(stream, resourceName);
    }

    private void ApplyOverrides(string language, Dictionary<string, string> target)
    {
        if (string.IsNullOrWhiteSpace(_overrideDirectory))
        {
            return;
        }

        var path = Path.Combine(_overrideDirectory!, OverrideSubdirectory, $"{language}.json");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            foreach (var pair in Parse(stream, path))
            {
                target[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken override file is ignored; the embedded default stays in place.
        }
    }

    private static Dictionary<string, string> Parse(Stream stream, string origin)
    {
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("strings", out var strings) || strings.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"'{origin}' has no 'strings' object.");
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in strings.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"'{origin}': value of '{property.Name}' is not a string.");
            }

            result[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return result;
    }
}
