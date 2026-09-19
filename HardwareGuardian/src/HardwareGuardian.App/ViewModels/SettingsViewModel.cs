using System.Collections.ObjectModel;
using System.Globalization;
using HardwareGuardian.App.Mvvm;
using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;

namespace HardwareGuardian.App.ViewModels;

/// <summary>
/// Settings page (spec sections 39 to 42): language, theme, offline behaviour, privacy switches and
/// the locations of the produced files. Telemetry is off by default and can only be switched on
/// deliberately - it stays off when the checkbox was never touched.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IPathProvider _paths;
    private readonly IEnvironmentProbe _environment;
    private readonly IBuildInfoProvider _buildInfo;
    private readonly IAuditLog _audit;
    private readonly App.Services.ThemeManager _themes;

    private AppSettings _current;
    private string _statusText = string.Empty;

    public SettingsViewModel(
        ILocalizer localizer,
        ISettingsService settings,
        IPathProvider paths,
        IEnvironmentProbe environment,
        IBuildInfoProvider buildInfo,
        IAuditLog audit,
        App.Services.ThemeManager themes)
        : base(localizer)
    {
        _settings = settings;
        _paths = paths;
        _environment = environment;
        _buildInfo = buildInfo;
        _audit = audit;
        _themes = themes;
        _current = settings.Current;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanInteract);
        ReloadAuditCommand = new RelayCommand(ReloadAudit);
        ReloadAudit();
    }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand ReloadAuditCommand { get; }

    public ObservableCollection<AuditEntry> RecentAudit { get; } = new();

    public IReadOnlyList<LanguagePreference> Languages { get; } =
        new[] { LanguagePreference.System, LanguagePreference.German, LanguagePreference.English };

    public IReadOnlyList<ThemePreference> Themes { get; } =
        new[] { ThemePreference.System, ThemePreference.Dark, ThemePreference.Light };

    public LanguagePreference SelectedLanguage
    {
        get => _current.Language;
        set
        {
            if (_current.Language == value)
            {
                return;
            }

            _current = _current with { Language = value };
            OnPropertyChanged();
            Localizer.SetLanguage(value);
            OnPropertyChanged(nameof(LanguageHint));
        }
    }

    public ThemePreference SelectedTheme
    {
        get => _current.Theme;
        set
        {
            if (_current.Theme == value)
            {
                return;
            }

            _current = _current with { Theme = value };
            OnPropertyChanged();
            _themes.Apply(value);
        }
    }

    /// <summary>Off when the switch is off: then no source is contacted, which is the offline mode.</summary>
    public bool UpdateCheckEnabled
    {
        get => _current.UpdateCheckEnabled;
        set
        {
            if (_current.UpdateCheckEnabled == value)
            {
                return;
            }

            _current = _current with { UpdateCheckEnabled = value };
            OnPropertyChanged();
            OnPropertyChanged(nameof(OfflineHint));
        }
    }

    public bool UpdateCheckOnStartup
    {
        get => _current.UpdateCheckOnStartup;
        set
        {
            if (_current.UpdateCheckOnStartup == value)
            {
                return;
            }

            _current = _current with { UpdateCheckOnStartup = value };
            OnPropertyChanged();
        }
    }

    public bool UseVendorTools
    {
        get => _current.UseVendorTools;
        set
        {
            if (_current.UseVendorTools == value)
            {
                return;
            }

            _current = _current with { UseVendorTools = value };
            OnPropertyChanged();
        }
    }

    public bool ManufacturerSourcesEnabled
    {
        get => _current.ManufacturerSourcesEnabled;
        set
        {
            if (_current.ManufacturerSourcesEnabled == value)
            {
                return;
            }

            _current = _current with { ManufacturerSourcesEnabled = value };
            OnPropertyChanged();
        }
    }

    public bool MaskSerialNumbers
    {
        get => _current.MaskSerialNumbersInReports;
        set
        {
            if (_current.MaskSerialNumbersInReports == value)
            {
                return;
            }

            _current = _current with { MaskSerialNumbersInReports = value };
            OnPropertyChanged();
        }
    }

    public bool MaskUserName
    {
        get => _current.MaskUserNameInReports;
        set
        {
            if (_current.MaskUserNameInReports == value)
            {
                return;
            }

            _current = _current with { MaskUserNameInReports = value };
            OnPropertyChanged();
        }
    }

    public bool IncludeEvidence
    {
        get => _current.IncludeEvidenceInReports;
        set
        {
            if (_current.IncludeEvidenceInReports == value)
            {
                return;
            }

            _current = _current with { IncludeEvidenceInReports = value };
            OnPropertyChanged();
        }
    }

    /// <summary>Always false unless the store was changed outside the app; shown, not switchable here.</summary>
    public bool TelemetryEnabled => _current.TelemetryEnabled;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LanguageHint => L("Settings_LanguageHint");

    public string OfflineHint => L("Settings_OfflineHint", L("Report_Source"));

    public string VersionLine =>
        $"{_buildInfo.Get().Version} · {_buildInfo.Get().Commit} · {_buildInfo.Get().BuildDate} · {_buildInfo.Get().TargetFramework}";

    public string EnvironmentLine =>
        $"{_environment.OsDescription} · {_environment.OsArchitecture} · {_environment.ProcessArchitecture}";

    public string DataRoot => _paths.DataRoot;

    public string LogDirectory => _paths.LogDirectory;

    public string ReportDirectory => string.IsNullOrWhiteSpace(_current.ReportDirectory) ? _paths.ReportDirectory : _current.ReportDirectory;

    public string BackupDirectory => _paths.BackupDirectory;

    public string AuditLocation => _audit.Location;

    public string SimulationNotice => L("Report_SimulationWarning");

    public bool IsSimulation => App.IsSimulationRequested;

    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            var saved = await _settings.UpdateAsync(current => _current with
            {
                SchemaVersion = current.SchemaVersion,
                LastPage = current.LastPage,
                LastRunWasSimulation = App.IsSimulationRequested,
                LastFullScanUtc = current.LastFullScanUtc,
                LastUpdateCheckUtc = current.LastUpdateCheckUtc,
            }, CancellationToken.None).ConfigureAwait(true);

            _current = saved;
            StatusText = L("Settings_Saved");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            SaveCommand.RaiseCanExecuteChanged();
        }
    }

    private void ReloadAudit()
    {
        RecentAudit.Clear();
        foreach (var entry in _audit.Recent(50))
        {
            RecentAudit.Add(entry);
        }
    }

    protected override void OnLanguageChangedCore() => ReloadAudit();

    protected override void DisposeCore() => RecentAudit.Clear();
}
