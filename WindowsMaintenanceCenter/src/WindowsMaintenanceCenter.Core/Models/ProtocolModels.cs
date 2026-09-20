using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>Machine readable audit entry (JSON) plus its human readable twin (TXT), spec section 27.</summary>
public sealed record AuditEntry
{
    public string Id { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }

    /// <summary>User name. Local audit only - it is never transmitted (spec section 28).</summary>
    public string User { get; init; } = string.Empty;

    public string ApplicationVersion { get; init; } = string.Empty;

    public string MachineName { get; init; } = string.Empty;

    public OperationKind Operation { get; init; } = OperationKind.Execute;

    public string OperationKey { get; init; } = "Audit_Operation_Unknown";

    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    public string? ComponentId { get; init; }

    public string? OldState { get; init; }

    public string? NewState { get; init; }

    public ManufacturerSourceRef Source { get; init; } = ManufacturerSourceRef.Unknown();

    public ApprovalRecord? Approval { get; init; }

    public StageOutcome Result { get; init; } = StageOutcome.NotRun;

    public string? Error { get; init; }

    public BackupRecord? Backup { get; init; }

    public RollbackResult? Rollback { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>Draft of an approval request. Built by the action, not by the UI.</summary>
public sealed record ApprovalRequestDraft
{
    public string OperationId { get; init; } = string.Empty;

    public OperationKind Operation { get; init; } = OperationKind.Execute;

    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    public string? ComponentId { get; init; }

    public LocalizedText Action { get; init; } = LocalizedText.Of("Approval_Unknown_Action");

    /// <summary>"What will happen".</summary>
    public LocalizedText What { get; init; } = LocalizedText.Of("Approval_Unknown_What");

    public IReadOnlyList<LocalizedText> Steps { get; init; } = Array.Empty<LocalizedText>();

    /// <summary>"Why".</summary>
    public LocalizedText Why { get; init; } = LocalizedText.Of("Approval_Unknown_Why");

    /// <summary>"Source" - must state exactly which source was used, or that none was verified.</summary>
    public ManufacturerSourceRef Source { get; init; } = ManufacturerSourceRef.Unknown();

    public LocalizedText SourceSummary { get; init; } = LocalizedText.Of("Approval_Source_Unknown");

    public BackupAvailability Backup { get; init; } = new();

    public RiskLevel Risk { get; init; } = RiskLevel.Medium;

    public LocalizedText RiskSummary { get; init; } = LocalizedText.Of("Approval_Risk_Unknown");

    /// <summary>Before/after preview (spec section 79).</summary>
    public IReadOnlyList<ChangePreview> Preview { get; init; } = Array.Empty<ChangePreview>();

    /// <summary>Set when the operation must be refused regardless of user consent.</summary>
    public string? BlockedReasonCode { get; init; }

    public LocalizedText? BlockedReason { get; init; }

    public bool RequiresAdministrator { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>An approval request that is waiting for (or has received) a decision.</summary>
public sealed record ApprovalRequest
{
    public string RequestId { get; init; } = string.Empty;

    public ApprovalRequestDraft Draft { get; init; } = new();

    public ApprovalDecision Decision { get; init; } = ApprovalDecision.Pending;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; init; }

    public string? Note { get; init; }

    public bool IsPending => Decision == ApprovalDecision.Pending;
}

/// <summary>Immutable record of an approval decision, referenced from the audit log.</summary>
public sealed record ApprovalRecord
{
    public string RequestId { get; init; } = string.Empty;

    public string OperationId { get; init; } = string.Empty;

    public ApprovalDecision Decision { get; init; } = ApprovalDecision.Pending;

    public DateTimeOffset DecidedAt { get; init; }

    public string? Note { get; init; }

    public RiskLevel Risk { get; init; } = RiskLevel.Medium;

    public bool WasRequired { get; init; } = true;
}

/// <summary>Before/after preview line shown to the user before anything changes.</summary>
public sealed record ChangePreview
{
    public string LabelKey { get; init; } = "Preview_Unknown";

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public LocalizedText Reason { get; init; } = LocalizedText.Of("Preview_Reason_Unknown");

    public string? SourceToken { get; init; }

    public RiskLevel Risk { get; init; } = RiskLevel.Low;
}

/// <summary>One line of the live protocol (spec section 20).</summary>
public sealed record ProtocolEntry
{
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Short module token, e.g. <c>CPU</c>, <c>BIOS</c>, <c>CHIPSET</c>, <c>DRIVER</c>.</summary>
    public string Module { get; init; } = "SYS";

    public LocalizedText Action { get; init; } = LocalizedText.Of("Protocol_Unknown");

    public Severity Severity { get; init; } = Severity.Info;

    /// <summary>Technical detail (data, not prose).</summary>
    public string? Detail { get; init; }

    public long Sequence { get; init; }

    public string SeverityKey => Severity switch
    {
        Severity.Info => "Severity_Info",
        Severity.Success => "Severity_Success",
        Severity.Warning => "Severity_Warning",
        Severity.Error => "Severity_Error",
        Severity.Critical => "Severity_Critical",
        Severity.Blocked => "Severity_Blocked",
        _ => "Severity_Info",
    };

    public bool NeedsAttention => Severity is Severity.Error or Severity.Critical or Severity.Blocked;
}

/// <summary>Progress snapshot for long running operations (spec section 21).</summary>
public sealed record ProgressSnapshot
{
    public string OperationKey { get; init; } = string.Empty;

    public string Module { get; init; } = string.Empty;

    public bool IsRunning { get; init; }

    public double? Fraction { get; init; }

    public int CompletedSteps { get; init; }

    public int? TotalSteps { get; init; }

    public string? StepKey { get; init; }

    public string? StepDetail { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>Only set when the estimate is statistically defensible, otherwise <c>null</c>.</summary>
    public TimeSpan? EstimatedRemaining { get; init; }

    public bool? Success { get; init; }

    public double? PercentComplete => Fraction.HasValue ? Math.Round(Fraction.Value * 100d, 0) : null;

    public static ProgressSnapshot Idle { get; } = new()
    {
        IsRunning = false,
        OperationKey = "Progress_Idle",
    };
}
