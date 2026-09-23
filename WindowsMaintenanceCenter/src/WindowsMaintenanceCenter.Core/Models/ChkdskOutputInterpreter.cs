using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>Verdict from interpreting raw CHKDSK output (spec chapter 18, module M12).</summary>
public sealed record ChkdskInterpretation(
    bool ChangesPerformed,
    bool RepairSucceeded,
    bool RebootRequired,
    LocalizedText Summary);

/// <summary>
/// Interprets raw CHKDSK tool output into structured verdicts (spec chapter 18, module M12).
/// Pure function: does not execute processes, can be tested on any host platform.
/// </summary>
public static class ChkdskOutputInterpreter
{
    public static ChkdskInterpretation Interpret(
        string output,
        int exitCode,
        bool repair)
    {
        var text = output ?? string.Empty;
        var lower = text.ToLowerInvariant();

        var rebootPending = lower.Contains("restart") ||
                            lower.Contains("reboot") ||
                            lower.Contains("neustart") ||
                            lower.Contains("schedule") ||
                            lower.Contains("cannot lock current drive");

        if (rebootPending)
        {
            return new ChkdskInterpretation(false, false, true, LocalizedText.Of("Integrity_Summary_ChkdskRebootPending"));
        }

        var noProblemsFound = exitCode == 0 ||
                              lower.Contains("found no problems") ||
                              lower.Contains("keine probleme") ||
                              lower.Contains("volume is clean") ||
                              lower.Contains("no further action");

        if (noProblemsFound && exitCode == 0)
        {
            return new ChkdskInterpretation(false, true, false, LocalizedText.Of("Integrity_Summary_ChkdskClean"));
        }

        if (repair)
        {
            var repaired = exitCode == 0 ||
                           lower.Contains("has made corrections") ||
                           lower.Contains("korrekturen vorgenommen") ||
                           lower.Contains("repariert") ||
                           lower.Contains("fixed");

            if (repaired)
            {
                return new ChkdskInterpretation(true, true, false, LocalizedText.Of("Integrity_Summary_ChkdskRepairCompleted"));
            }

            return new ChkdskInterpretation(false, false, false, LocalizedText.Of("Integrity_Summary_ChkdskErrorsFound"));
        }

        return new ChkdskInterpretation(false, false, false, LocalizedText.Of("Integrity_Summary_ChkdskErrorsFound"));
    }
}
