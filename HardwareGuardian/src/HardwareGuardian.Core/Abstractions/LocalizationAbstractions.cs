using System.Globalization;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Abstractions;

/// <summary>
/// Localisation service (spec section 41). No user visible text exists in code: everything is
/// addressed through a key, so language switching also updates log lines and reports.
/// </summary>
public interface ILocalizer
{
    event EventHandler<CultureInfo>? LanguageChanged;

    CultureInfo Culture { get; }

    LanguagePreference Preference { get; }

    /// <summary>Returns the string or a visible marker when the key is missing (never an empty string).</summary>
    string this[string key] { get; }

    string Format(string key, params object?[] arguments);

    string Resolve(LocalizedText text);

    string Resolve(LocalizedText? text, string fallbackKey, params object?[] arguments);

    bool HasKey(string key);

    IReadOnlyCollection<string> Keys { get; }

    void SetLanguage(LanguagePreference preference);

    /// <summary>Resolves the effective culture for a preference without changing state.</summary>
    CultureInfo ResolveCulture(LanguagePreference preference);
}

/// <summary>Marshals callbacks onto the UI thread. The implementation lives in the WPF layer.</summary>
public interface IUiDispatcher
{
    bool IsOnUiThread { get; }

    void Post(Action action);

    void Invoke(Action action);
}
