using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>How an argument of a registered action has to look (chapter 32).</summary>
public enum ActionArgumentKind
{
    /// <summary>Free text without shell or path metacharacters.</summary>
    Text,

    /// <summary>A whole number, optionally with a sign.</summary>
    Number,

    /// <summary>A file or directory path. <c>..</c>, UNC and device paths are refused.</summary>
    Path,

    /// <summary>One value out of a fixed list.</summary>
    Choice,

    /// <summary>A switch: the value is <c>true</c> or <c>false</c> and is rendered by the action itself.</summary>
    Switch,
}

/// <summary>Declares one argument of a registered action and how it is validated.</summary>
public sealed record ActionArgumentSpec
{
    public string Name { get; init; } = string.Empty;

    public ActionArgumentKind Kind { get; init; } = ActionArgumentKind.Text;

    public bool Required { get; init; }

    public int MaxLength { get; init; } = 200;

    /// <summary>Permitted values for <see cref="ActionArgumentKind.Choice"/>.</summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = Array.Empty<string>();

    /// <summary>Only for <see cref="ActionArgumentKind.Path"/>: may the value contain spaces?</summary>
    public bool AllowSpaces { get; init; } = true;
}

/// <summary>
/// One action the application is allowed to perform (chapter 32). An action that is not registered
/// cannot be performed at all, and an action that is registered but not allowed is registered only
/// so that the interface can show it as "not available" (chapter 96).
/// </summary>
public sealed record RegisteredAction
{
    /// <summary>Stable identifier in the form <c>Cleanup.WindowsUpdateCache</c>.</summary>
    public string Id { get; init; } = string.Empty;

    public LocalizedText Description { get; init; } = LocalizedText.Of("Action_Unknown_Description");

    public RiskLevel Risk { get; init; } = RiskLevel.Medium;

    public bool RequiresAdmin { get; init; }

    public IReadOnlyList<ActionArgumentSpec> Arguments { get; init; } = Array.Empty<ActionArgumentSpec>();

    /// <summary>Timeout for the whole action (chapter 39).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// True when the action may only run with an approval record for exactly this action
    /// (chapter 30, order BACKUP → APPROVAL → EXECUTE of chapter 44).
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// True when the action may only run after the locations it touches were secured. The backup
    /// record has to belong to this action as well - an approval or a backup for something else is
    /// not a permission for this one.
    /// </summary>
    public bool RequiresBackup { get; init; }

    /// <summary>
    /// Identifier of the action that undoes this one, if one exists. Null means: this action cannot
    /// be undone, and that must be visible before it runs (chapter 30, M24-S-001).
    /// </summary>
    public string? RollbackActionId { get; init; }

    /// <summary>
    /// False for actions that are described but deliberately not executable in this version. The
    /// registry refuses them with <see cref="Values.BlockReasons.ActionNotAllowed"/>.
    /// </summary>
    public bool Allowed { get; init; } = true;

    /// <summary>
    /// True when the exit code of the action is itself the verification (for example
    /// <c>dism /scanhealth</c>). When it is false, a caller has to validate the result before the
    /// run may be called successful (chapter 86).
    /// </summary>
    public bool SelfValidating { get; init; }

    /// <summary>
    /// Exit codes beyond 0 that mean "the tool ran and answered". Some Windows tools report a finding
    /// with a code of their own (a scan that found something to repair). Such a code is not an error
    /// of the run, but it is not success either - the result marks it as
    /// <see cref="ActionExecutionResult.ReportedFindings"/>. An empty list means: only 0 counts as
    /// "ran".
    /// </summary>
    public IReadOnlyList<int> FindingExitCodes { get; init; } = Array.Empty<int>();

    /// <summary>Program to start. Only a program that is registered here can ever be started.</summary>
    public string Executable { get; init; } = string.Empty;

    /// <summary>
    /// Literal arguments of the action. Placeholders are written as <c>{Name}</c> and are replaced
    /// only with values that passed the validation of the matching <see cref="ActionArgumentSpec"/>.
    /// </summary>
    public IReadOnlyList<string> ArgumentTemplate { get; init; } = Array.Empty<string>();
}

/// <summary>A call of a registered action with the arguments the caller wants to hand in.</summary>
public sealed record ActionRequest
{
    public string ActionId { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> Arguments { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The approval this call is based on, if one is required.</summary>
    public ApprovalRecord? Approval { get; init; }

    /// <summary>The backup this call is based on, if one is required.</summary>
    public BackupRecord? Backup { get; init; }
}

/// <summary>
/// One refusal with its localisation key and the argument it belongs to. The registry answers in
/// keys, never in ready made sentences: the interface decides the language (chapter 37).
/// </summary>
public sealed record ActionValidationError
{
    /// <summary>Localisation key, for example <c>Action_Path_Traversal</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Name of the argument the refusal belongs to, empty when it is not about one argument.</summary>
    public string Argument { get; init; } = string.Empty;
}

/// <summary>The result of checking a request before anything is executed.</summary>
public sealed record ActionValidationResult
{
    public bool IsValid { get; init; }

    /// <summary>Machine readable reason, see <see cref="Values.BlockReasons"/>.</summary>
    public string ReasonCode { get; init; } = string.Empty;

    /// <summary>Every refusal with its own reason, so the interface can show all of them at once.</summary>
    public IReadOnlyList<ActionValidationError> Errors { get; init; } = Array.Empty<ActionValidationError>();

    public RegisteredAction? Action { get; init; }

    /// <summary>The resolved command line: executable plus arguments after substitution.</summary>
    public IReadOnlyList<string> ResolvedArguments { get; init; } = Array.Empty<string>();

    public static ActionValidationResult Invalid(string reasonCode, params ActionValidationError[] errors) => new()
    {
        IsValid = false,
        ReasonCode = reasonCode,
        Errors = errors,
    };
}

/// <summary>Outcome of an executed action.</summary>
public sealed record ActionExecutionResult
{
    public string ActionId { get; init; } = string.Empty;

    public StageOutcome Outcome { get; init; } = StageOutcome.NotRun;

    /// <summary>True only when the action ran and its exit code said "ok".</summary>
    public bool Succeeded { get; init; }

    /// <summary>
    /// True when the result was validated afterwards. Chapter 86 allows SUCCESS only after
    /// EXECUTION and VALIDATION, so a caller that claims success has to check this as well.
    /// </summary>
    public bool Validated { get; init; }

    public int? ExitCode { get; init; }

    /// <summary>
    /// True when the tool ran and its documented finding code says "there is something". The run
    /// itself is complete, the state is not. Chapter 88 forbids calling this a repair.
    /// </summary>
    public bool ReportedFindings { get; init; }

    public bool TimedOut { get; init; }

    /// <summary>True when the user declined the elevation prompt (M31-S-004).</summary>
    public bool ElevationCancelled { get; init; }

    public bool RequiresAdmin { get; init; }

    /// <summary>False when the output of the process could not be captured, with the reason in evidence.</summary>
    public bool OutputCaptured { get; init; } = true;

    public string Output { get; init; } = string.Empty;

    public string? ErrorDetail { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Action_Summary_NotRun");

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}
