using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>
/// What the recovery engine found after a restart (spec section 41, M35-F-001 to M35-F-003).
///
/// Every field answers one question and says so when it cannot: whether a job was interrupted, which
/// operation it was, at which state it was cut off, whether a backup exists for it and whether a
/// rollback is possible. "No backup found" is a result, not a failure of the assessment.
/// </summary>
public sealed record RecoveryAssessment
{
    /// <summary>True when the previous run ended in a state a crash can interrupt.</summary>
    public bool RecoveryAvailable { get; init; }

    /// <summary>State the last journal entry recorded.</summary>
    public SystemState? LastState { get; init; }

    public DateTimeOffset? LastChangeAt { get; init; }

    /// <summary>Operation that was running when the run was cut off (M35-F-001).</summary>
    public string? OperationId { get; init; }

    /// <summary>Registered action of that operation, when the caller named one.</summary>
    public string? ActionId { get; init; }

    /// <summary>Backup that belongs to the interrupted operation (M35-F-002).</summary>
    public string? BackupRecordId { get; init; }

    public bool BackupFound { get; init; }

    /// <summary>True when the rollback service confirms that this backup can be restored (M35-F-003).</summary>
    public bool RollbackPossible { get; init; }

    /// <summary>
    /// Always true: a recovery changes the system, so it needs an approval of its own (M35-S-001).
    /// The property exists so that no reader has to guess.
    /// </summary>
    public bool RequiresApproval { get; init; } = true;

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Recovery_Reason_NothingRecorded");

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>What the caller asks the recovery engine to do.</summary>
public sealed record RecoveryRequest
{
    public string OperationKey { get; init; } = "Recovery_Operation_Default";

    /// <summary>
    /// Approval for exactly this recovery. Without it nothing is restored (M35-S-001); the operation
    /// identifier of the approval has to be the one the assessment names
    /// (<c>Recovery:&lt;backup&gt;</c>), so an approval for something else never authorises a recovery.
    /// </summary>
    public ApprovalRecord? Approval { get; init; }

    public LocalizedText Reason { get; init; } = LocalizedText.Of("Recovery_Reason_InterruptedJob");
}

/// <summary>Result of a recovery attempt. Success only after execution <b>and</b> verification (chapter 86).</summary>
public sealed record RecoveryResult
{
    public string? BackupRecordId { get; init; }

    public bool Attempted { get; init; }

    public bool Verified { get; init; }

    /// <summary>Set when nothing was attempted, with the reason code of the specification.</summary>
    public string? BlockedReasonCode { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Recovery_NotAttempted");

    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();

    public string? ErrorDetail { get; init; }
}
