using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// Recovery after an interrupted run (spec section 41, M35).
///
/// What it does: it reads the state journal, decides whether the previous run was cut off in the
/// middle, names the operation and the action that was running, looks for the backup of that
/// operation and asks the rollback service whether that backup can be restored. That is M35-F-001
/// (last action), M35-F-002 (backup state) and M35-F-003 (rollback possibility) - each of them
/// answered from records, never from a guess.
///
/// What it deliberately cannot do: recover on its own. M35-S-001 forbids a risky automatic recovery
/// without approval, so <see cref="RecoverAsync"/> refuses with
/// <see cref="BlockedReasonCodes.RecoveryApprovalRequired"/> unless the caller hands in an approval
/// for exactly this backup. A refused attempt changes nothing and is audited, because "nothing
/// happened" has to be provable too.
/// </summary>
public interface IRecoveryEngine
{
    /// <summary>What the previous run left behind. Read only; it changes nothing.</summary>
    Task<RecoveryAssessment> AssessAsync(CancellationToken cancellationToken);

    /// <summary>Restores the interrupted operation - only with an approval for exactly that backup.</summary>
    Task<RecoveryResult> RecoverAsync(
        RecoveryRequest request,
        IProgress<ProgressSnapshot>? progress,
        CancellationToken cancellationToken);

    /// <summary>Operation identifier an approval has to carry to authorise the recovery of a backup.</summary>
    string ApprovalOperationId(string backupRecordId);
}

/// <summary>Reason codes the recovery adds to the specification's block reason list.</summary>
public static class BlockedReasonCodes
{
    /// <summary>The state journal exists, but it does not end in an interruptible state.</summary>
    public const string RecoveryNotAvailable = "RECOVERY_NOT_AVAILABLE";

    /// <summary>A recovery was requested without an approval for exactly this backup (M35-S-001).</summary>
    public const string RecoveryApprovalRequired = "RECOVERY_APPROVAL_REQUIRED";
}

/// <summary>Standard implementation, built on the journal, the backup service and the rollback service.</summary>
public sealed class RecoveryEngine : IRecoveryEngine
{
    private readonly IStateJournal _journal;
    private readonly IBackupService _backups;
    private readonly IRollbackService _rollback;
    private readonly IAuditLog? _audit;

    public RecoveryEngine(
        IStateJournal journal,
        IBackupService backups,
        IRollbackService rollback,
        IAuditLog? audit = null)
    {
        _journal = journal;
        _backups = backups;
        _rollback = rollback;
        _audit = audit;
    }

    public string ApprovalOperationId(string backupRecordId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupRecordId);
        return $"Recovery:{backupRecordId}";
    }

    public async Task<RecoveryAssessment> AssessAsync(CancellationToken cancellationToken)
    {
        var entries = _journal.Read();
        if (entries.Count == 0)
        {
            return new RecoveryAssessment
            {
                RecoveryAvailable = false,
                Summary = LocalizedText.Of("Recovery_Reason_NothingRecorded"),
                Evidence = new[] { $"journal={_journal.Location}, entries=0" },
            };
        }

        var last = entries[^1];
        var evidence = new List<string>
        {
            $"journal={_journal.Location}, entries={entries.Count}",
            $"last change {last.From} -> {last.To} at {last.At:u}",
        };

        if (!StateJournalEntry.IsInterruptible(last.To))
        {
            // A finished run: SUCCESS, ERROR, BLOCKED, CANCELLED and the states before a change all
            // mean nothing was left half done.
            return new RecoveryAssessment
            {
                RecoveryAvailable = false,
                LastState = last.To,
                LastChangeAt = last.At,
                Summary = LocalizedText.Of("Recovery_Reason_NoInterruption", last.To.ToString()),
                Evidence = evidence,
            };
        }

        // The operation is taken from the last entry that names one: a transition that ends a run
        // (for example back to BLOCKED) does not have to carry it, the interrupted one does.
        var interrupted = entries.LastOrDefault(entry => !string.IsNullOrWhiteSpace(entry.OperationId)) ?? last;
        var operationId = interrupted.OperationId;
        if (!string.IsNullOrWhiteSpace(operationId))
        {
            evidence.Add($"operation={operationId}");
        }
        if (!string.IsNullOrWhiteSpace(interrupted.ActionId))
        {
            evidence.Add($"action={interrupted.ActionId}");
        }

        // M35-F-002: the backup of that operation, if one exists. An operation without an identifier
        // cannot be matched to a backup, and then the engine says so instead of picking any backup.
        BackupRecord? backup = null;
        if (!string.IsNullOrWhiteSpace(operationId))
        {
            var all = await _backups.ListAsync(cancellationToken).ConfigureAwait(false);
            backup = all
                .Where(record => string.Equals(record.OperationId, operationId, StringComparison.Ordinal))
                .OrderByDescending(record => record.CreatedAt)
                .FirstOrDefault();
        }
        else
        {
            evidence.Add("the interrupted entry names no operation, so no backup can be matched to it");
        }

        if (backup is null)
        {
            return new RecoveryAssessment
            {
                RecoveryAvailable = true,
                LastState = last.To,
                LastChangeAt = last.At,
                OperationId = operationId,
                ActionId = interrupted.ActionId,
                BackupFound = false,
                RollbackPossible = false,
                Summary = LocalizedText.Of("Recovery_Reason_BackupMissing"),
                Evidence = evidence,
            };
        }

        evidence.Add($"backup={backup.Id} created={backup.CreatedAt:u}");

        // M35-F-003: only the rollback service decides whether that backup can be restored; a backup
        // file that exists is not the same as a rollback that is possible.
        var rollbackPossible = await _rollback.CanRollbackAsync(backup.Id, cancellationToken).ConfigureAwait(false);
        evidence.Add($"rollback possible={rollbackPossible}");

        return new RecoveryAssessment
        {
            RecoveryAvailable = true,
            LastState = last.To,
            LastChangeAt = last.At,
            OperationId = operationId,
            ActionId = interrupted.ActionId,
            BackupRecordId = backup.Id,
            BackupFound = true,
            RollbackPossible = rollbackPossible,
            Summary = LocalizedText.Of(rollbackPossible ? "Recovery_Reason_Ready" : "Recovery_Reason_RollbackNotPossible"),
            Evidence = evidence,
        };
    }

    public async Task<RecoveryResult> RecoverAsync(
        RecoveryRequest request,
        IProgress<ProgressSnapshot>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var assessment = await AssessAsync(cancellationToken).ConfigureAwait(false);

        if (!assessment.RecoveryAvailable)
        {
            return await RefuseAsync(
                assessment,
                "Recovery_Blocked_NothingToRecover",
                BlockedReasonCodes.RecoveryNotAvailable,
                request,
                cancellationToken).ConfigureAwait(false);
        }

        // M35-S-001: no risky automatic recovery without approval. The approval has to name exactly
        // this backup; an approval the user gave for another operation is not a permission here.
        var expected = assessment.BackupRecordId is null ? null : ApprovalOperationId(assessment.BackupRecordId);
        if (request.Approval is null
            || request.Approval.Decision != ApprovalDecision.Approved
            || !string.Equals(request.Approval.OperationId, expected, StringComparison.Ordinal))
        {
            return await RefuseAsync(
                assessment,
                "Recovery_Blocked_ApprovalRequired",
                BlockedReasonCodes.RecoveryApprovalRequired,
                request,
                cancellationToken).ConfigureAwait(false);
        }

        if (!assessment.RollbackPossible || assessment.BackupRecordId is null)
        {
            // Fail closed: without a restorable backup nothing is attempted, and the reason says which
            // part was missing rather than letting a half recovery look like a try.
            return await RefuseAsync(
                assessment,
                "Recovery_Blocked_RollbackNotPossible",
                BlockedReasonCodes.RecoveryNotAvailable,
                request,
                cancellationToken).ConfigureAwait(false);
        }

        var rollback = await _rollback.RollbackAsync(
            new RollbackRequest
            {
                BackupRecordId = assessment.BackupRecordId,
                Operation = OperationKind.Rollback,
                OperationKey = request.OperationKey,
                Reason = request.Reason,
                Approval = request.Approval,
            },
            progress,
            cancellationToken).ConfigureAwait(false);

        if (_audit is not null)
        {
            await _audit.RecordAsync(
                OperationKind.Rollback,
                request.OperationKey,
                ComponentCategory.Unknown,
                rollback.Verified ? StageOutcome.Succeeded : StageOutcome.Failed,
                componentId: assessment.BackupRecordId,
                approval: request.Approval,
                rollback: rollback,
                error: rollback.ErrorDetail,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return new RecoveryResult
        {
            BackupRecordId = assessment.BackupRecordId,
            Attempted = rollback.Attempted,
            // Chapter 86: success only after EXECUTION and VALIDATION. A rollback that ran but was not
            // confirmed stays "not verified", whatever its own summary says.
            Verified = rollback.Attempted && rollback.Verified,
            Summary = rollback.Summary,
            Steps = rollback.Steps,
            ErrorDetail = rollback.ErrorDetail,
        };
    }

    private async Task<RecoveryResult> RefuseAsync(
        RecoveryAssessment assessment,
        string summaryKey,
        string reasonCode,
        RecoveryRequest request,
        CancellationToken cancellationToken)
    {
        if (_audit is not null)
        {
            await _audit.RecordAsync(
                OperationKind.Rollback,
                request.OperationKey,
                ComponentCategory.Unknown,
                StageOutcome.Blocked,
                componentId: assessment.BackupRecordId,
                approval: request.Approval,
                error: reasonCode,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return new RecoveryResult
        {
            BackupRecordId = assessment.BackupRecordId,
            Attempted = false,
            Verified = false,
            BlockedReasonCode = reasonCode,
            Summary = LocalizedText.Of(summaryKey),
        };
    }
}
