using System.Collections.ObjectModel;
using WindowsMaintenanceCenter.App.Mvvm;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.App.ViewModels;

/// <summary>
/// The recovery page (spec section 41, module M35).
///
/// The page exists because the recovery engine alone is not a product: without it a user could not
/// see RECOVERY AVAILABLE and could not approve the restoration of an interrupted operation, so the
/// engine would be code that nothing reaches.
///
/// It owns no decision. What the page shows comes from <see cref="IRecoveryCoordinator"/> - which
/// operation was interrupted, whether a backup exists, whether a rollback is possible, which finding
/// was registered and what the approval has to say. What the page does is hand the user's decision to
/// the coordinator and show the outcome, including the case that the recovery ran but was **not**
/// validated (chapter 86).
/// </summary>
public sealed class RecoveryViewModel : ViewModelBase
{
    private readonly IRecoveryCoordinator _coordinator;
    private readonly IApprovalService _approvals;
    private readonly IClock _clock;

    private RecoveryPlan? _plan;
    private ApprovalRequest? _pendingApproval;
    private string _statusText = string.Empty;
    private string _message = string.Empty;
    private string _approvalSummary = string.Empty;
    private bool _isAwaitingApproval;
    private bool _isAvailable;
    private bool _hasResult;

    public RecoveryViewModel(
        ILocalizer localizer,
        IRecoveryCoordinator coordinator,
        IApprovalService approvals,
        IClock clock)
        : base(localizer)
    {
        _coordinator = coordinator;
        _approvals = approvals;
        _clock = clock;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RecoverCommand = new AsyncRelayCommand(RequestRecoveryAsync, () => CanRecover);
        ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => _isAwaitingApproval);
        DeclineCommand = new AsyncRelayCommand(DeclineAsync, () => _isAwaitingApproval);

        StatusText = L("Recovery_Headline_None");
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand RecoverCommand { get; }

    public AsyncRelayCommand ApproveCommand { get; }

    public AsyncRelayCommand DeclineCommand { get; }

    /// <summary>True when the previous run was cut off - the condition for "RECOVERY AVAILABLE".</summary>
    public bool IsAvailable
    {
        get => _isAvailable;
        private set
        {
            if (SetProperty(ref _isAvailable, value))
            {
                OnPropertyChanged(nameof(Headline));
                OnPropertyChanged(nameof(CanRecover));
                RecoverCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>The sentence a user reads after a crash (M35).</summary>
    public string Headline => IsAvailable ? L("Recovery_Available") : L("Recovery_Headline_None");

    /// <summary>What the operation was, at which state it stopped and which backup exists.</summary>
    public ObservableCollection<string> Lines { get; } = new();

    /// <summary>What the recovery did - execution and verification, never one without the other.</summary>
    public ObservableCollection<string> ResultLines { get; } = new();

    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

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

    public string ApprovalSummary
    {
        get => _approvalSummary;
        private set => SetProperty(ref _approvalSummary, value);
    }

    public bool IsAwaitingApproval
    {
        get => _isAwaitingApproval;
        private set
        {
            if (SetProperty(ref _isAwaitingApproval, value))
            {
                ApproveCommand.RaiseCanExecuteChanged();
                DeclineCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanRecover));
                RecoverCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// A recovery may only be requested while one is offered and while no request is waiting. The
    /// reason is M35-S-001: one interrupted operation, one approval, one run.
    /// </summary>
    public bool CanRecover => IsAvailable && !IsAwaitingApproval && _plan?.ApprovalDraft is not null;

    /// <summary>
    /// Reads the journal and shows what it found. Called when the page is opened and after a run.
    /// </summary>
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            _plan = await _coordinator.PrepareAsync(CancellationToken.None).ConfigureAwait(true);
            Apply(_plan);
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

    private void Apply(RecoveryPlan plan)
    {
        Lines.Clear();
        IsAvailable = plan.RecoveryAvailable;
        StatusText = L(plan.Summary);

        if (!plan.RecoveryAvailable)
        {
            Message = string.Empty;
            foreach (var line in plan.Evidence)
            {
                Lines.Add(line);
            }

            return;
        }

        Lines.Add($"{L("Recovery_Field_Operation")}: {plan.OperationId ?? "—"}");
        Lines.Add($"{L("Recovery_Field_Action")}: {plan.ActionId ?? "—"}");
        Lines.Add($"{L("Recovery_Field_State")}: {plan.LastState?.ToString() ?? "—"}");
        Lines.Add($"{L("Recovery_Field_Time")}: {(plan.LastChangeAt is { } at ? at.ToString("u") : "—")}");
        Lines.Add($"{L("Recovery_Field_Backup")}: {plan.BackupRecordId ?? L("Value_NotAvailable")}");
        Lines.Add($"{L("Recovery_Field_Rollback")}: {L(plan.RollbackPossible ? "Value_Yes" : "Value_No")}");
        Lines.Add($"{L("Recovery_Field_Problem")}: {plan.ProblemId ?? L("Value_NotAvailable")}");
        Lines.Add($"{L("Recovery_Field_Log")}: {plan.ProblemId ?? L("Report_LogReference_None")}");

        if (!plan.BackupFound)
        {
            // A plan without a backup cannot be approved: there is nothing to restore, and the page
            // says so instead of offering a button that would only fail (chapter 96).
            Message = L("Recovery_Blocked_NoBackup");
        }
        else
        {
            Message = L("Recovery_Hint_Approval");
        }
    }

    /// <summary>Asks for the approval the recovery needs - and does nothing until it is granted.</summary>
    private async Task RequestRecoveryAsync()
    {
        if (_plan?.ApprovalDraft is null || IsAwaitingApproval)
        {
            return;
        }

        IsBusy = true;
        try
        {
            _pendingApproval = await _approvals.CreateAsync(_plan.ApprovalDraft, CancellationToken.None).ConfigureAwait(true);
            ApprovalSummary = $"{L(_pendingApproval.Draft.Action)}: {L(_pendingApproval.Draft.What)}";
            IsAwaitingApproval = true;
            StatusText = L("Recovery_Blocked_ApprovalRequired");
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
            await RunAsync(Record(decided)).ConfigureAwait(true);
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

    /// <summary>
    /// Declines the recovery. The decision is recorded and the refusal is audited like every other
    /// refused operation - "nothing happened because the user said no" has to be provable too.
    /// </summary>
    private async Task DeclineAsync()
    {
        if (_pendingApproval is null || _plan is null)
        {
            return;
        }

        var requestId = _pendingApproval.RequestId;
        _approvals.Decide(requestId, ApprovalDecision.Rejected, note: "recovery-declined");
        IsAwaitingApproval = false;
        IsBusy = true;

        try
        {
            var decided = await _approvals.WaitForDecisionAsync(requestId, CancellationToken.None).ConfigureAwait(true);
            await RunAsync(Record(decided)).ConfigureAwait(true);
            Message = L("Recovery_Declined_Recorded");
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

    private async Task RunAsync(ApprovalRecord record)
    {
        var outcome = await _coordinator
            .ExecuteAsync(_plan!, record, progress: null, CancellationToken.None)
            .ConfigureAwait(true);

        HasResult = true;
        ResultLines.Clear();
        ResultLines.Add(L(outcome.Summary));
        foreach (var line in outcome.Evidence)
        {
            ResultLines.Add(line);
        }

        StatusText = L(outcome.Summary);
        _pendingApproval = null;
        ApprovalSummary = string.Empty;

        // The page shows what the run was: a verified recovery closes the finding, an unverified one
        // leaves it open, and the wording of the two differs on purpose (chapter 86).
        await RefreshAsync().ConfigureAwait(true);
    }

    private ApprovalRecord Record(ApprovalRequest decided) => new()
    {
        RequestId = decided.RequestId,
        OperationId = decided.Draft.OperationId,
        Decision = decided.Decision,
        DecidedAt = decided.DecidedAt ?? _clock.Now,
        Note = decided.Note,
        Risk = decided.Draft.Risk,
        WasRequired = true,
    };

    protected override void OnLanguageChangedCore()
    {
        // The headline is produced in code, so it has to be re-published when the language changes;
        // the strings on the page itself come from the localizer and refresh through the base.
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(StatusText));
    }
}
