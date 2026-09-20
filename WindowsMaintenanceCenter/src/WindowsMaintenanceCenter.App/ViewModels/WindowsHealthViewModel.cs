using System.Globalization;
// System.Globalization has a TextInfo as well; the alias names the intended type.
using TextInfo = WindowsMaintenanceCenter.Core.Values.TextInfo;
using WindowsMaintenanceCenter.App.Mvvm;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.App.ViewModels;

/// <summary>
/// Windows health page (spec sections 30 to 33). Every integrity run needs an explicit click, the
/// read-only checks run without approval, and the component store repair is only started after the
/// user approved that exact operation - the approval record travels with the call and the service
/// refuses a repair without it. The result is shown as the tool reported it, never as assumed.
/// </summary>
public sealed class WindowsHealthViewModel : ViewModelBase
{
    private readonly IWindowsHealthService _service;
    private readonly IApprovalService _approvals;
    private readonly IProgressReporter _progress;
    private readonly ISettingsService _settings;
    private readonly IEnvironmentProbe _environment;

    private WindowsHealthReport? _report;
    private ApprovalRequest? _pendingRepair;
    private string _statusText = string.Empty;
    private string? _message;
    private bool _hasResult;
    private bool _isAwaitingRepairApproval;

    public WindowsHealthViewModel(
        ILocalizer localizer,
        IWindowsHealthService service,
        IApprovalService approvals,
        IProgressReporter progress,
        ISettingsService settings,
        IEnvironmentProbe environment)
        : base(localizer)
    {
        _service = service;
        _approvals = approvals;
        _progress = progress;
        _settings = settings;
        _environment = environment;

        AssessCommand = new AsyncRelayCommand(() => RunAsync(() => AssessAsync(includeOnline: true)), () => CanInteract);
        AssessOfflineCommand = new AsyncRelayCommand(() => RunAsync(() => AssessAsync(includeOnline: false)), () => CanInteract);
        DismScanCommand = new AsyncRelayCommand(() => RunAsync(() => IntegrityAsync()), () => CanInteract);
        DismRepairCommand = new AsyncRelayCommand(() => RunAsync(RequestRepairAsync), () => CanInteract && _environment.IsElevated);
        SfcVerifyCommand = new AsyncRelayCommand(() => RunAsync(() => IntegrityAsync(systemFiles: true)), () => CanInteract);
        ApproveRepairCommand = new AsyncRelayCommand(ApproveRepairAsync, () => _isAwaitingRepairApproval);
        RejectRepairCommand = new RelayCommand(RejectRepair, () => _isAwaitingRepairApproval);
    }

    public BulkObservableCollection<CheckRow> Checks { get; } = new();

    /// <summary>
    /// What the Windows update agent offers for this machine (rule 90, UPDATE-F-002/F-003). The list
    /// is display only: nothing is downloaded or installed from this page, and every field shows
    /// "not reported" when the agent did not report it.
    /// </summary>
    public BulkObservableCollection<UpdateRow> AvailableUpdates { get; } = new();

    public AsyncRelayCommand AssessCommand { get; }

    public AsyncRelayCommand AssessOfflineCommand { get; }

    public AsyncRelayCommand DismScanCommand { get; }

    public AsyncRelayCommand DismRepairCommand { get; }

    public AsyncRelayCommand SfcVerifyCommand { get; }

    public AsyncRelayCommand ApproveRepairCommand { get; }

    public RelayCommand RejectRepairCommand { get; }

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

    /// <summary>True while a repair waits for the decision of the user; the buttons appear then.</summary>
    public bool IsAwaitingRepairApproval
    {
        get => _isAwaitingRepairApproval;
        private set
        {
            if (SetProperty(ref _isAwaitingRepairApproval, value))
            {
                OnPropertyChanged(nameof(RepairApprovalSummary));
                ApproveRepairCommand.RaiseCanExecuteChanged();
                RejectRepairCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>What the user is asked to approve, in plain words, with the risk and the steps.</summary>
    public string RepairApprovalSummary => _pendingRepair is null
        ? string.Empty
        : $"{L(_pendingRepair.Draft.Action)}\n{L(_pendingRepair.Draft.What)}\n{L(_pendingRepair.Draft.RiskSummary)}";

    public HealthStatus OverallStatus => _report?.Status ?? HealthStatus.Unknown;

    public string PendingRebootText => _report?.PendingRebootReason is null
        ? L("Windows_Check_NotRun")
        : _report.PendingRebootReason;

    public string DefenderText => _report?.Defender is { } defender
        ? L(defender.Summary) + (defender.ErrorDetail is null ? string.Empty : " - " + defender.ErrorDetail)
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

    /// <summary>
    /// Read-only integrity run. The repair variant is not reachable from here: it goes through
    /// <see cref="RequestRepairAsync"/>, which asks first and passes the resulting record on.
    /// </summary>
    private async Task IntegrityAsync(ApprovalRecord? approval = null, bool systemFiles = false)
    {
        var result = systemFiles
            ? await _service.RunSystemFileCheckAsync(repair: false, approval: null, _progress, CancellationToken.None).ConfigureAwait(true)
            : await _service.RunComponentStoreCheckAsync(repair: approval is not null, approval: approval, _progress, CancellationToken.None).ConfigureAwait(true);

        StatusText = L(result.Summary);
        Message = string.Join(" | ", result.Evidence.Take(4));
        HasResult = true;
        OnPropertyChanged(nameof(OverallStatus));
    }

    /// <summary>Creates the approval request for the component store repair; nothing runs yet.</summary>
    private async Task RequestRepairAsync()
    {
        var draft = new ApprovalRequestDraft
        {
            Operation = OperationKind.Execute,
            Category = ComponentCategory.Windows,
            Action = LocalizedText.Of("Windows_Repair_Action"),
            What = LocalizedText.Of("Windows_Repair_What"),
            Why = LocalizedText.Of("Windows_Repair_Why"),
            Risk = RiskLevel.High,
            RiskSummary = LocalizedText.Of("Windows_Repair_Risk"),
            RequiresAdministrator = true,
            Steps = new[] { LocalizedText.Of("Windows_Repair_What") },
            Evidence = new[]
            {
                "tool=dism.exe /Online /Cleanup-Image /RestoreHealth",
                $"elevation={_environment.IsElevated}",
                $"windowsHealthStatus={_report?.Status.ToString() ?? "not assessed"}",
            },
        };

        _pendingRepair = await _approvals.CreateAsync(draft, CancellationToken.None).ConfigureAwait(true);
        IsAwaitingRepairApproval = true;
        StatusText = L("Windows_Repair_Pending");
    }

    private async Task ApproveRepairAsync()
    {
        if (_pendingRepair is null)
        {
            return;
        }

        var requestId = _pendingRepair.RequestId;
        _approvals.Decide(requestId, ApprovalDecision.Approved, note: null);
        IsAwaitingRepairApproval = false;

        var decided = await _approvals.WaitForDecisionAsync(requestId, CancellationToken.None).ConfigureAwait(true);
        var record = new ApprovalRecord
        {
            RequestId = decided.RequestId,
            OperationId = decided.Draft.OperationId,
            Decision = decided.Decision,
            DecidedAt = decided.DecidedAt ?? decided.CreatedAt,
            Note = decided.Note,
            Risk = decided.Draft.Risk,
            WasRequired = true,
        };

        _pendingRepair = null;
        Message = L("Windows_Repair_Completed");
        await IntegrityAsync(record, systemFiles: false).ConfigureAwait(true);
    }

    private void RejectRepair()
    {
        if (_pendingRepair is null)
        {
            return;
        }

        _approvals.Decide(_pendingRepair.RequestId, ApprovalDecision.Rejected, note: null);
        _pendingRepair = null;
        IsAwaitingRepairApproval = false;
        StatusText = L("Windows_Repair_Rejected");
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
            check.Performed ? L("Value_Yes") : L("Value_No"),
            check.RequiresAdministrator ? L("Value_Yes") : L("Value_No"),
            string.Join("; ", check.Evidence))));

        AvailableUpdates.Reset(report.Updates.Available.Select(update => new UpdateRow(
            Show(update.Caption, "Value_NotReported"),
            Show(update.KnowledgeBaseId, "Value_NotReported"),
            Show(update.Category, "Value_NotReported"),
            Show(update.Severity, "Value_NotReported"),
            update.RebootRequired is { } reboot ? L(reboot ? "Value_Yes" : "Value_No") : L("Value_NotReported"),
            SizeText.Format(update.DownloadSizeBytes, CultureInfo.CurrentCulture, L("Value_NotAvailable")))));

        HasResult = true;
        OnPropertyChanged(nameof(OverallStatus));
        OnPropertyChanged(nameof(PendingRebootText));
        OnPropertyChanged(nameof(DefenderText));
        OnPropertyChanged(nameof(UpdateText));
        OnPropertyChanged(nameof(NoAvailableUpdates));
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
            SfcVerifyCommand.RaiseCanExecuteChanged();
            ApproveRepairCommand.RaiseCanExecuteChanged();
            RejectRepairCommand.RaiseCanExecuteChanged();
        }
    }

    protected override void OnLanguageChangedCore()
    {
        if (_report is not null)
        {
            Apply(_report);
        }
    }

    protected override void DisposeCore()
    {
        Checks.Clear();
        AvailableUpdates.Clear();
    }

    /// <summary>
    /// True when the agent offered nothing. The hint is shown from the view model instead of
    /// inverting the flag in the view, so the view stays free of logic.
    /// </summary>
    public bool NoAvailableUpdates => AvailableUpdates.Count == 0;

    /// <summary>Shows a reported value or the caller's wording for "this was not reported".</summary>
    private string Show(TextInfo text, string notReportedKey) =>
        text.IsKnown ? text.Value! : L(notReportedKey);

    /// <summary>
    /// One Windows check with its evidence and whether it was really performed. The two flags are
    /// pre-rendered as localized words: a DataGrid would otherwise print "True" and "False" in a
    /// German user interface (rule 119).
    /// </summary>
    public sealed record CheckRow(
        string Title,
        HealthStatus Status,
        string StatusText,
        LocalizedText Summary,
        string Detail,
        string PerformedText,
        string AdminText,
        string Evidence);

    /// <summary>One update the agent offers, with every field as reported.</summary>
    public sealed record UpdateRow(
        string Title,
        string KnowledgeBase,
        string Category,
        string Severity,
        string Reboot,
        string Size);
}
