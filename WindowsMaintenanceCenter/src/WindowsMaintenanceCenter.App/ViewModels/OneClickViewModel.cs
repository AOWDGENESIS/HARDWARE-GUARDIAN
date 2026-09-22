using System.Globalization;
using WindowsMaintenanceCenter.App.Mvvm;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.App.ViewModels;

/// <summary>
/// One-click maintenance page (chapter 33, module M27).
///
/// The page exists for the same reason the recovery page exists: a service nobody can reach is not a
/// product. It is deliberately thin - the sequence lives in <see cref="IOneClickMaintenanceService"/>,
/// and the page only
///
/// * lets the user deselect categories before anything happens (M27-S-002),
/// * shows the plan with risk, safety class and size before it runs (M27-S-003),
/// * shows the phase list with what, why, risk and result (M27-S-001),
/// * asks the user for the approval the service waits for, using exactly the draft the service built,
/// * stops the run on request and reports it as cancelled - never as success (M27-R-001),
/// * shows the measured before/after values and every open point the run reported (M27-F-001).
/// </summary>
public sealed class OneClickViewModel : ViewModelBase
{
    private readonly IOneClickMaintenanceService _service;
    private readonly IMaintenanceService _maintenance;
    private readonly IApprovalService _approvals;
    private readonly INotificationService _notifications;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClock _clock;

    private CancellationTokenSource? _cancellation;
    private ApprovalRequest? _pendingApproval;
    private string _statusText = string.Empty;
    private string _message = string.Empty;
    private string _planSummary = string.Empty;
    private string _approvalSummary = string.Empty;
    private string _finalStateText = string.Empty;
    private string _reportPath = string.Empty;
    private string _openPointsText = string.Empty;
    private bool _isRunning;
    private bool _isAwaitingApproval;
    private bool _includeProtected;
    private bool _planOnly;
    private bool _hasResult;

    public OneClickViewModel(
        ILocalizer localizer,
        IOneClickMaintenanceService service,
        IMaintenanceService maintenance,
        IApprovalService approvals,
        INotificationService notifications,
        IUiDispatcher dispatcher,
        IClock clock)
        : base(localizer)
    {
        _service = service;
        _maintenance = maintenance;
        _approvals = approvals;
        _notifications = notifications;
        _dispatcher = dispatcher;
        _clock = clock;

        PlanCommand = new AsyncRelayCommand(PlanAsync, () => CanInteract);
        RunCommand = new AsyncRelayCommand(RunAsync, () => CanInteract);
        CancelCommand = new RelayCommand(Cancel, () => _isRunning);
        ApproveCommand = new RelayCommand(Approve, () => _isAwaitingApproval);
        RejectCommand = new RelayCommand(Reject, () => _isAwaitingApproval);
        SelectAllCommand = new RelayCommand(() => SelectAll(true));
        SelectNoneCommand = new RelayCommand(() => SelectAll(false));

        StatusText = L("OneClick_Status_Idle");
        LoadCategories();
        _approvals.ApprovalRequested += OnApprovalRequested;
    }

    /// <summary>Category rows: what the run may clean, how safe that is, and what the user chose.</summary>
    public BulkObservableCollection<OneClickCategoryRow> Categories { get; } = new();

    /// <summary>The phase list. It is the evidence for M27-S-001: what ran, why, with which result.</summary>
    public BulkObservableCollection<OneClickStepRow> Steps { get; } = new();

    /// <summary>Before/after measurements of the run, one line each.</summary>
    public BulkObservableCollection<string> Measurements { get; } = new();

    /// <summary>Plan lines with the item's own risk class.</summary>
    public BulkObservableCollection<string> PlanLines { get; } = new();

    public AsyncRelayCommand PlanCommand { get; }

    public AsyncRelayCommand RunCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ApproveCommand { get; }

    public RelayCommand RejectCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand SelectNoneCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string PlanSummary
    {
        get => _planSummary;
        private set => SetProperty(ref _planSummary, value);
    }

    public string ApprovalSummary
    {
        get => _approvalSummary;
        private set => SetProperty(ref _approvalSummary, value);
    }

    /// <summary>The verdict of the run: the state it ended in, never an optimistic phrase.</summary>
    public string FinalStateText
    {
        get => _finalStateText;
        private set => SetProperty(ref _finalStateText, value);
    }

    public string ReportPath
    {
        get => _reportPath;
        private set => SetProperty(ref _reportPath, value);
    }

    public string OpenPointsText
    {
        get => _openPointsText;
        private set => SetProperty(ref _openPointsText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanInteract));
                CancelCommand.RaiseCanExecuteChanged();
                PlanCommand.RaiseCanExecuteChanged();
                RunCommand.RaiseCanExecuteChanged();
            }
        }
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

    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

    /// <summary>Protected categories (for example the Windows Update cache) only run when asked for.</summary>
    public bool IncludeProtected
    {
        get => _includeProtected;
        set
        {
            if (SetProperty(ref _includeProtected, value))
            {
                LoadCategories();
            }
        }
    }

    /// <summary>Stops after the plan: nothing is changed, no approval is requested, no backup is made.</summary>
    public bool PlanOnly
    {
        get => _planOnly;
        set => SetProperty(ref _planOnly, value);
    }

    /// <summary>Ids of the selected categories, as the request wants them.</summary>
    private IReadOnlyCollection<MaintenanceCategory> Selection
    {
        get
        {
            var selected = Categories.Where(row => row.IsSelected).Select(row => row.Category).ToArray();
            return selected.Length == 0 || selected.Length == Categories.Count
                ? Categories.Select(row => row.Category).ToArray()
                : selected;
        }
    }

    private void LoadCategories()
    {
        // The list comes from the maintenance engine, so the page never invents a category.
        var descriptors = _maintenance.DescribeCategories();
        var rows = new List<OneClickCategoryRow>(descriptors.Count);

        foreach (var descriptor in descriptors)
        {
            if (descriptor.SafetyClass == SafetyClass.Protected && !IncludeProtected)
            {
                // Not offered at all: a checkbox that can never take effect would be a false promise.
                continue;
            }

            rows.Add(new OneClickCategoryRow
            {
                Category = descriptor.Category,
                Name = L(descriptor.DisplayNameKey),
                SafetyText = L("Safety_" + descriptor.SafetyClass),
                Description = L(descriptor.Description),
                RequiresAdministrator = descriptor.RequiresAdministrator,
                IsSelected = descriptor.SafetyClass is SafetyClass.Safe or SafetyClass.Optional,
            });
        }

        Categories.Reset(rows);
    }

    private void SelectAll(bool selected)
    {
        foreach (var row in Categories)
        {
            row.IsSelected = selected;
        }
    }

    private OneClickRequest Request() => new()
    {
        Selection = Selection,
        IncludeProtectedCategories = IncludeProtected,
        PlanOnly = PlanOnly,
        ReportFormat = ReportFormat.Html,
    };

    private async Task PlanAsync()
    {
        IsRunning = true;
        Message = string.Empty;
        try
        {
            var result = await _service.PlanAsync(Request(), CancellationToken.None).ConfigureAwait(true);
            Show(result, isPlan: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Message = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task RunAsync()
    {
        IsRunning = true;
        Message = string.Empty;
        Steps.Reset(Array.Empty<OneClickStepRow>());
        Measurements.Clear();
        PlanLines.Clear();
        OpenPointsText = string.Empty;
        ReportPath = string.Empty;
        FinalStateText = string.Empty;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        try
        {
            var request = Request();
            var result = await _service.RunAsync(request, cancellation.Token).ConfigureAwait(true);
            Show(result, isPlan: false);

            if (result.FinalState == SystemState.Success)
            {
                _notifications.Notify(new NotificationMessage
                {
                    Kind = NotificationKind.MaintenanceCompleted,
                    ModuleKey = "Module_OneClick",
                    Title = LocalizedText.Of("Navigation_OneClick"),
                    Message = result.Summary,
                    RaisedAt = _clock.Now,
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The service reports blocked and failed runs as results; reaching this means the page
            // itself failed, so it says so instead of showing a stale state.
            Message = $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            _cancellation = null;
            IsRunning = false;
            IsAwaitingApproval = false;
            _pendingApproval = null;
        }
    }

    private void Cancel()
    {
        // M27-R-001: an interruption creates a recovery state. The service turns the cancellation into
        // a cancelled result; the page does not decide anything behind its back.
        _cancellation?.Cancel();
        StatusText = L("OneClick_Status_Cancelling");
    }

    private void Show(OneClickResult result, bool isPlan)
    {
        Steps.Reset(result.Steps.Select(step => new OneClickStepRow
        {
            Phase = L("OneClick_Phase_" + step.Phase),
            What = L(step.What),
            Why = L(step.Why),
            Risk = step.Risk is null ? L("Value_NotAvailable") : L("OneClick_Risk_" + step.Risk),
            Outcome = L("OneClick_Outcome_" + step.Outcome),
            Detail = step.Detail ?? string.Empty,
            IsPhaseStep = step.IsPhaseStep,
        }));

        var planLines = result.Plan.Maintenance is null
            ? Array.Empty<string>()
            : result.Plan.Maintenance.Items
                .Select(item => string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} · {1} · {2}",
                    L(item.DisplayNameKey),
                    SizeText.Format(item.SizeBytes, CultureInfo.CurrentCulture, L("Value_NotAvailable")),
                    L("Safety_" + item.SafetyClass)))
                .ToArray();
        PlanLines.Reset(planLines);

        PlanSummary = string.Format(
            CultureInfo.CurrentCulture,
            "{0} · {1}: {2} · {3}",
            L(result.Plan.Summary),
            L("OneClick_Risk"),
            result.Plan.Risk is null ? L("Value_NotAvailable") : L("OneClick_Risk_" + result.Plan.Risk),
            L("OneClick_Plan_Excluded", result.Plan.ExcludedByUser.Count, result.Plan.ExcludedByPolicy.Count));

        Measurements.Reset(result.Measurements.Select(measurement => string.Format(
            CultureInfo.CurrentCulture,
            "{0}: {1} → {2} ({3} {4}) — {5}",
            L(measurement.DisplayNameKey),
            Text(measurement.Before),
            Text(measurement.After),
            L("OneClick_Measurement_Delta"),
            Text(measurement.Delta),
            measurement.Source)));

        OpenPointsText = result.OpenPoints.Count == 0
            ? L("OneClick_OpenPoints_None")
            : string.Join(Environment.NewLine, result.OpenPoints.Select((point, index) => $"{index + 1}. {point}"));

        ReportPath = result.Report is null
            ? L("OneClick_Report_NotWritten")
            : L("OneClick_Report_Written", result.Report.FilePath);

        // The state tokens of chapter 40 are the vocabulary of the specification (SUCCESS, BLOCKED,
        // CANCELLED, ...). They are shown as they are, exactly like the state badge in the header -
        // translating them would invent names the specification does not have.
        FinalStateText = result.FinalState.ToString();
        StatusText = L(result.Summary);
        Message = isPlan ? L("OneClick_Status_PlannedNoChange") : string.Empty;
        HasResult = true;
    }

    private string Text(Measured<long> value) => value.HasValue
        ? SizeText.Format(value, CultureInfo.CurrentCulture, L("Value_NotAvailable"))
        : L("Value_NotAvailable");

    private void OnApprovalRequested(object? sender, ApprovalRequest request)
    {
        // The service waits for exactly this decision. The page shows the draft the service built -
        // including the steps and the before/after preview - and hands the answer back. Any other
        // request (from another page) is left alone.
        void Apply()
        {
            if (_cancellation is null)
            {
                return;
            }

            _pendingApproval = request;
            ApprovalSummary = string.Format(
                CultureInfo.CurrentCulture,
                "{0}: {1} — {2}: {3} — {4}",
                L("OneClick_Approval_Risk"),
                request.Draft.Risk,
                L("OneClick_Approval_Action"),
                L(request.Draft.Action),
                L(request.Draft.What));
            IsAwaitingApproval = true;
            StatusText = L("OneClick_Status_AwaitingApproval");
        }

        if (_dispatcher.IsOnUiThread)
        {
            Apply();
        }
        else
        {
            _dispatcher.Post(Apply);
        }
    }

    private void Approve()
    {
        if (_pendingApproval is null)
        {
            return;
        }

        _approvals.Decide(_pendingApproval.RequestId, ApprovalDecision.Approved, "approved on the one-click page");
        IsAwaitingApproval = false;
        StatusText = L("OneClick_Status_Approved");
    }

    private void Reject()
    {
        if (_pendingApproval is null)
        {
            return;
        }

        _approvals.Decide(_pendingApproval.RequestId, ApprovalDecision.Rejected, "refused on the one-click page");
        IsAwaitingApproval = false;
        StatusText = L("OneClick_Status_Rejected");
    }
}

/// <summary>A category the user can deselect before the run plans it (M27-S-002).</summary>
public sealed class OneClickCategoryRow : ObservableObject
{
    private bool _isSelected;

    public MaintenanceCategory Category { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Translated safety class, so "Protected" never reaches the screen as an enum name.</summary>
    public string SafetyText { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool RequiresAdministrator { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>One line of the phase list: what, why, risk, result, evidence.</summary>
public sealed class OneClickStepRow
{
    public string Phase { get; init; } = string.Empty;

    public string What { get; init; } = string.Empty;

    public string Why { get; init; } = string.Empty;

    public string Risk { get; init; } = string.Empty;

    public string Outcome { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public bool IsPhaseStep { get; init; }
}
