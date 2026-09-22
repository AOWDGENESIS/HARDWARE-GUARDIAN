using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The recovery as a product, not as an engine (spec section 41, module M35).
///
/// The engine tests prove that the three questions are answered from records. What is checked here is
/// the part a user sees and the part an auditor reads: the interrupted operation becomes a finding
/// with a stable identifier (chapter 85), the recovery is offered with an approval draft that names
/// exactly this backup (M35-S-001), a declined approval changes nothing, and a run that was executed
/// but not validated neither closes the finding nor claims a restored system (chapter 86).
///
/// The problem registry is the real one - a double would prove nothing about the identifier the
/// specification prescribes.
/// </summary>
public sealed class RecoveryCoordinatorTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public async Task Without_an_interrupted_run_there_is_no_finding_and_nothing_to_approve()
    {
        var fixture = Build();

        var plan = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        Assert.False(plan.RecoveryAvailable);
        Assert.Null(plan.ApprovalDraft);
        Assert.Null(plan.ProblemId);
        Assert.Empty(fixture.Problems.All);
    }

    [Fact]
    public async Task An_interrupted_run_becomes_a_finding_with_a_stable_identifier_and_an_approval_draft()
    {
        var fixture = Build();
        Interrupted(fixture, "op-77", "Volume.ChkdskScan");

        var plan = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        Assert.True(plan.RecoveryAvailable);
        Assert.Equal("op-77", plan.OperationId);
        Assert.Equal(SystemState.Executing, plan.LastState);

        // Chapter 85: the finding carries a module identifier and the sections of the error picture.
        var problem = Assert.Single(fixture.Problems.All);
        Assert.Equal("WMC-M35-001", problem.Id);
        Assert.Equal("op-77", problem.ComponentId);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title.Key));
        Assert.False(string.IsNullOrWhiteSpace(problem.Cause.Key));
        Assert.False(string.IsNullOrWhiteSpace(problem.Impact.Key));
        Assert.False(string.IsNullOrWhiteSpace(problem.RecommendedAction.Key));
        Assert.Contains("op-77", problem.Evidence, StringComparison.Ordinal);
        Assert.Equal(problem.Id, plan.ProblemId);

        // M35-S-001: the approval names exactly this backup, and the risk is high, so nobody can
        // approve a recovery by accident.
        var draft = Assert.IsType<ApprovalRequestDraft>(plan.ApprovalDraft);
        Assert.Equal("Recovery:BKP-TEST-001", draft.OperationId);
        Assert.Equal(RiskLevel.High, draft.Risk);
        Assert.Equal(OperationKind.Rollback, draft.Operation);
        Assert.Single(draft.Steps);
        Assert.NotEmpty(draft.Evidence);
    }

    /// <summary>
    /// The application asks again at every start and after every run. Asking twice must not produce a
    /// second finding for the same interruption - otherwise the problem centre grows by one entry
    /// every time somebody opens the page.
    /// </summary>
    [Fact]
    public async Task Asking_twice_keeps_one_finding()
    {
        var fixture = Build();
        Interrupted(fixture, "op-78", "Volume.ChkdskScan");

        var first = await fixture.Coordinator.PrepareAsync(CancellationToken.None);
        var second = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        Assert.Equal(first.ProblemId, second.ProblemId);
        Assert.Single(fixture.Problems.All);
    }

    [Fact]
    public async Task A_recovery_that_is_not_approved_changes_nothing_and_leaves_the_finding_open()
    {
        var fixture = Build();
        Interrupted(fixture, "op-79", "Volume.ChkdskScan");
        var plan = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        // The user declined: a record exists, but it is not an approval.
        var declined = new ApprovalRecord
        {
            RequestId = "APR-1",
            OperationId = plan.ApprovalDraft!.OperationId,
            Decision = ApprovalDecision.Rejected,
            DecidedAt = _clock.Now,
        };
        var before = fixture.Journal.Read().Count;

        var outcome = await fixture.Coordinator.ExecuteAsync(plan, declined, progress: null, CancellationToken.None);

        Assert.False(outcome.Attempted);
        Assert.False(outcome.Verified);
        Assert.Equal(BlockedReasonCodes.RecoveryApprovalRequired, outcome.BlockedReasonCode);
        Assert.Equal("Recovery_Result_NotAttemptedBlocked", outcome.Summary.Key);
        Assert.Empty(fixture.Rollback.Requests);
        Assert.Equal(ProblemStatus.Open, Assert.Single(fixture.Problems.All).Status);

        // Nothing was executed, so nothing was written into the journal - and the interrupted
        // operation keeps being offered.
        Assert.Equal(before, fixture.Journal.Read().Count);
        Assert.True((await fixture.Coordinator.PrepareAsync(CancellationToken.None)).RecoveryAvailable);
    }

    [Fact]
    public async Task An_approved_recovery_runs_the_rollback_and_closes_the_finding()
    {
        var fixture = Build(verified: true);
        Interrupted(fixture, "op-80", "Volume.ChkdskScan");
        var plan = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        var outcome = await fixture.Coordinator.ExecuteAsync(
            plan,
            Approved(plan),
            progress: null,
            CancellationToken.None);

        Assert.True(outcome.Attempted);
        Assert.True(outcome.Verified);
        Assert.Equal("Recovery_Result_Verified", outcome.Summary.Key);
        Assert.Equal("BKP-TEST-001", Assert.Single(fixture.Rollback.Requests).BackupRecordId);
        Assert.Equal(ProblemStatus.Resolved, Assert.Single(fixture.Problems.All).Status);
    }

    /// <summary>
    /// An approval for another operation is not a permission for this one - the record has to name the
    /// backup, not just be "approved" (M35-S-001).
    /// </summary>
    [Fact]
    public async Task An_approval_for_something_else_does_not_authorise_the_recovery()
    {
        var fixture = Build();
        Interrupted(fixture, "op-82", "Volume.ChkdskScan");
        var plan = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        var outcome = await fixture.Coordinator.ExecuteAsync(
            plan,
            new ApprovalRecord
            {
                RequestId = "APR-4",
                OperationId = "Cleanup.WindowsUpdateCache",
                Decision = ApprovalDecision.Approved,
                DecidedAt = _clock.Now,
            },
            progress: null,
            CancellationToken.None);

        Assert.False(outcome.Attempted);
        Assert.Equal(BlockedReasonCodes.RecoveryApprovalRequired, outcome.BlockedReasonCode);
        Assert.Empty(fixture.Rollback.Requests);
    }

    /// <summary>
    /// Chapter 86: a rollback that ran but was not confirmed must not close the finding. The state of
    /// the system is unproven, and the problem centre has to keep saying so.
    /// </summary>
    [Fact]
    public async Task A_run_that_was_not_validated_neither_claims_success_nor_closes_the_finding()
    {
        var fixture = Build(verified: false);
        Interrupted(fixture, "op-81", "Volume.ChkdskScan");
        var plan = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        var outcome = await fixture.Coordinator.ExecuteAsync(
            plan,
            Approved(plan),
            progress: null,
            CancellationToken.None);

        Assert.True(outcome.Attempted);
        Assert.False(outcome.Verified);
        Assert.Equal("Recovery_Result_NotVerified", outcome.Summary.Key);
        Assert.Equal(ProblemStatus.Open, Assert.Single(fixture.Problems.All).Status);
    }

    /// <summary>
    /// A plan without an approval draft cannot be executed through the coordinator either - not even
    /// when the caller hands in an approval that looks valid. The refusal names the reason code of the
    /// specification.
    /// </summary>
    [Fact]
    public async Task A_plan_without_a_draft_cannot_be_executed()
    {
        var fixture = Build();
        var plan = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        var outcome = await fixture.Coordinator.ExecuteAsync(
            plan,
            new ApprovalRecord { RequestId = "APR-2", Decision = ApprovalDecision.Approved, DecidedAt = _clock.Now },
            progress: null,
            CancellationToken.None);

        Assert.False(outcome.Attempted);
        Assert.Equal(BlockedReasonCodes.RecoveryNotAvailable, outcome.BlockedReasonCode);
        Assert.Empty(fixture.Problems.All);
    }

    /// <summary>
    /// The recovery has to stand in the journal - otherwise the application would offer the same
    /// recovery at every future start, although the interruption was dealt with.
    /// </summary>
    [Fact]
    public async Task A_verified_recovery_ends_the_interruption_in_the_journal()
    {
        var fixture = Build(verified: true);
        Interrupted(fixture, "op-83", "Volume.ChkdskScan");
        var plan = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        await fixture.Coordinator.ExecuteAsync(plan, Approved(plan), progress: null, CancellationToken.None);

        Assert.Equal(SystemState.Success, fixture.State.Current);
        Assert.Equal(SystemState.Success, fixture.Journal.Read()[^1].To);

        // After the run there is nothing left to offer, and no second finding appears.
        var after = await fixture.Coordinator.PrepareAsync(CancellationToken.None);
        Assert.False(after.RecoveryAvailable);
        Assert.Single(fixture.Problems.All);
        Assert.Equal(ProblemStatus.Resolved, fixture.Problems.All[0].Status);
    }

    /// <summary>
    /// A refused request is not a state change. The interrupted operation keeps being offered, so the
    /// user can decide again - and the journal shows no recovery that never happened.
    /// </summary>
    [Fact]
    public async Task A_refused_request_leaves_the_interruption_untouched()
    {
        var fixture = Build();
        Interrupted(fixture, "op-84", "Volume.ChkdskScan");
        var plan = await fixture.Coordinator.PrepareAsync(CancellationToken.None);
        var before = fixture.Journal.Read().Count;

        var outcome = await fixture.Coordinator.ExecuteAsync(plan, approval: null, progress: null, CancellationToken.None);

        Assert.False(outcome.Attempted);
        Assert.Equal(before, fixture.Journal.Read().Count);

        var again = await fixture.Coordinator.PrepareAsync(CancellationToken.None);
        Assert.True(again.RecoveryAvailable);
        Assert.Equal("BKP-TEST-001", again.BackupRecordId);
    }

    /// <summary>
    /// A crash in the middle of a recovery must stay recognisable - and it has to name the same
    /// operation, so the same backup is found again instead of the user being left with nothing.
    /// </summary>
    [Fact]
    public async Task A_crash_during_the_recovery_keeps_the_same_operation_recognisable()
    {
        var fixture = Build();
        Interrupted(fixture, "op-85", "Volume.ChkdskScan");
        var plan = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        // The recovery is booked as a state change, and then the process dies before it can be
        // finished: that is exactly the entry the next start has to read.
        fixture.State.BeginOperation("op-85", "Volume.ChkdskScan");
        fixture.State.TryTransitionTo(SystemState.Recovering, "recovery:BKP-TEST-001");

        var next = await fixture.Coordinator.PrepareAsync(CancellationToken.None);

        Assert.True(next.RecoveryAvailable);
        Assert.Equal("op-85", next.OperationId);
        Assert.Equal(SystemState.Recovering, next.LastState);
        Assert.Equal("BKP-TEST-001", next.BackupRecordId);
        Assert.True(next.RollbackPossible);
    }

    private static ApprovalRecord Approved(RecoveryPlan plan) => new()
    {
        RequestId = "APR-3",
        OperationId = plan.ApprovalDraft!.OperationId,
        Decision = ApprovalDecision.Approved,
        DecidedAt = DateTimeOffset.UnixEpoch,
        Risk = RiskLevel.High,
        WasRequired = true,
    };

    private Fixture Build(bool verified = true)
    {
        var backups = new RecordingBackupService(_clock);
        var rollback = new RecordingRollbackService { CanRollback = true, Verified = verified };
        var problems = new ProblemRegistry(_clock);
        var journal = new InMemoryStateJournal();
        var engine = new RecoveryEngine(journal, backups, rollback);
        var protocol = new RecordingLiveProtocol();
        var state = new SystemStateMachine(_clock, events: null, logger: null, journal: journal);

        return new Fixture(
            new RecoveryCoordinator(engine, problems, protocol, state, _clock),
            problems,
            backups,
            rollback,
            journal,
            state);
    }

    /// <summary>
    /// Builds the situation the module exists for: a run cut off while an operation was running, with
    /// the backup of exactly that operation on record.
    /// </summary>
    private void Interrupted(Fixture fixture, string operationId, string actionId)
    {
        var machine = new SystemStateMachine(_clock, events: null, logger: null, journal: fixture.Journal);
        machine.BeginOperation(operationId, actionId);
        machine.TryTransitionTo(SystemState.Discovery, "start");
        machine.TryTransitionTo(SystemState.Diagnostic, "assess");
        machine.TryTransitionTo(SystemState.PlanGenerated, "plan");
        machine.TryTransitionTo(SystemState.AwaitingApproval, "ask");
        machine.TryTransitionTo(SystemState.Backup, "secure");
        machine.TryTransitionTo(SystemState.Executing, "run");

        fixture.Backups
            .CreateAsync(
                new BackupRequest { OperationId = operationId, Kind = OperationKind.Execute, Risk = RiskLevel.Medium },
                progress: null,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private sealed record Fixture(
        RecoveryCoordinator Coordinator,
        ProblemRegistry Problems,
        RecordingBackupService Backups,
        RecordingRollbackService Rollback,
        InMemoryStateJournal Journal,
        SystemStateMachine State);
}
