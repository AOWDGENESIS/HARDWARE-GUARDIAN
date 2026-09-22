using System.Collections.ObjectModel;
using System.Globalization;
using WindowsMaintenanceCenter.App.Mvvm;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Events;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.App.ViewModels;

/// <summary>
/// The window view model: navigation, the live protocol, progress and the state machine display.
/// It owns no analysis logic - that lives in the services.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly IScanOrchestrator _orchestrator;
    private readonly ILiveProtocol _protocol;
    private readonly IProgressReporter _progress;
    private readonly IProblemRegistry _problems;
    private readonly ISystemStateMachine _state;
    private readonly IRecoveryEngine _recovery;
    private readonly INotificationService _notifications;
    private readonly ISettingsService _settings;
    private readonly IEnvironmentProbe _environment;
    private readonly IBuildInfoProvider _buildInfo;
    private readonly IUiDispatcher _dispatcher;

    private CancellationTokenSource? _scanCancellation;
    private ViewModelBase? _currentPage;
    private string _statusText = string.Empty;
    private string _stepText = string.Empty;
    private double _progressPercent;
    private bool _isScanning;
    private string? _lastError;
    private int _selectedPageIndex;
    private bool _isProtocolVisible;

    public MainViewModel(
        ILocalizer localizer,
        IScanOrchestrator orchestrator,
        ILiveProtocol protocol,
        IProgressReporter progress,
        IProblemRegistry problems,
        ISystemStateMachine state,
        IRecoveryEngine recovery,
        INotificationService notifications,
        ISettingsService settings,
        IEnvironmentProbe environment,
        IBuildInfoProvider buildInfo,
        IUiDispatcher dispatcher,
        DashboardViewModel dashboard,
        HardwareViewModel hardware,
        WindowsHealthViewModel windows,
        MaintenanceViewModel maintenance,
        OneClickViewModel oneClick,
        RecoveryViewModel recoveryViewModel,
        SettingsViewModel settingsViewModel)
        : base(localizer)
    {
        _orchestrator = orchestrator;
        _protocol = protocol;
        _progress = progress;
        _problems = problems;
        _state = state;
        _recovery = recovery;
        _notifications = notifications;
        _settings = settings;
        _environment = environment;
        _buildInfo = buildInfo;
        _dispatcher = dispatcher;

        Dashboard = dashboard;
        Hardware = hardware;
        WindowsHealth = windows;
        Maintenance = maintenance;
        OneClick = oneClick;
        Recovery = recoveryViewModel;
        SettingsPage = settingsViewModel;

        ScanCommand = new AsyncRelayCommand(RunFullScanAsync, () => !IsScanning);
        CancelCommand = new RelayCommand(CancelScan, () => IsScanning);
        ToggleProtocolCommand = new RelayCommand(() => IsProtocolVisible = !IsProtocolVisible);
        ClearProtocolCommand = new RelayCommand(() =>
        {
            _protocol.Clear();
            ProtocolEntries.Reset(Array.Empty<ProtocolEntry>());
        });

        _protocol.EntryAdded += OnProtocolEntry;
        _progress.ProgressChanged += OnProgressChanged;
        _state.StateChanged += OnStateChanged;
        _problems.ProblemRegistered += OnProblemRegistered;
        _notifications.NotificationRaised += OnNotification;

        Navigation.Add(new NavigationEntry("Navigation_Dashboard", Dashboard));
        Navigation.Add(new NavigationEntry("Navigation_Hardware", Hardware));
        Navigation.Add(new NavigationEntry("Navigation_Windows", WindowsHealth));
        Navigation.Add(new NavigationEntry("Navigation_Maintenance", Maintenance));
        Navigation.Add(new NavigationEntry("Navigation_OneClick", OneClick));
        Navigation.Add(new NavigationEntry("Navigation_Recovery", Recovery));
        Navigation.Add(new NavigationEntry("Navigation_Settings", SettingsPage));

        _isProtocolVisible = _settings.Current.LiveProtocolVisible;
        // The dashboard owns the scan button, but exactly one place may start a scan.
        Dashboard.ScanRequested += (_, _) => ScanCommand.Execute(null);

        CurrentPage = Dashboard;
        StatusText = L("Progress_Idle");
    }

    public DashboardViewModel Dashboard { get; }

    public HardwareViewModel Hardware { get; }

    public WindowsHealthViewModel WindowsHealth { get; }

    public MaintenanceViewModel Maintenance { get; }

    /// <summary>One-click maintenance (module M27): the guided run through all eight phases.</summary>
    public OneClickViewModel OneClick { get; }

    /// <summary>Recovery after an interrupted run (module M35).</summary>
    public RecoveryViewModel Recovery { get; }

    public SettingsViewModel SettingsPage { get; }

    public BulkObservableCollection<ProtocolEntry> ProtocolEntries { get; } = new();

    public List<NavigationEntry> Navigation { get; } = new();

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ClearProtocolCommand { get; }

    public RelayCommand ToggleProtocolCommand { get; }

    /// <summary>The live protocol panel can be hidden; the setting is kept in the settings file.</summary>
    public bool IsProtocolVisible
    {
        get => _isProtocolVisible;
        set => SetProperty(ref _isProtocolVisible, value);
    }

    public ViewModelBase? CurrentPage
    {
        get => _currentPage;
        set => SetProperty(ref _currentPage, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string StepText
    {
        get => _stepText;
        private set => SetProperty(ref _stepText, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                ScanCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanInteract));
            }
        }
    }

    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    /// <summary>Index in the navigation list. Selecting an entry switches the visible page.</summary>
    public int SelectedPageIndex
    {
        get => _selectedPageIndex;
        set
        {
            if (SetProperty(ref _selectedPageIndex, value) && value >= 0 && value < Navigation.Count)
            {
                CurrentPage = Navigation[value].Page;
            }
        }
    }

    public string ApplicationLine =>
        $"{_buildInfo.Get().ProductName} {_buildInfo.Get().Version} ({_buildInfo.Get().TargetFramework}, {_buildInfo.Get().Configuration})";

    public string EnvironmentLine =>
        $"{_environment.OsDescription} · {_environment.OsArchitecture} · {L(_environment.IsElevated ? "Build_Privilege_Administrator" : "Build_Privilege_StandardUser")}";

    public bool IsSimulation => App.IsSimulationRequested;

    /// <summary>Banner text shown in the window header while the simulation fixture is active.</summary>
    public string SimulationNotice => L("Report_SimulationWarning");

    /// <summary>True when update checks are switched off in the settings (offline working mode).</summary>
    public bool IsOffline => !_settings.Current.UpdateCheckEnabled;

    public string StateText => _state.Current.ToString();

    public string ProblemSummary => L(
        "Report_Section_Problems") + ": " + _problems.Counts.Total.ToString(CultureInfo.CurrentCulture);

    public async Task InitialiseAsync()
    {
        _protocol.Info("SYS", LocalizedText.Of("Protocol_ScanStarted", _orchestrator.Modules.Count));

        // Chapter 41 (M35): a run that was cut off has to be recognisable at the next start, and the
        // application has to say RECOVERY AVAILABLE instead of starting as if nothing had happened.
        // Reading the journal changes nothing; the recovery itself needs an approval of its own
        // (M35-S-001), so this only reports what it found.
        try
        {
            var assessment = await _recovery.AssessAsync(CancellationToken.None).ConfigureAwait(true);

            // SEC-12: a state journal that was changed after the fact is reported right away. The run
            // continues, but it is not presented as healthy (chapter 96 - a finding is not a detail).
            if (!assessment.JournalIntact && assessment.JournalFinding is not null)
            {
                _protocol.Publish(
                    "JRN",
                    assessment.JournalFinding,
                    Severity.Error,
                    string.Join(" | ", assessment.JournalEvidence));
            }

            if (assessment.RecoveryAvailable)
            {
                _protocol.Publish(
                    "REC",
                    LocalizedText.Of("Recovery_Available"),
                    Severity.Warning,
                    string.Join(" | ", assessment.Evidence));
            }

            // The recovery page is filled before it is opened: an interrupted operation is a finding
            // that has to stand in the problem centre from the start, not only after somebody happens
            // to click on the page (chapter 96).
            await Recovery.RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
        }

        // Startup inventory: local only, no network, no analysis modules.
        try
        {
            await _orchestrator.ReadInventoryAsync(CancellationToken.None).ConfigureAwait(true);
            StatusText = L("Overall_Unknown");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
        }

        ProtocolEntries.Reset(_protocol.Snapshot().TakeLast(200));
    }

    private async Task RunFullScanAsync()
    {
        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        LastError = null;
        StatusText = L("Progress_FullScan");

        try
        {
            var snapshot = await _orchestrator.RunFullScanAsync(_scanCancellation.Token).ConfigureAwait(true);
            Dashboard.Load(snapshot);
            Hardware.Load(snapshot);
            StatusText = L(snapshot.OverallSummary);
        }
        catch (OperationCanceledException)
        {
            StatusText = L("Severity_Warning");
        }
        catch (OperationBlockedException blocked)
        {
            // Fail closed: the reason is shown instead of a partial result being presented as complete.
            LastError = $"{blocked.ReasonCode}: {L(blocked.Reason)}";
            StatusText = L("Blocked_Unknown");
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            StatusText = L("Health_Unknown");
        }
        finally
        {
            IsScanning = false;
            ProgressPercent = 0d;
            StepText = string.Empty;
            _scanCancellation?.Dispose();
            _scanCancellation = null;
        }
    }

    private void CancelScan() => _scanCancellation?.Cancel();

    private void OnProtocolEntry(object? sender, ProtocolEntry entry) =>
        _dispatcher.Post(() =>
        {
            ProtocolEntries.Add(entry);
            while (ProtocolEntries.Count > 500)
            {
                ProtocolEntries.RemoveAt(0);
            }
        });

    private void OnProgressChanged(object? sender, ProgressSnapshot snapshot) =>
        _dispatcher.Post(() =>
        {
            ProgressPercent = snapshot.PercentComplete ?? 0d;
            StepText = snapshot.StepKey is null ? string.Empty : L(snapshot.StepKey);
            if (!string.IsNullOrWhiteSpace(snapshot.OperationKey) && snapshot.IsRunning)
            {
                StatusText = L(snapshot.OperationKey);
            }
        });

    private void OnStateChanged(object? sender, StateChangedEvent e) =>
        _dispatcher.Post(() => OnPropertyChanged(nameof(StateText)));

    private void OnProblemRegistered(object? sender, Problem problem) =>
        _dispatcher.Post(() => OnPropertyChanged(nameof(ProblemSummary)));

    private void OnNotification(object? sender, NotificationMessage message) =>
        _dispatcher.Post(() => StatusText = L(message.Title));

    protected override void DisposeCore()
    {
        _protocol.EntryAdded -= OnProtocolEntry;
        _progress.ProgressChanged -= OnProgressChanged;
        _state.StateChanged -= OnStateChanged;
        _problems.ProblemRegistered -= OnProblemRegistered;
        _notifications.NotificationRaised -= OnNotification;
        _scanCancellation?.Dispose();
        Dashboard.Dispose();
        Hardware.Dispose();
        WindowsHealth.Dispose();
        Maintenance.Dispose();
        SettingsPage.Dispose();
    }
}

/// <summary>One entry of the navigation list.</summary>
public sealed record NavigationEntry(string TitleKey, ViewModelBase Page);
