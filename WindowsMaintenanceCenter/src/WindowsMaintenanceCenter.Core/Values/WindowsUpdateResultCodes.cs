namespace WindowsMaintenanceCenter.Core.Values;

/// <summary>Meaning of a result code that the Windows Update Agent reported.</summary>
public enum WindowsUpdateResultVerdict
{
    NotStarted,
    InProgress,
    Succeeded,
    SucceededWithErrors,
    Failed,
    Aborted,

    /// <summary>The agent reported a code that is not part of the documented set.</summary>
    Unknown,
}

/// <summary>
/// Maps the numeric result code of the Windows Update Agent (WUResultCode / OperationResultCode).
///
/// Documented values: 0 NotStarted, 1 InProgress, 2 Succeeded, 3 SucceededWithErrors, 4 Failed,
/// 5 Aborted. Anything else - including a missing code - is <see cref="WindowsUpdateResultVerdict.Unknown"/>
/// and never counts as success: rule 90 (UPDATE-E-001) forbids showing a failed update as successful,
/// and a code that nobody documented is exactly the case where a guess would be invisible.
///
/// This lives in Core so that the mapping is testable without the update agent; the Windows layer
/// only supplies the number it parsed.
/// </summary>
public static class WindowsUpdateResultCodes
{
    public static WindowsUpdateResultVerdict Interpret(int resultCode) => resultCode switch
    {
        0 => WindowsUpdateResultVerdict.NotStarted,
        1 => WindowsUpdateResultVerdict.InProgress,
        2 => WindowsUpdateResultVerdict.Succeeded,
        3 => WindowsUpdateResultVerdict.SucceededWithErrors,
        4 => WindowsUpdateResultVerdict.Failed,
        5 => WindowsUpdateResultVerdict.Aborted,
        _ => WindowsUpdateResultVerdict.Unknown,
    };

    /// <summary>
    /// True only for a completion the agent called successful. A partial success stays visible as
    /// "succeeded with errors" and is reported as a warning, not as a clean result.
    /// </summary>
    public static bool IsSuccess(WindowsUpdateResultVerdict verdict) =>
        verdict is WindowsUpdateResultVerdict.Succeeded or WindowsUpdateResultVerdict.SucceededWithErrors;

    /// <summary>True when the agent was still working or never started, so nothing may be claimed.</summary>
    public static bool IsIncomplete(WindowsUpdateResultVerdict verdict) =>
        verdict is WindowsUpdateResultVerdict.NotStarted or WindowsUpdateResultVerdict.InProgress;
}
