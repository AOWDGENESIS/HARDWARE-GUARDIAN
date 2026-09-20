using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>One clean-up candidate (a concrete file or a well defined set of files).</summary>
public sealed record MaintenanceItem
{
    public string Id { get; init; } = string.Empty;

    public MaintenanceCategory Category { get; init; }

    public SafetyClass SafetyClass { get; init; } = SafetyClass.Unknown;

    /// <summary>Localisation key of the category name.</summary>
    public string DisplayNameKey { get; init; } = "Maintenance_Unknown";

    public LocalizedText Description { get; init; } = LocalizedText.Of("Maintenance_Unknown_Description");

    /// <summary>Root that is being measured. Never a single file path of a user document.</summary>
    public string? RootPath { get; init; }

    public Measured<long> SizeBytes { get; init; } = Measured<long>.NotAvailable("not measured");

    public Measured<int> FileCount { get; init; } = Measured<int>.NotAvailable("not measured");

    public bool RequiresAdministrator { get; init; }

    public bool IsEnabledByDefault { get; init; }

    /// <summary>Set when the category cannot be cleaned safely or automatically.</summary>
    public string? ProtectionReasonCode { get; init; }

    public LocalizedText? ProtectionReason { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>Result of a maintenance scan (never modifies anything).</summary>
public sealed record MaintenanceScanResult
{
    public IReadOnlyList<MaintenanceItem> Items { get; init; } = Array.Empty<MaintenanceItem>();

    public Measured<long> TotalSizeBytes { get; init; } = Measured<long>.NotAvailable("not measured");

    public DateTimeOffset ScannedAt { get; init; }

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<Problem> Problems { get; init; } = Array.Empty<Problem>();
}

/// <summary>The exact plan including a per-item before/after preview (spec sections 78 and 79).</summary>
public sealed record MaintenancePlan
{
    public string PlanId { get; init; } = string.Empty;

    public ExecutionMode Mode { get; init; } = ExecutionMode.DryRun;

    public IReadOnlyList<MaintenancePlanItem> Items { get; init; } = Array.Empty<MaintenancePlanItem>();

    public Measured<long> TotalBytesToFree { get; init; } = Measured<long>.NotAvailable("not measured");

    public RiskLevel Risk { get; init; } = RiskLevel.Low;

    public bool RequiresAdministrator { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Id of the dry run that covered exactly this set of locations (spec section 78). The service
    /// fills it when the plan is built and refuses to execute a plan without it, so the mandatory
    /// dry run cannot be skipped by calling the service directly.
    /// </summary>
    public string? DryRunPlanId { get; init; }

    /// <summary>
    /// True when this plan touches something that must be secured before execution. Safe cache and
    /// temporary locations do not need one; optional and protected categories do (spec section 44).
    /// </summary>
    public bool BackupRequired { get; init; }

    /// <summary>Id of the backup record that covers this plan, or <c>null</c> when none is on record.</summary>
    public string? BackupRecordId { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Maintenance_Plan_Empty");
}

/// <summary>A single planned action with the reason and the expected change.</summary>
public sealed record MaintenancePlanItem
{
    public string ItemId { get; init; } = string.Empty;

    public MaintenanceCategory Category { get; init; }

    public string DisplayNameKey { get; init; } = "Maintenance_Unknown";

    public SafetyClass SafetyClass { get; init; } = SafetyClass.Unknown;

    public string? RootPath { get; init; }

    public Measured<long> SizeBytes { get; init; } = Measured<long>.NotAvailable("not measured");

    public Measured<int> FileCount { get; init; } = Measured<int>.NotAvailable("not measured");

    /// <summary>What will happen, in localised form (what the dialog shows as "what will change").</summary>
    public LocalizedText Change { get; init; } = LocalizedText.Of("Maintenance_Change_Unknown");

    public LocalizedText Reason { get; init; } = LocalizedText.Of("Maintenance_Reason_Unknown");

    public RiskLevel Risk { get; init; } = RiskLevel.Low;

    public bool IsProtected { get; init; }

    public bool IsSelected { get; init; }
}

/// <summary>Outcome of an executed (or dry run) maintenance plan, item by item.</summary>
public sealed record MaintenanceResult
{
    public string PlanId { get; init; } = string.Empty;

    public ExecutionMode Mode { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset CompletedAt { get; init; }

    public IReadOnlyList<MaintenanceItemResult> Items { get; init; } = Array.Empty<MaintenanceItemResult>();

    public Measured<long> FreedBytes { get; init; } = Measured<long>.NotAvailable("nothing deleted");

    public bool WasNothingDeleted => Mode == ExecutionMode.DryRun;

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Maintenance_Result_Empty");
}

/// <summary>Per-category result, including verification after deletion.</summary>
public sealed record MaintenanceItemResult
{
    public string ItemId { get; init; } = string.Empty;

    public MaintenanceCategory Category { get; init; }

    public string DisplayNameKey { get; init; } = "Maintenance_Unknown";

    public StageOutcome Outcome { get; init; } = StageOutcome.NotRun;

    public Measured<long> PlannedBytes { get; init; } = Measured<long>.NotAvailable("not measured");

    public Measured<long> FreedBytes { get; init; } = Measured<long>.NotAvailable("nothing deleted");

    public Measured<int> DeletedFiles { get; init; } = Measured<int>.NotAvailable("nothing deleted");

    public Measured<int> SkippedFiles { get; init; } = Measured<int>.NotAvailable("nothing skipped");

    public LocalizedText? Message { get; init; }

    /// <summary>Id of the backup/rollback record created for this item, when one was needed.</summary>
    public string? BackupRecordId { get; init; }
}

/// <summary>Workload detection result that gates optimisation suggestions (spec section 19).</summary>
public sealed record WorkloadAssessment
{
    public WorkloadProfile Profile { get; init; } = WorkloadProfile.Unknown;

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Workload_Unknown");

    public IReadOnlyList<DetectedWorkload> Detected { get; init; } = Array.Empty<DetectedWorkload>();

    public IReadOnlyList<OptimizationSuggestion> Suggestions { get; init; } = Array.Empty<OptimizationSuggestion>();
}

/// <summary>A workload component that was actually found on this machine.</summary>
public sealed record DetectedWorkload
{
    public string Id { get; init; } = string.Empty;

    public string DisplayNameKey { get; init; } = "Workload_Unknown";

    public bool Detected { get; init; }

    public TextInfo Evidence { get; init; }

    /// <summary>True when this workload must not be disturbed by an optimisation.</summary>
    public bool MustNotBeDisturbed { get; init; }
}

/// <summary>A single, explainable optimisation proposal. No automatic changes.</summary>
public sealed record OptimizationSuggestion
{
    public string Id { get; init; } = string.Empty;

    public string DisplayNameKey { get; init; } = "Optimization_Unknown";

    public LocalizedText What { get; init; } = LocalizedText.Of("Optimization_Unknown_What");

    public LocalizedText Why { get; init; } = LocalizedText.Of("Optimization_Unknown_Why");

    public LocalizedText Risk { get; init; } = LocalizedText.Of("Optimization_Unknown_Risk");

    public RiskLevel RiskLevel { get; init; } = RiskLevel.Medium;

    public bool RequiresAdministrator { get; init; }

    public bool IsReversible { get; init; }

    /// <summary>Set when the suggestion was refused because it would disturb a detected workload.</summary>
    public string? BlockedReasonCode { get; init; }

    public LocalizedText? BlockedReason { get; init; }
}
