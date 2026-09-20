using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>
/// A tracked problem with identifier, evidence and recommendation. There are no speculative
/// root causes: <see cref="Evidence"/> contains what was actually observed (spec sections 23, 67).
/// </summary>
public sealed record Problem
{
    /// <summary>Stable error identifier in the form of chapter 85, e.g. <c>WMC-UPDATE-0042</c>.</summary>
    public string Id { get; init; } = string.Empty;

    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    public Severity Severity { get; init; } = Severity.Info;

    public ProblemStatus Status { get; init; } = ProblemStatus.Open;

    public LocalizedText Title { get; init; } = LocalizedText.Of("Problem_Unknown_Title");

    public LocalizedText Description { get; init; } = LocalizedText.Of("Problem_Unknown_Description");

    /// <summary>Observed facts only - machine data, counts, codes. No guesses.</summary>
    public string Evidence { get; init; } = string.Empty;

    /// <summary>
    /// WHY the finding is reported. This is deliberately a text and not a diagnosis: when the data
    /// does not prove a cause, the default is the wording of chapter 87 - "the available data is not
    /// enough to determine the cause unambiguously" (WMC-SPEC 20/87).
    /// </summary>
    public LocalizedText Cause { get; init; } = LocalizedText.Of("Problem_Cause_NotDeterminable");

    public LocalizedText Impact { get; init; } = LocalizedText.Of("Problem_Unknown_Impact");

    public LocalizedText RecommendedAction { get; init; } = LocalizedText.Of("Problem_Unknown_Action");

    public DateTimeOffset DetectedAt { get; init; }

    public string? ComponentId { get; init; }

    public string? ComponentName { get; init; }

    /// <summary>Identifier of the action that can fix this problem, if one exists.</summary>
    public string? ActionId { get; init; }

    /// <summary>
    /// Where the LOG entry for this finding lives (chapter 85). Null when nothing was written - that
    /// is visible, instead of a file name that was made up.
    /// </summary>
    public string? LogReference { get; init; }

    /// <summary>Blocked operations that were refused because of this problem.</summary>
    public IReadOnlyList<BlockedOperation> BlockedOperations { get; init; } = Array.Empty<BlockedOperation>();

    /// <summary>Non-localised references (WMI class, source URL, registry path, command).</summary>
    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();

    /// <summary>True when the recommendation requires administrator rights.</summary>
    public bool RequiresAdministrator { get; init; }

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

    public string StatusKey => Status switch
    {
        ProblemStatus.Open => "ProblemStatus_Open",
        ProblemStatus.Acknowledged => "ProblemStatus_Acknowledged",
        ProblemStatus.Resolved => "ProblemStatus_Resolved",
        ProblemStatus.Blocked => "ProblemStatus_Blocked",
        ProblemStatus.Ignored => "ProblemStatus_Ignored",
        _ => "ProblemStatus_Open",
    };
}

/// <summary>An operation that was refused and why (fail closed, spec section 1.2).</summary>
public sealed record BlockedOperation
{
    public string OperationId { get; init; } = string.Empty;

    public LocalizedText Title { get; init; } = LocalizedText.Of("Blocked_Unknown");

    public LocalizedText Reason { get; init; } = LocalizedText.Of("Blocked_Unknown_Reason");

    /// <summary>Stable reason code from <see cref="BlockReasons"/>.</summary>
    public string ReasonCode { get; init; } = BlockReasons.HardwareMatchNotProven;

    /// <summary>Non-localised technical detail.</summary>
    public string? Detail { get; init; }

    public DateTimeOffset BlockedAt { get; init; }
}

/// <summary>Draft used to register a problem; the registry assigns the identifier and timestamp.</summary>
public sealed record ProblemDraft
{
    /// <summary>
    /// Optional: theme of the error identifier (chapter 85). Left empty the registry derives it from
    /// <see cref="Category"/>, so a finding cannot end up in a theme that does not fit it.
    /// </summary>
    public string IdPrefix { get; init; } = string.Empty;

    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    public Severity Severity { get; init; } = Severity.Info;

    public LocalizedText Title { get; init; } = LocalizedText.Of("Problem_Unknown_Title");

    public LocalizedText Description { get; init; } = LocalizedText.Of("Problem_Unknown_Description");

    public string Evidence { get; init; } = string.Empty;

    /// <summary>
    /// WHY the finding is reported (chapter 85). Defaults to the wording of chapter 87: the cause is
    /// not determinable from the data at hand. A caller that really has a proven cause sets it.
    /// </summary>
    public LocalizedText Cause { get; init; } = LocalizedText.Of("Problem_Cause_NotDeterminable");

    public LocalizedText Impact { get; init; } = LocalizedText.Of("Problem_Unknown_Impact");

    public LocalizedText RecommendedAction { get; init; } = LocalizedText.Of("Problem_Unknown_Action");

    public string? ComponentId { get; init; }

    public string? ComponentName { get; init; }

    public string? ActionId { get; init; }

    /// <summary>
    /// Where the LOG entry for this finding lives (chapter 85). Null when nothing was written, which
    /// is visible instead of being replaced by a made up file name.
    /// </summary>
    public string? LogReference { get; init; }

    public bool RequiresAdministrator { get; init; }

    public IReadOnlyList<string> References { get; init; } = Array.Empty<string>();

    public BlockedOperation? BlockedOperation { get; init; }
}

/// <summary>Counts shown in the problem centre header (spec section 23).</summary>
public sealed record ProblemCounts
{
    public int Critical { get; init; }

    public int Warnings { get; init; }

    public int Information { get; init; }

    public int Blocked { get; init; }

    public int Errors { get; init; }

    public int Total { get; init; }

    public static ProblemCounts From(IEnumerable<Problem> problems)
    {
        var list = problems as IReadOnlyCollection<Problem> ?? problems.ToList();
        return new ProblemCounts
        {
            Critical = list.Count(p => p.Severity == Severity.Critical),
            Errors = list.Count(p => p.Severity == Severity.Error),
            Warnings = list.Count(p => p.Severity == Severity.Warning),
            Information = list.Count(p => p.Severity == Severity.Info || p.Severity == Severity.Success),
            Blocked = list.Count(p => p.Severity == Severity.Blocked),
            Total = list.Count,
        };
    }
}
