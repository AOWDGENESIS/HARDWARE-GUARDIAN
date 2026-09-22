using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// What a caller has to know before a recovery may be offered to a user (spec section 41, M35).
/// </summary>
public sealed record RecoveryPlan
{
    /// <summary>True when the previous run was cut off in the middle.</summary>
    public bool RecoveryAvailable { get; init; }

    public SystemState? LastState { get; init; }

    public DateTimeOffset? LastChangeAt { get; init; }

    public string? OperationId { get; init; }

    public string? ActionId { get; init; }

    public string? BackupRecordId { get; init; }

    public bool BackupFound { get; init; }

    public bool RollbackPossible { get; init; }

    /// <summary>
    /// Identifier of the finding in the problem centre (chapter 85, <c>WMC-M35-###</c>). Null when
    /// there is nothing to report.
    /// </summary>
    public string? ProblemId { get; init; }

    /// <summary>
    /// The approval a recovery needs. Null when no recovery can be offered - then there is nothing to
    /// approve either (M35-S-001).
    /// </summary>
    public ApprovalRequestDraft? ApprovalDraft { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Recovery_Reason_NothingRecorded");

    /// <summary>Lines that back the verdict; the interface shows them, the report carries them.</summary>
    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>What the recovery did - stated as execution <b>and</b> verification (chapter 86).</summary>
public sealed record RecoveryOutcome
{
    public bool Attempted { get; init; }

    public bool Verified { get; init; }

    public string? BlockedReasonCode { get; init; }

    public string? BackupRecordId { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Recovery_Result_NotAttempted");

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Connects the recovery engine to the rest of the application (M35).
///
/// The engine answers three questions from records. This layer adds what a product needs on top of
/// them and keeps it out of the interface: the interrupted operation becomes a finding in the problem
/// centre with a stable identifier, a reason, an impact and a recommended action (chapter 85); the
/// recovery is offered as an operation that carries its own approval draft (M35-S-001); and the
/// verdict of the run is written where it belongs - protocol, finding and audit.
///
/// Nothing here decides on its own. Without a plan there is no draft, and without a draft the caller
/// cannot approve anything - a state the tests pin.
/// </summary>
public interface IRecoveryCoordinator
{
    /// <summary>
    /// Reads the journal and prepares everything a user needs to decide: the finding, the wording and
    /// the approval draft. Read only - a call changes nothing but the problem registry.
    /// </summary>
    Task<RecoveryPlan> PrepareAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs the recovery of the prepared plan with the given approval. An approval that does not name
    /// exactly this backup is refused by the engine, and this method reports that outcome unchanged.
    /// </summary>
    Task<RecoveryOutcome> ExecuteAsync(
        RecoveryPlan plan,
        ApprovalRecord? approval,
        IProgress<ProgressSnapshot>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Default coordinator, built on the recovery engine and the problem registry.</summary>
public sealed class RecoveryCoordinator : IRecoveryCoordinator
{
    /// <summary>
    /// Theme of the identifiers of this module: a finding of the recovery reads <c>WMC-M35-001</c>
    /// (chapter 85), not <c>WMC-MAINTENANCE-001</c> - the specification names M35 as the module that
    /// reports it, and the identifier has to say which module a reader can look up.
    /// </summary>
    public const string ProblemIdPrefix = "WMC-M35";

    private readonly IRecoveryEngine _engine;
    private readonly IProblemRegistry _problems;
    private readonly ILiveProtocol _protocol;
    private readonly ISystemStateMachine _state;
    private readonly IClock _clock;

    public RecoveryCoordinator(
        IRecoveryEngine engine,
        IProblemRegistry problems,
        ILiveProtocol protocol,
        ISystemStateMachine state,
        IClock clock)
    {
        _engine = engine;
        _problems = problems;
        _protocol = protocol;
        _state = state;
        _clock = clock;
    }

    public async Task<RecoveryPlan> PrepareAsync(CancellationToken cancellationToken)
    {
        var assessment = await _engine.AssessAsync(cancellationToken).ConfigureAwait(false);

        var evidence = assessment.Evidence.ToList();
        evidence.AddRange(assessment.JournalEvidence);

        if (!assessment.RecoveryAvailable)
        {
            return new RecoveryPlan
            {
                RecoveryAvailable = false,
                LastState = assessment.LastState,
                LastChangeAt = assessment.LastChangeAt,
                Summary = assessment.Summary,
                Evidence = evidence,
            };
        }

        var problemId = RegisterFinding(assessment);

        // The operation a user approves is the recovery of exactly this backup; the engine checks that
        // identity itself, so a draft that named anything else would be refused there.
        var draft = assessment.BackupRecordId is null
            ? null
            : new ApprovalRequestDraft
            {
                OperationId = _engine.ApprovalOperationId(assessment.BackupRecordId),
                Operation = OperationKind.Rollback,
                Category = ComponentCategory.Maintenance,
                ComponentId = assessment.OperationId,
                Action = LocalizedText.Of("Recovery_Action_Recover"),
                What = LocalizedText.Of(
                    "Recovery_Plan_What",
                    assessment.OperationId ?? "unknown",
                    assessment.BackupRecordId),
                Why = LocalizedText.Of("Recovery_Plan_Why", assessment.LastState?.ToString() ?? "unknown"),
                Risk = RiskLevel.High,
                RiskSummary = LocalizedText.Of("Recovery_Plan_Impact"),
                RequiresAdministrator = false,
                Steps = new[] { LocalizedText.Of("Recovery_Plan_Step", assessment.BackupRecordId) },
                Preview = new[]
                {
                    new ChangePreview
                    {
                        LabelKey = "Recovery_Field_State",
                        OldValue = assessment.LastState?.ToString(),
                        NewValue = SystemState.Recovering.ToString(),
                        Reason = LocalizedText.Of("Recovery_Plan_Impact"),
                        Risk = RiskLevel.High,
                    },
                },
                Evidence = evidence,
            };

        return new RecoveryPlan
        {
            RecoveryAvailable = true,
            LastState = assessment.LastState,
            LastChangeAt = assessment.LastChangeAt,
            OperationId = assessment.OperationId,
            ActionId = assessment.ActionId,
            BackupRecordId = assessment.BackupRecordId,
            BackupFound = assessment.BackupFound,
            RollbackPossible = assessment.RollbackPossible,
            ProblemId = problemId,
            ApprovalDraft = draft,
            Summary = assessment.Summary,
            Evidence = evidence,
        };
    }

    public async Task<RecoveryOutcome> ExecuteAsync(
        RecoveryPlan plan,
        ApprovalRecord? approval,
        IProgress<ProgressSnapshot>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.RecoveryAvailable || plan.ApprovalDraft is null)
        {
            // No plan, no run. The engine would refuse just as well, but a caller that never had
            // anything to approve must not be able to reach an execution through this method.
            return new RecoveryOutcome
            {
                Attempted = false,
                Verified = false,
                BlockedReasonCode = BlockedReasonCodes.RecoveryNotAvailable,
                Summary = LocalizedText.Of("Recovery_Blocked_NothingToRecover"),
                Evidence = plan.Evidence,
            };
        }

        // The recovery is an operation of the application and has to stand in the journal. Only then
        // does a crash *during* a recovery look like what it is - the same interrupted operation, which
        // can be recovered again - and only then does a recovery that ran to the end stop the
        // application from offering the same recovery at every future start.
        var booked = BeginRecovery(plan, approval);

        RecoveryResult result;
        try
        {
            result = await _engine.RecoverAsync(
                new RecoveryRequest
                {
                    OperationKey = plan.ApprovalDraft.OperationId,
                    Approval = approval,
                    Reason = LocalizedText.Of("Recovery_Reason_InterruptedJob"),
                },
                progress,
                cancellationToken).ConfigureAwait(false);

            if (booked)
            {
                EndRecovery(result);
            }
        }
        finally
        {
            if (booked)
            {
                _state.EndOperation();
            }
        }

        var evidence = plan.Evidence.ToList();
        evidence.Add($"attempted={result.Attempted}, verified={result.Verified}");
        evidence.AddRange(result.Steps.Select(step => $"step={step}"));
        if (!string.IsNullOrWhiteSpace(result.ErrorDetail))
        {
            evidence.Add($"error={result.ErrorDetail}");
        }

        var outcome = new RecoveryOutcome
        {
            Attempted = result.Attempted,
            Verified = result.Verified,
            BlockedReasonCode = result.BlockedReasonCode,
            BackupRecordId = result.BackupRecordId,
            Summary = Verdict(result),
            Evidence = evidence,
        };

        Announce(plan, outcome);
        UpdateFinding(plan, outcome);
        return outcome;
    }

    /// <summary>
    /// Marks the start of a recovery in the state machine - but only when the caller really may start
    /// one. A refusal that happens before this point (no approval, an approval for another operation,
    /// no restorable backup) is not a state change: it is a request that was turned down, and the
    /// interrupted operation has to keep being offered afterwards (M35-S-001 does not mean "once
    /// asked, never again").
    /// </summary>
    private bool BeginRecovery(RecoveryPlan plan, ApprovalRecord? approval)
    {
        // The engine is the authority on whether a recovery may run and it audits every refusal. What
        // is decided here is something else: whether a *state change* is booked. Without an approval
        // for exactly this backup nothing will be executed, so nothing may be written into the journal
        // either - a journal that shows a recovery that never started would be a false record.
        var authorised = approval is not null
            && approval.Decision == ApprovalDecision.Approved
            && plan.ApprovalDraft is not null
            && string.Equals(approval.OperationId, plan.ApprovalDraft.OperationId, StringComparison.Ordinal);

        var mayStart = authorised
            && plan.RollbackPossible
            && plan.BackupRecordId is not null
            && !string.IsNullOrWhiteSpace(plan.OperationId);

        if (!mayStart)
        {
            return false;
        }

        _state.BeginOperation(plan.OperationId!, plan.ActionId);

        // A crash during an earlier recovery leaves the machine in RECOVERING already; entering it
        // twice is not a transition and would be refused.
        if (_state.Current != SystemState.Recovering)
        {
            _state.TryTransitionTo(SystemState.Recovering, $"recovery:{plan.BackupRecordId}");
        }

        return true;
    }

    /// <summary>
    /// Closes the recovery in the state machine with the state the run really reached (chapter 86):
    /// a verified rollback ends in SUCCESS, an executed but unconfirmed one in BLOCKED. A refusal that
    /// only became known when the engine ran (the backup disappeared in between) stays in RECOVERING -
    /// that state is interruptible, so the interrupted operation keeps being offered instead of being
    /// declared handled.
    /// </summary>
    private void EndRecovery(RecoveryResult result)
    {
        if (result.BlockedReasonCode is not null)
        {
            _protocol.Publish(
                "REC",
                LocalizedText.Of("Recovery_Result_NotAttemptedBlocked", result.BlockedReasonCode),
                Severity.Warning,
                $"backup={result.BackupRecordId ?? "none"}");
            return;
        }

        _state.TryTransitionTo(SystemState.Rollback, $"recovery-rollback:{result.BackupRecordId ?? "unknown"}");
        _state.TryTransitionTo(SystemState.Validating, "recovery-verification");
        _state.TryTransitionTo(
            result.Verified ? SystemState.Success : SystemState.Blocked,
            result.Verified ? "recovery-verified" : "RECOVERY_NOT_VERIFIED");
    }

    /// <summary>
    /// The verdict of a run, worded so that the three cases cannot be confused: done and confirmed,
    /// done but not confirmed, or not done at all (chapter 86 - success needs execution and
    /// validation).
    /// </summary>
    private static LocalizedText Verdict(RecoveryResult result)
    {
        if (!result.Attempted)
        {
            return result.BlockedReasonCode is null
                ? LocalizedText.Of("Recovery_Result_NotAttempted")
                : LocalizedText.Of("Recovery_Result_NotAttemptedBlocked", result.BlockedReasonCode);
        }

        return result.Verified
            ? LocalizedText.Of("Recovery_Result_Verified", result.BackupRecordId ?? "unknown")
            : LocalizedText.Of("Recovery_Result_NotVerified", result.BackupRecordId ?? "unknown");
    }

    private void Announce(RecoveryPlan plan, RecoveryOutcome outcome)
    {
        var severity = outcome.Verified
            ? Severity.Success
            : outcome.Attempted ? Severity.Warning : Severity.Blocked;

        var detail = $"operation={plan.OperationId ?? "unknown"} | backup={outcome.BackupRecordId ?? "none"}";

        _protocol.Publish("REC", outcome.Summary, severity, detail);
    }

    /// <summary>
    /// Writes the finding for the interrupted operation. An operation that is already reported keeps
    /// its identifier: a second call must not produce a second finding for the same interruption -
    /// otherwise the problem centre would grow every time the application is started.
    /// </summary>
    private string? RegisterFinding(RecoveryAssessment assessment)
    {
        if (string.IsNullOrWhiteSpace(assessment.OperationId))
        {
            // Nothing to name, nothing to prove: a finding without an operation would be a claim.
            return null;
        }

        var existing = _problems
            .ForComponent(assessment.OperationId)
            .FirstOrDefault(problem => problem.Status is ProblemStatus.Open or ProblemStatus.Acknowledged);

        if (existing is not null)
        {
            return existing.Id;
        }

        var problem = _problems.Add(new ProblemDraft
        {
            IdPrefix = ProblemIdPrefix,
            Category = ComponentCategory.Maintenance,
            Severity = Severity.Warning,
            ComponentId = assessment.OperationId,
            ComponentName = assessment.ActionId,
            ActionId = assessment.ActionId,
            Title = LocalizedText.Of("Recovery_Problem_Title", assessment.ActionId ?? assessment.OperationId),
            Description = LocalizedText.Of(
                "Recovery_Problem_Description",
                assessment.OperationId,
                assessment.LastState?.ToString() ?? "unknown"),
            Cause = LocalizedText.Of("Recovery_Problem_Cause", assessment.LastState?.ToString() ?? "unknown"),
            Impact = LocalizedText.Of(
                assessment.BackupFound ? "Recovery_Problem_Impact_Secured" : "Recovery_Problem_Impact_Unsecured"),
            RecommendedAction = LocalizedText.Of("Recovery_Problem_Action"),
            Evidence = string.Join("; ", assessment.Evidence.Concat(assessment.JournalEvidence)),
            RequiresAdministrator = false,
        });

        return problem.Id;
    }

    private void UpdateFinding(RecoveryPlan plan, RecoveryOutcome outcome)
    {
        if (plan.ProblemId is null)
        {
            return;
        }

        if (outcome.Verified)
        {
            _problems.UpdateStatus(plan.ProblemId, ProblemStatus.Resolved, $"recovery verified at {_clock.Now:u}");
            return;
        }

        // Attempted but not confirmed stays open - and is worded as such. Closing it here would claim
        // a restored system that nobody measured (chapter 86).
        _problems.UpdateStatus(
            plan.ProblemId,
            ProblemStatus.Open,
            outcome.Attempted ? "recovery not verified" : "recovery not executed");
    }
}
