using System.Collections.ObjectModel;
using HardwareGuardian.App.Mvvm;
using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.App.ViewModels;

/// <summary>
/// Windows health page (spec sections 30 to 33). Every integrity run needs an explicit click, the
/// repair is only offered with administrator rights and the result is shown as the tool reported it.
/// </summary>
public sealed class WindowsHealthViewModel : ViewModelBase
{
    private readonly IWindowsHealthService _service;
    private readonly IProgressReporter _progress;
    private readonly ISettingsService _settings;
    private readonly IEnvironmentProbe _environment;

    private WindowsHealthReport? _report;
    private string _statusText = string.Empty;
    private string? _message;
    private bool _hasResult;

    public WindowsHealthViewModel(
        ILocalizer localizer,
        IWindowsHealthService service,
        IProgressReporter progress,
        ISettingsService settings,
        IEnvironmentProbe environment)
        : base(localizer)
    {
        _service = service;
        _progress = progress;
        _settings = settings;
        _environment = environment;

        AssessCommand = new AsyncRelayCommand(() => RunAsync(() => AssessAsync(includeOnline: true)), () => CanInteract);
        AssessOfflineCommand = new AsyncRelayCommand(() => RunAsync(() => AssessAsync(includeOnline: false)), () => CanInteract);
        DismScanCommand = new AsyncRelayCommand(() => RunAsync(() => IntegrityAsync(repair: false)), () => CanInteract);
        DismRepairCommand = new AsyncRelayCommand(() => RunAsync(() => IntegrityAsync(repair: true)), () => CanInteract && _environment.IsElevated);
        SfcCommand = new AsyncRelayCommand(() => RunAsync(() => IntegrityAsync(repair: false, systemFiles: true)), () => CanInteract);
    }

    public BulkObservableCollection<CheckRow> Checks { get; } = new();

    public AsyncRelayCommand AssessCommand { get; }

    public AsyncRelayCommand AssessOfflineCommand { get; }

    public AsyncRelayCommand DismScanCommand { get; }

    public AsyncRelayCommand DismRepairCommand { get; }

    public AsyncRelayCommand SfcCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

    public HealthStatus OverallStatus => _report?.Status ?? HealthStatus.Unknown;

    public string PendingRebootText => _report?.PendingRebootReason is null
        ? L("Windows_Check_NotRun")
        : _report.PendingRebootReason;

    public string DefenderText => _report?.Defender is { } defender
        ? L(defender.Summary) + (defender.ErrorDetail is null ? string.Empty : " — " + defender.ErrorDetail)
        : L("Defender_Unknown");

    public string UpdateText => _report?.Updates is { } updates
        ? L(updates.Summary)
        : L("WindowsUpdate_NotChecked");

    public string ElevationHint => L(_environment.IsElevated ? "Build_Privilege_Administrator" : "Build_Privilege_StandardUser");

    private async Task AssessAsync(bool includeOnline)
    {
        // Online checks run only when the user asked for them and the settings allow network access.
        var online = includeOnline && _settings.Current.UpdateCheckEnabled;
        var report = await _service.AssessAsync(null, online, _progress, CancellationToken.None).ConfigureAwait(true);
        Apply(report);
    }

    private async Task IntegrityAsync(bool repair, bool systemFiles = false)
    {
        var result = systemFiles
            ? await _service.RunSystemFileCheckAsync(false, _progress, CancellationToken.None).ConfigureAwait(true)
            : await _service.RunComponentStoreCheckAsync(repair, _progress, CancellationToken.None).ConfigureAwait(true);

        StatusText = L(result.Summary);
        Message = string.Join(" · ", result.Evidence.Take(3));
        HasResult = true;
        OnPropertyChanged(nameof(OverallStatus));
    }

    private void Apply(WindowsHealthReport report)
    {
        _report = report;
        StatusText = L(report.Summary);
        Message = report.PendingRebootReason;

        Checks.Reset(report.Checks.Select(check => new CheckRow(
            L(check.DisplayNameKey),
            check.Status,
            check.Status.ToString(),
            check.Summary,
            check.Detail ?? string.Empty,
            check.Performed,
            check.RequiresAdministrator,
            string.Join("; ", check.Evidence))));

        HasResult = true;
        OnPropertyChanged(nameof(OverallStatus));
        OnPropertyChanged(nameof(PendingRebootText));
        OnPropertyChanged(nameof(DefenderText));
        OnPropertyChanged(nameof(UpdateText));
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        Message = null;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (OperationBlockedException blocked)
        {
            Message = $"{blocked.ReasonCode}: {L(blocked.Reason)}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Message = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            AssessCommand.RaiseCanExecuteChanged();
            AssessOfflineCommand.RaiseCanExecuteChanged();
            DismScanCommand.RaiseCanExecuteChanged();
            DismRepairCommand.RaiseCanExecuteChanged();
            SfcCommand.RaiseCanExecuteChanged();
        }
    }

    protected override void OnLanguageChangedCore()
    {
        if (_report is not null)
        {
            Apply(_report);
        }
    }

    protected override void DisposeCore() => Checks.Clear();

    /// <summary>One Windows check with its evidence and whether it was really performed.</summary>
    public sealed record CheckRow(
        string Title,
        HealthStatus Status,
        string StatusText,
        LocalizedText Summary,
        string Detail,
        bool Performed,
        bool RequiresAdministrator,
        string Evidence);
}
