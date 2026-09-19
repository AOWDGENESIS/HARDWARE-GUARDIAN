using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Abstractions;

/// <summary>Problem centre backing store (spec sections 23 and 67).</summary>
public interface IProblemRegistry
{
    event EventHandler<Problem>? ProblemRegistered;

    event EventHandler<Problem>? ProblemUpdated;

    event EventHandler? Cleared;

    IReadOnlyList<Problem> All { get; }

    ProblemCounts Counts { get; }

    Problem Add(ProblemDraft draft);

    void AddRange(IEnumerable<ProblemDraft> drafts);

    void RegisterRange(IEnumerable<Problem> problems);

    void UpdateStatus(string problemId, ProblemStatus status, string? note = null);

    Problem? Find(string problemId);

    void Clear();

    /// <summary>All problems that were detected for a specific component.</summary>
    IReadOnlyList<Problem> ForComponent(string componentId);
}

/// <summary>Persisted user settings (spec sections 40 and 72).</summary>
public interface ISettingsService
{
    event EventHandler<AppSettings>? Changed;

    AppSettings Current { get; }

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);

    Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> mutator, CancellationToken cancellationToken);
}

/// <summary>History of diagnostics, maintenance and changes (spec section 66).</summary>
public interface IHistoryStore
{
    string Location { get; }

    Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<HistoryEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken);

    Task<HistoryEntry?> LatestAsync(CancellationToken cancellationToken);
}

/// <summary>Durable sink for audit entries. JSON lines plus a readable text log.</summary>
public interface IAuditSink
{
    string Location { get; }

    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> ReadAsync(int maxEntries, CancellationToken cancellationToken);
}

/// <summary>
/// Audit log (spec section 27). Every change is recorded with before/after state, approval,
/// result, error and rollback information.
/// </summary>
public interface IAuditLog
{
    string Location { get; }

    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);

    Task<AuditEntry> RecordAsync(
        OperationKind operation,
        string operationKey,
        ComponentCategory category,
        StageOutcome result,
        string? componentId = null,
        string? oldState = null,
        string? newState = null,
        ManufacturerSourceRef? source = null,
        ApprovalRecord? approval = null,
        BackupRecord? backup = null,
        RollbackResult? rollback = null,
        string? error = null,
        IReadOnlyList<string>? evidence = null,
        CancellationToken cancellationToken = default);

    IReadOnlyList<AuditEntry> Recent(int maxEntries = 100);
}

/// <summary>
/// Approval workflow (spec section 24). The service never auto approves high or critical risk
/// operations, regardless of the configured policy.
/// </summary>
public interface IApprovalService
{
    event EventHandler<ApprovalRequest>? ApprovalRequested;

    event EventHandler<ApprovalRequest>? ApprovalDecided;

    IReadOnlyList<ApprovalRecord> History { get; }

    bool IsConfirmationRequired(RiskLevel risk);

    Task<ApprovalRequest> CreateAsync(ApprovalRequestDraft draft, CancellationToken cancellationToken);

    Task<ApprovalRequest> WaitForDecisionAsync(string requestId, CancellationToken cancellationToken);

    void Decide(string requestId, ApprovalDecision decision, string? note = null);

    /// <summary>
    /// Requests approval and returns the record. Throws <see cref="OperationBlockedException"/>
    /// when the operation is blocked or the user rejected it.
    /// </summary>
    Task<ApprovalRecord> EnsureApprovedAsync(ApprovalRequestDraft draft, CancellationToken cancellationToken);
}

/// <summary>Raised when an operation must not continue (fail closed, spec section 1.2).</summary>
public sealed class OperationBlockedException : Exception
{
    public OperationBlockedException(string reasonCode, LocalizedText reason, string? detail = null)
        : base($"Operation blocked: {reasonCode} ({reason.Key})")
    {
        ReasonCode = reasonCode;
        Reason = reason;
        Detail = detail;
    }

    public string ReasonCode { get; }

    public LocalizedText Reason { get; }

    public string? Detail { get; }
}

/// <summary>Persists the raw snapshots that reports and history are built from.</summary>
public interface ISnapshotStore
{
    string Location { get; }

    Task SaveAsync(SystemSnapshot snapshot, CancellationToken cancellationToken);

    Task<SystemSnapshot?> LoadLatestAsync(CancellationToken cancellationToken);

    Task<SystemSnapshot?> LoadAsync(string path, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListAsync(int maxEntries, CancellationToken cancellationToken);
}
