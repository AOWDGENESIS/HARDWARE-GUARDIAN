using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>
/// The phases of the one-click maintenance run (chapter 33, module M27).
///
/// The order is the specification's order and is not a suggestion: every phase is a step of the same
/// list, and a phase that did not run is reported as <see cref="StageOutcome.NotRun"/> or
/// <see cref="StageOutcome.Skipped"/> - never left out. That is what makes the list checkable: a reader
/// compares it with the eight phases and sees at once what happened.
/// </summary>
public enum OneClickPhase
{
    Discovery = 0,
    Diagnostic = 1,
    Plan = 2,
    Approval = 3,
    Backup = 4,
    Execution = 5,
    Validation = 6,
    Report = 7,
}

/// <summary>
/// One phase of the run with everything a reader needs to judge it: what was done, why, how risky it
/// was and what came out. M27-S-001 ("no hidden actions") is nothing other than this record: every
/// phase and every planned item appears here, and the list is written to the protocol, the audit log
/// and the report - so an action that nobody can find in the list cannot happen.
/// </summary>
public sealed record OneClickStep
{
    public OneClickPhase Phase { get; init; }

    /// <summary>
    /// True for the eight phase steps, false for the single lines that name a planned item. The
    /// distinction matters twice: it keeps a phase from appearing twice in the list, and it lets the
    /// interface show the phases as headings with their planned items underneath.
    /// </summary>
    public bool IsPhaseStep { get; init; }

    /// <summary>What this phase does - the user-visible description, never an identifier.</summary>
    public LocalizedText What { get; init; } = LocalizedText.Of("OneClick_Unknown_What");

    /// <summary>Why the phase runs (M27-S-003 wants the risk next to the reason).</summary>
    public LocalizedText Why { get; init; } = LocalizedText.Of("OneClick_Unknown_Why");

    /// <summary>
    /// Risk of this phase. <c>null</c> means "not stated" - the enumeration has no UNKNOWN member, and
    /// inventing one here would make every missing statement look like a classification (chapter 86).
    /// </summary>
    public RiskLevel? Risk { get; init; }

    public StageOutcome Outcome { get; init; } = StageOutcome.NotRun;

    /// <summary>
    /// Evidence or the reason for the outcome, as text. A phase that was blocked says why; a phase
    /// that succeeded says what was measured. Never a bare "OK" (chapter 86).
    /// </summary>
    public string? Detail { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>
/// A value that was measured before and after the run (M27-F-001). When one of the two readings is
/// missing the delta stays <c>UNKNOWN</c> - a difference that was not measured must not appear as 0.
/// </summary>
public sealed record OneClickMeasurement
{
    /// <summary>Technical identifier (for logs and CSV), e.g. <c>freeSpace</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Localisation key of the measurement's name, so the interface never shows an identifier.</summary>
    public string DisplayNameKey { get; init; } = "OneClick_Measurement_Unknown";

    public Measured<long> Before { get; init; } = Measured<long>.NotAvailable("not measured before the run");

    public Measured<long> After { get; init; } = Measured<long>.NotAvailable("not measured after the run");

    /// <summary>
    /// After minus before, or UNKNOWN when either side is unknown. The difference is computed, not
    /// measured - its origin says so (<c>DataSource.Derived</c> if that value exists, otherwise the
    /// origin of the "after" reading), and a missing reading never becomes a zero.
    /// </summary>
    public Measured<long> Delta => Before.HasValue && After.HasValue
        ? Measured<long>.Known(After.Value!.Value - Before.Value!.Value, After.Origin)
        : Measured<long>.NotAvailable(
            Before.HasValue ? "the value after the run was not measured" : "the value before the run was not measured");

    public string Unit { get; init; } = string.Empty;

    /// <summary>Where the reading came from (class, counter, file), so it can be reproduced.</summary>
    public string Source { get; init; } = string.Empty;
}

/// <summary>What the user asked for. Everything here is a choice the user can make (M27-S-002).</summary>
public sealed record OneClickRequest
{
    /// <summary>
    /// Categories to work on. <c>null</c> means "everything the scan found that is safe to clean";
    /// an empty list means "nothing" - the run then reports that it did nothing instead of guessing.
    /// </summary>
    public IReadOnlyCollection<MaintenanceCategory>? Selection { get; init; }

    public MaintenanceScanOptions Scan { get; init; } = new();

    /// <summary>
    /// Protected and optional categories only run when this is set. It is the user's decision
    /// (chapter 18), which is why the plan lists them as excluded with the reason.
    /// </summary>
    public bool IncludeProtectedCategories { get; init; }

    /// <summary>
    /// Stops after PLAN. Then nothing is changed, no approval is asked for and no backup is made -
    /// useful for a caller that only wants to show what a run would do.
    /// </summary>
    public bool PlanOnly { get; init; }

    public ReportFormat ReportFormat { get; init; } = ReportFormat.Html;

    /// <summary>Report options. <c>null</c> means the application defaults (masking stays on).</summary>
    public ReportOptions? Report { get; init; }

    /// <summary>Free text that ends up in the report, so a run can be told apart later.</summary>
    public string? Note { get; init; }
}

/// <summary>The plan of a one-click run: what would happen, and what the user left out.</summary>
public sealed record OneClickPlan
{
    public string PlanId { get; init; } = string.Empty;

    public MaintenancePlan? Maintenance { get; init; }

    public IReadOnlyList<MaintenanceCategory> Selected { get; init; } = Array.Empty<MaintenanceCategory>();

    /// <summary>Categories the user deselected (M27-S-002). Reported, so the omission is visible.</summary>
    public IReadOnlyList<MaintenanceCategory> ExcludedByUser { get; init; } = Array.Empty<MaintenanceCategory>();

    /// <summary>
    /// Categories left out for a reason of their own: protected, unknown, without a usable location,
    /// or needing administrator rights this session does not have. The reason is in the run's findings.
    /// </summary>
    public IReadOnlyList<MaintenanceCategory> ExcludedByPolicy { get; init; } = Array.Empty<MaintenanceCategory>();

    /// <summary>Highest risk among the planned items; <c>null</c> when no plan exists.</summary>
    public RiskLevel? Risk { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Maintenance_Plan_Summary");
}

/// <summary>The result of a run. <see cref="OpenPoints"/> carries everything that was not done.</summary>
public sealed record OneClickResult
{
    public string OperationId { get; init; } = string.Empty;

    public OneClickPlan Plan { get; init; } = new();

    public IReadOnlyList<OneClickStep> Steps { get; init; } = Array.Empty<OneClickStep>();

    public IReadOnlyList<OneClickMeasurement> Measurements { get; init; } = Array.Empty<OneClickMeasurement>();

    public MaintenanceResult? Execution { get; init; }

    public BackupRecord? Backup { get; init; }

    public ApprovalRecord? Approval { get; init; }

    public ReportArtifact? Report { get; init; }

    /// <summary>
    /// The state the machine ended in. This is the honest verdict: <see cref="SystemState.Success"/>
    /// only when execution and validation both happened, <see cref="SystemState.Blocked"/> when the
    /// run stopped for a reason, <see cref="SystemState.Cancelled"/> when the user stopped it.
    /// </summary>
    public SystemState FinalState { get; init; } = SystemState.Blocked;

    public LocalizedText Summary { get; init; } = LocalizedText.Of("OneClick_Result_NotRun");

    /// <summary>What was skipped or could not be proven, each with its reason. Never empty without a reason.</summary>
    public IReadOnlyList<string> OpenPoints { get; init; } = Array.Empty<string>();

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset CompletedAt { get; init; }
}
