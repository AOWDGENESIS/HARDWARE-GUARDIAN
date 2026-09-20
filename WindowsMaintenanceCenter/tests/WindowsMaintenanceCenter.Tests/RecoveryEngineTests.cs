using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// Recovery after an interrupted run (specification chapter 41, module M35).
///
/// The engine has to answer three questions from records - which operation was interrupted (M35-F-001),
/// whether a backup exists for it (M35-F-002) and whether that backup can be restored (M35-F-003) -
/// and then it has to refuse to act on its own: M35-S-001 forbids a risky automatic recovery without
/// approval. Every test here drives the real engine with real journal entries; the backup and
/// rollback services are doubles, because the file system and the restore are the target machine's
/// part.
/// </summary>
public sealed class RecoveryEngineTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public async Task An_empty_journal_offers_no_recovery()
    {
        var engine = Engine(new RecordingBackupService(_clock), new RecordingRollbackService());

        var assessment = await engine.AssessAsync(CancellationToken.None);

        Assert.False(assessment.RecoveryAvailable);
        Assert.Equal("Recovery_Reason_NothingRecorded", assessment.Summary.Key);
        Assert.True(assessment.RequiresApproval);
    }

    [Fact]
    public async Task A_finished_run_offers_no_recovery()
    {
        var journal = new InMemoryStateJournal();
        var machine = new SystemStateMachine(_clock, events: null, logger: null, journal: journal);
        machine.TryTransitionTo(SystemState.Discovery, "start");
        machine.TryTransitionTo(SystemState.Diagnostic, "assess");
        machine.TryTransitionTo(SystemState.Success, "done");

        var engine = Engine(new RecordingBackupService(_clock), new RecordingRollbackService(), journal);
        var assessment = await engine.AssessAsync(CancellationToken.None);

        Assert.False(assessment.RecoveryAvailable);
        Assert.Equal(SystemState.Success, assessment.LastState);
        Assert.Equal("Recovery_Reason_NoInterruption", assessment.Summary.Key);
    }

    [Fact]
    public async Task An_interrupted_run_names_the_operation_the_backup_and_the_rollback_chance()
    {
        var journal = Interrupted("op-42", "Volume.ChkdskScan");
        var backups = new RecordingBackupService(_clock);
        await backups.CreateAsync(
            new BackupRequest { OperationId = "op-42", Kind = OperationKind.Execute, Risk = RiskLevel.Medium },
            progress: null,
            CancellationToken.None);

        var rollback = new RecordingRollbackService { CanRollback = true };
        var engine = Engine(backups, rollback, journal);

        var assessment = await engine.AssessAsync(CancellationToken.None);

        // M35-F-001: the operation and the action are named.
        Assert.True(assessment.RecoveryAvailable);
        Assert.Equal("op-42", assessment.OperationId);
        Assert.Equal("Volume.ChkdskScan", assessment.ActionId);
        Assert.Equal(SystemState.Executing, assessment.LastState);

        // M35-F-002: the backup of exactly that operation is found.
        Assert.True(assessment.BackupFound);
        Assert.Equal("BKP-TEST-001", assessment.BackupRecordId);

        // M35-F-003: the rollback service - not this engine - decides whether it can be restored.
        Assert.True(assessment.RollbackPossible);
        Assert.Equal("Recovery_Reason_Ready", assessment.Summary.Key);
        Assert.Contains(assessment.Evidence, line => line.Contains("backup=BKP-TEST-001", StringComparison.Ordinal));

        // M35-S-001: a recovery always needs its own approval.
        Assert.True(assessment.RequiresApproval);
    }

    [Fact]
    public async Task An_interrupted_run_without_a_backup_states_exactly_that()
    {
        var journal = Interrupted("op-43", "Volume.ChkdskScan");
        var engine = Engine(new RecordingBackupService(_clock), new RecordingRollbackService(), journal);

        var assessment = await engine.AssessAsync(CancellationToken.None);

        Assert.True(assessment.RecoveryAvailable);
        Assert.Equal("op-43", assessment.OperationId);
        Assert.False(assessment.BackupFound);
        Assert.Null(assessment.BackupRecordId);
        Assert.False(assessment.RollbackPossible);
        Assert.Equal("Recovery_Reason_BackupMissing", assessment.Summary.Key);
    }

    [Fact]
    public async Task A_backup_that_cannot_be_restored_is_not_a_rollback_chance()
    {
        var journal = Interrupted("op-44", "Volume.ChkdskScan");
        var backups = new RecordingBackupService(_clock);
        await backups.CreateAsync(
            new BackupRequest { OperationId = "op-44", Kind = OperationKind.Execute, Risk = RiskLevel.Medium },
            progress: null,
            CancellationToken.None);

        // The service says no - for example because the manifest is missing. The engine must report
        // that instead of offering a rollback it cannot perform.
        var engine = Engine(backups, new RecordingRollbackService { CanRollback = false }, journal);
        var assessment = await engine.AssessAsync(CancellationToken.None);

        Assert.True(assessment.BackupFound);
        Assert.False(assessment.RollbackPossible);
        Assert.Equal("Recovery_Reason_RollbackNotPossible", assessment.Summary.Key);
    }

    [Fact]
    public async Task Recovery_without_an_approval_changes_nothing()
    {
        var journal = Interrupted("op-45", "Volume.ChkdskScan");
        var backups = new RecordingBackupService(_clock);
        await backups.CreateAsync(
            new BackupRequest { OperationId = "op-45", Kind = OperationKind.Execute, Risk = RiskLevel.Medium },
            progress: null,
            CancellationToken.None);

        var rollback = new RecordingRollbackService { CanRollback = true };
        var engine = Engine(backups, rollback, journal);

        var result = await engine.RecoverAsync(new RecoveryRequest(), progress: null, CancellationToken.None);

        Assert.False(result.Attempted);
        Assert.False(result.Verified);
        Assert.Equal(BlockedReasonCodes.RecoveryApprovalRequired, result.BlockedReasonCode);
        Assert.Empty(rollback.Requests);
    }

    [Fact]
    public async Task An_approval_for_another_operation_does_not_authorise_a_recovery()
    {
        var journal = Interrupted("op-46", "Volume.ChkdskScan");
        var backups = new RecordingBackupService(_clock);
        await backups.CreateAsync(
            new BackupRequest { OperationId = "op-46", Kind = OperationKind.Execute, Risk = RiskLevel.Medium },
            progress: null,
            CancellationToken.None);

        var rollback = new RecordingRollbackService { CanRollback = true };
        var engine = Engine(backups, rollback, journal);

        var result = await engine.RecoverAsync(
            new RecoveryRequest
            {
                Approval = new ApprovalRecord
                {
                    RequestId = "req-1",
                    OperationId = "Volume.ChkdskScan",
                    Decision = ApprovalDecision.Approved,
                    DecidedAt = _clock.Now,
                },
            },
            progress: null,
            CancellationToken.None);

        Assert.Equal(BlockedReasonCodes.RecoveryApprovalRequired, result.BlockedReasonCode);
        Assert.Empty(rollback.Requests);
    }

    [Fact]
    public async Task Recovery_with_the_matching_approval_runs_the_rollback_and_reports_only_what_was_verified()
    {
        var journal = Interrupted("op-47", "Volume.ChkdskScan");
        var backups = new RecordingBackupService(_clock);
        await backups.CreateAsync(
            new BackupRequest { OperationId = "op-47", Kind = OperationKind.Execute, Risk = RiskLevel.Medium },
            progress: null,
            CancellationToken.None);

        var rollback = new RecordingRollbackService { CanRollback = true, Verified = true };
        var engine = Engine(backups, rollback, journal);
        var approval = new ApprovalRecord
        {
            RequestId = "req-2",
            OperationId = engine.ApprovalOperationId("BKP-TEST-001"),
            Decision = ApprovalDecision.Approved,
            DecidedAt = _clock.Now,
        };

        var result = await engine.RecoverAsync(
            new RecoveryRequest { Approval = approval, OperationKey = "Recovery_Operation_Default" },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Attempted);
        Assert.True(result.Verified);
        Assert.Null(result.BlockedReasonCode);
        var request = Assert.Single(rollback.Requests);
        Assert.Equal("BKP-TEST-001", request.BackupRecordId);
        Assert.Same(approval, request.Approval);
    }

    [Fact]
    public async Task A_rollback_that_ran_but_was_not_confirmed_is_not_reported_as_success()
    {
        var journal = Interrupted("op-48", "Volume.ChkdskScan");
        var backups = new RecordingBackupService(_clock);
        await backups.CreateAsync(
            new BackupRequest { OperationId = "op-48", Kind = OperationKind.Execute, Risk = RiskLevel.Medium },
            progress: null,
            CancellationToken.None);

        // Attempted, but the verification did not confirm the state (chapter 86).
        var rollback = new RecordingRollbackService { CanRollback = true, Attempted = true, Verified = false };
        var engine = Engine(backups, rollback, journal);

        var result = await engine.RecoverAsync(
            new RecoveryRequest
            {
                Approval = new ApprovalRecord
                {
                    RequestId = "req-3",
                    OperationId = engine.ApprovalOperationId("BKP-TEST-001"),
                    Decision = ApprovalDecision.Approved,
                    DecidedAt = _clock.Now,
                },
            },
            progress: null,
            CancellationToken.None);

        Assert.True(result.Attempted);
        Assert.False(result.Verified);
        // The summary comes from the rollback service; what counts here is that the recovery did not
        // turn an unverified rollback into a success (chapter 86).
        Assert.Equal("Rollback_Summary_Partial", result.Summary.Key);
        Assert.Single(rollback.Requests);
    }

    /// <summary>
    /// SEC-12: a state journal that was changed after the fact stays readable, but the assessment
    /// carries the finding. A recovery that is planned on a manipulated record has to be recognisable
    /// as such.
    /// </summary>
    [Fact]
    public async Task A_changed_journal_is_reported_with_the_assessment()
    {
        using var paths = new TempPathProvider();
        var journal = new WindowsMaintenanceCenter.Infrastructure.Persistence.FileStateJournal(paths);
        var machine = new SystemStateMachine(_clock, events: null, logger: null, journal: journal);
        machine.TryTransitionTo(SystemState.Discovery, "start");
        machine.TryTransitionTo(SystemState.Diagnostic, "assess");
        machine.TryTransitionTo(SystemState.PlanGenerated, "plan");

        var lines = File.ReadAllLines(journal.Location);
        File.WriteAllLines(journal.Location, lines.Where((_, index) => index != 1));

        var engine = Engine(new RecordingBackupService(_clock), new RecordingRollbackService(), new WindowsMaintenanceCenter.Infrastructure.Persistence.FileStateJournal(paths));
        var assessment = await engine.AssessAsync(CancellationToken.None);

        Assert.False(assessment.JournalIntact);
        Assert.NotNull(assessment.JournalFinding);
        Assert.Contains(assessment.JournalEvidence, line => line.Contains("chain broken", StringComparison.Ordinal));
    }

    private RecoveryEngine Engine(RecordingBackupService backups, RecordingRollbackService rollback, IStateJournal? journal = null) =>
        new(journal ?? new InMemoryStateJournal(), backups, rollback);

    /// <summary>
    /// Builds a journal that ends in EXECUTING - the state a crash during an action leaves behind.
    /// </summary>
    private IStateJournal Interrupted(string operationId, string actionId)
    {
        var journal = new InMemoryStateJournal();
        var machine = new SystemStateMachine(_clock, events: null, logger: null, journal: journal);
        machine.BeginOperation(operationId, actionId);
        machine.TryTransitionTo(SystemState.Discovery, "start");
        machine.TryTransitionTo(SystemState.Diagnostic, "assess");
        machine.TryTransitionTo(SystemState.PlanGenerated, "plan");
        machine.TryTransitionTo(SystemState.AwaitingApproval, "ask");
        machine.TryTransitionTo(SystemState.Backup, "secure");
        machine.TryTransitionTo(SystemState.Executing, "run");
        return journal;
    }
}
