using System.Collections.ObjectModel;
using System.Globalization;
using HardwareGuardian.App.Mvvm;
using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.App.ViewModels;

/// <summary>
/// Maintenance page (spec sections 44 to 46).
///
/// Order of operations, enforced here and not only in the service: scan, select, dry run, approval,
/// execute. The execute button stays disabled until a dry run for the same selection has completed,
/// and an approval is requested from the user before anything is deleted.
/// </summary>
public sealed class MaintenanceViewModel : ViewModelBase
{
    private readonly IMaintenanceService _service;
    private readonly IApprovalService _approvals;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;
    private readonly INotificationService _notifications;

    private MaintenanceScanResult? _scan;
    private MaintenancePlan? _plan;
    private ApprovalRequest? _pendingApproval;
    private string _statusText = string.Empty;
    private string? _message;
    private bool _dryRunCompleted;
    private bool _isAwaitingApproval;

    public MaintenanceViewModel(
        ILocalizer localizer,
        IMaintenanceService service,
        IApprovalService approvals,
        ISettingsService settings,
        IClock clock,
        INotificationService notifications)
        : base(localizer)
    {
        _service = service;
        _approvals = approvals;
        _settings = settings;
        _clock = clock;
        _notifications = notifications;

        ScanCommand = new AsyncRelayCommand(ScanAsync, () => CanInteract);
        DryRunCommand = new AsyncRelayCommand(DryRunAsync, () => CanInteract);
        ExecuteCommand = new AsyncRelayCommand(RequestExecutionAsync, () => CanInteract && _dryRunCompleted);
        ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => _isAwaitingApproval);
        RejectCommand = new RelayCommand(Reject, () => _isAwaitingApproval);
        SelectSafeCommand = new RelayCommand(() => SelectOnly(SafetyClass.Safe));

        _approvals.ApprovalDecided += OnApprovalDecided;
    }

    public BulkObservableCollection<MaintenanceItemRow> Items { get; } = new();

    public BulkObservableCollection<string> ResultLines { get; } = new();

    public AsyncRelayCommand ScanCommand { get; }

    public AsyncRelayCommand DryRunCommand { get; }

    public AsyncRelayCommand ExecuteCommand { get; }

    public AsyncRelayCommand ApproveCommand { get; }

    public RelayCommand RejectCommand { get; }

    public RelayCommand SelectSafeCommand { get; }

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

    public bool IsAwaitingApproval
    {
        get => _isAwaitingApproval;
        private set
        {
            if (SetProperty(ref _isAwaitingApproval, value))
            {
                ApproveCommand.RaiseCanExecuteChanged();
                RejectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool DryRunCompleted
    {
        get => _dryRunCompleted;
        private set
        {
            if (SetProperty(ref _dryRunCompleted, value))
            {
                ExecuteCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PlanSummary => _plan is null
        ? L("Maintenance_Plan_Empty")
        : string.Format(
            CultureInfo.CurrentCulture,
            "{0} · {1}: {2} · {3}: {4}",
            L(_plan.Summary),
            L("Maintenance_Risk"),
            _plan.Risk,
            L("Maintenance_Size"),
            _plan.TotalBytesToFree.Display(CultureInfo.CurrentCulture));

    public string ApprovalSummary => _pendingApproval is null
        ? string.Empty
        : $"{L(_pendingApproval.Draft.Action)}: {L(_pendingApproval.Draft.What)}";

    private async Task ScanAsync()
    {
        IsBusy = true;
        Message = null;
        ResultLines.Clear();
        DryRunCompleted = false;
        try
        {
            var options = new MaintenanceScanOptions
            {
                IncludeBrowserCache = _settings.Current.MaintenanceIncludeBrowserCache,
                IncludeWindowsUpdateCache = _settings.Current.MaintenanceIncludeWindowsUpdateCache,
                IncludePrefetch = _settings.Current.IncludePrefetchedData,
            };

            _scan = await _service.ScanAsync(options, CancellationToken.None).ConfigureAwait(true);

            Items.Reset(_scan.Items.Select(item => new MaintenanceItemRow(
                item,
                L(item.DisplayNameKey),
                item.SafetyClass.ToString(),
                L(item.Description),
                item.SizeBytes.HasValue ? $"{item.SizeBytes.Value!.Value / (1024d * 1024d):0.#} MB" : "UNKNOWN",
                item.FileCount.HasValue ? item.FileCount.Value!.Value.ToString(CultureInfo.CurrentCulture) : "UNKNOWN",
                item.RequiresAdministrator,
                item.IsEnabledByDefault)));

            StatusText = L("Maintenance_Scan_Completed", _scan.Items.Count);

            foreach (var problem in _scan.Problems)
            {
                ResultLines.Add($"{problem.Id}: {L(problem.Title)} — {problem.Evidence}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Message = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ScanCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task DryRunAsync()
    {
        if (_scan is null)
        {
            Message = L("Maintenance_Plan_Empty");
            return;
        }

        IsBusy = true;
        Message = null;
        ResultLines.Clear();
        try
        {
            var selection = SelectedCategories();
            _plan = await _service.BuildPlanAsync(_scan, selection, ExecutionMode.DryRun, CancellationToken.None).ConfigureAwait(true);
            var result = await _service.ExecuteDryRunAsync(_plan, CancellationToken.None).ConfigureAwait(true);

            ResultLines.Add(L(result.Summary));
            foreach (var item in result.Items)
            {
                ResultLines.Add($"{L(item.DisplayNameKey)}: {item.Outcome} — {L(item.Message, "Maintenance_Change_Unknown")}");
            }

            DryRunCompleted = true;
            StatusText = L("Maintenance_DryRun_Completed");
            OnPropertyChanged(nameof(PlanSummary));
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
            DryRunCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task RequestExecutionAsync()
    {
        if (_scan is null || !_dryRunCompleted)
        {
            Message = L("Maintenance_Plan_Empty");
            return;
        }

        IsBusy = true;
        Message = null;
        try
        {
            var selection = SelectedCategories();
            _plan = await _service.BuildPlanAsync(_scan, selection, ExecutionMode.Execute, CancellationToken.None).ConfigureAwait(true);

            var draft = new ApprovalRequestDraft
            {
                OperationId = _plan.PlanId,
                Operation = OperationKind.Maintenance,
                Category = ComponentCategory.Maintenance,
                Action = LocalizedText.Of("Maintenance_Execute_Completed", _plan.PlanId),
                What = _plan.Summary,
                Why = LocalizedText.Of("Maintenance_Reason_Evidence", $"{selection.Count} " + L("Maintenance_Plan_Summary")),
                Risk = _plan.Risk,
                RiskSummary = LocalizedText.Of("Maintenance_Reason_Evidence", L(_plan.Summary)),
                RequiresAdministrator = _plan.RequiresAdministrator,
                Steps = _plan.Items.Select(i => i.Change).ToList(),
                Preview = _plan.Items.Select(i => new ChangePreview
                {
                    LabelKey = i.DisplayNameKey,
                    OldValue = i.SizeBytes.Display(CultureInfo.CurrentCulture),
                    NewValue = L("Maintenance_Change_Delete", i.FileCount.Display(CultureInfo.CurrentCulture)),
                    Reason = i.Reason,
                    Risk = i.Risk,
                }).ToList(),
                Evidence = _plan.Items.Select(i => $"item={i.ItemId} root={i.RootPath ?? "n/a"} safety={i.SafetyClass}").ToList(),
            };

            _pendingApproval = await _approvals.CreateAsync(draft, CancellationToken.None).ConfigureAwait(true);
            IsAwaitingApproval = true;
            OnPropertyChanged(nameof(ApprovalSummary));
            StatusText = L("Maintenance_Blocked_Title", _plan.PlanId);
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
            ExecuteCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task ApproveAsync()
    {
        if (_pendingApproval is null || _plan is null)
        {
            return;
        }

        var requestId = _pendingApproval.RequestId;
        _approvals.Decide(requestId, ApprovalDecision.Approved, note: null);
        IsAwaitingApproval = false;

        IsBusy = true;
        try
        {
            var decided = await _approvals.WaitForDecisionAsync(requestId, CancellationToken.None).ConfigureAwait(true);
            var record = new ApprovalRecord
            {
                RequestId = decided.RequestId,
                OperationId = decided.Draft.OperationId,
                Decision = decided.Decision,
                DecidedAt = decided.DecidedAt ?? _clock.Now,
                Note = decided.Note,
                Risk = decided.Draft.Risk,
                WasRequired = true,
            };

            var result = await _service.ExecuteAsync(_plan, record, CancellationToken.None).ConfigureAwait(true);

            ResultLines.Clear();
            ResultLines.Add(L(result.Summary));
            foreach (var item in result.Items)
            {
                ResultLines.Add($"{L(item.DisplayNameKey)}: {item.Outcome} — {L(item.Message, "Maintenance_Change_Unknown")}");
            }

            StatusText = L(result.Summary);
            DryRunCompleted = false;
            _pendingApproval = null;
            OnPropertyChanged(nameof(ApprovalSummary));

            _notifications.Notify(new NotificationMessage
            {
                Kind = NotificationKind.MaintenanceCompleted,
                ModuleKey = "Module_Maintenance",
                Title = LocalizedText.Of("Progress_Maintenance"),
                Message = result.Summary,
                RaisedAt = _clock.Now,
            });
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
        }
    }

    private void Reject()
    {
        if (_pendingApproval is null)
        {
            return;
        }

        _approvals.Decide(_pendingApproval.RequestId, ApprovalDecision.Rejected, note: null);
        IsAwaitingApproval = false;
        _pendingApproval = null;
        OnPropertyChanged(nameof(ApprovalSummary));
        StatusText = L("Approval_Rejected");
    }

    private void SelectOnly(SafetyClass safetyClass)
    {
        foreach (var row in Items)
        {
            row.IsSelected = row.SafetyClass == safetyClass;
        }

        DryRunCompleted = false;
    }

    private IReadOnlyList<MaintenanceCategory> SelectedCategories() =>
        Items.Where(i => i.IsSelected).Select(i => i.Model.Category).Distinct().ToList();

    private void OnApprovalDecided(object? sender, ApprovalRequest request) =>
        OnPropertyChanged(nameof(ApprovalSummary));

    protected override void OnLanguageChangedCore()
    {
        OnPropertyChanged(nameof(PlanSummary));
        OnPropertyChanged(nameof(ApprovalSummary));
    }

    protected override void DisposeCore()
    {
        _approvals.ApprovalDecided -= OnApprovalDecided;
        Items.Clear();
        ResultLines.Clear();
    }

    /// <summary>One selectable maintenance item. Selection is the user's decision, never automatic.</summary>
    public sealed class MaintenanceItemRow : ObservableObject
    {
        private bool _isSelected;

        public MaintenanceItemRow(
            MaintenanceItem model,
            string name,
            string safetyClassText,
            string description,
            string sizeText,
            string fileCountText,
            bool requiresAdministrator,
            bool isEnabledByDefault)
        {
            Model = model;
            Name = name;
            SafetyClassText = safetyClassText;
            Description = description;
            SizeText = sizeText;
            FileCountText = fileCountText;
            RequiresAdministrator = requiresAdministrator;
            _isSelected = isEnabledByDefault && model.SafetyClass == SafetyClass.Safe;
        }

        public MaintenanceItem Model { get; }

        public string Name { get; }

        public string SafetyClassText { get; }

        public string Description { get; }

        public string SizeText { get; }

        public string FileCountText { get; }

        public bool RequiresAdministrator { get; }

        public SafetyClass SafetyClass => Model.SafetyClass;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
