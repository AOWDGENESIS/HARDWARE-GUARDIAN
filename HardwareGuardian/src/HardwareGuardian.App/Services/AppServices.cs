using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.App.Services;

/// <summary>Marshals callbacks onto the WPF dispatcher thread (spec section 41).</summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher? dispatcher = null) =>
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    public bool IsOnUiThread => _dispatcher.CheckAccess();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsOnUiThread)
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsOnUiThread)
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }
}

/// <summary>
/// In-process notification service. Notifications are never sent anywhere: they are shown in the
/// status area of the window and kept in a bounded history so that the operator can look them up.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private const int MaxHistory = 200;
    private readonly List<NotificationMessage> _history = new();
    private readonly object _gate = new();
    private readonly IClock _clock;
    private int _counter;

    public NotificationService(IClock clock) => _clock = clock;

    public event EventHandler<NotificationMessage>? NotificationRaised;

    public IReadOnlyList<NotificationMessage> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToList();
            }
        }
    }

    public void Notify(NotificationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var entry = message.RaisedAt == default
            ? message with { RaisedAt = _clock.Now, Id = string.IsNullOrWhiteSpace(message.Id) ? NextId() : message.Id }
            : message;

        lock (_gate)
        {
            _history.Add(entry);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(0, _history.Count - MaxHistory);
            }
        }

        NotificationRaised?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _history.Clear();
        }
    }

    private string NextId() => $"N-{_clock.Now:yyyyMMddHHmmss}-{Interlocked.Increment(ref _counter):D3}";
}

/// <summary>
/// Applies Dark, Light or the system theme at runtime by swapping the theme resource dictionary.
/// The resource dictionaries are the single source for colours; no view hard codes a colour.
/// </summary>
public sealed class ThemeManager
{
    private const string ThemePrefix = "Themes/";
    private readonly Application _application;

    public ThemeManager(Application application) => _application = application;

    public ThemePreference Current { get; private set; } = ThemePreference.Dark;

    public string ActiveKey { get; private set; } = "Dark";

    public void Apply(ThemePreference preference)
    {
        Current = preference;
        ActiveKey = preference switch
        {
            ThemePreference.Light => "Light",
            ThemePreference.System => IsSystemDark() ? "Dark" : "Light",
            _ => "Dark",
        };

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"{ThemePrefix}{ActiveKey}.xaml", UriKind.Relative),
        };

        var merged = _application.Resources.MergedDictionaries;
        var existing = merged.FirstOrDefault(d => d.Source is not null && d.Source.OriginalString.StartsWith(ThemePrefix, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var index = merged.IndexOf(existing);
            merged[index] = dictionary;
        }
        else
        {
            merged.Add(dictionary);
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Without a readable preference the default (Dark) stays active; no guessing about "system".
            return true;
        }
    }
}

/// <summary>
/// Binding source for localised strings. Views bind to <c>[Key]</c> through the indexer, and a
/// language change raises <c>Item[]</c> so every visible text updates at once.
/// </summary>
public sealed class LocalizationProxy : System.ComponentModel.INotifyPropertyChanged
{
    private ILocalizer? _localizer;

    private LocalizationProxy()
    {
    }

    public static LocalizationProxy Instance { get; } = new();

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => _localizer is null ? $"[[{key}]]" : _localizer[key];

    public void Attach(ILocalizer localizer)
    {
        if (_localizer is not null)
        {
            _localizer.LanguageChanged -= OnLanguageChanged;
        }

        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _localizer.LanguageChanged += OnLanguageChanged;
        RaiseAll();
    }

    public void RaiseAll() => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("Item[]"));

    private void OnLanguageChanged(object? sender, CultureInfo culture) => RaiseAll();
}

/// <summary>Markup extension for XAML: <c>Text="{loc:Loc Dashboard_Title}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]")
        {
            Source = LocalizationProxy.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = $"[[{Key}]]",
        }.ProvideValue(serviceProvider);
}
