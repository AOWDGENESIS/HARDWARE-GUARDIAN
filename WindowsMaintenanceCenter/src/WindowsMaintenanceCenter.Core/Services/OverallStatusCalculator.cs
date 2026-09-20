using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// Derives the overall status from the detected problems and module results. Deliberately no
/// numeric score: the status is a category with a documented derivation (spec sections 32, 92).
/// </summary>
public static class OverallStatusCalculator
{
    public static HealthStatus Calculate(IEnumerable<Problem> problems, IEnumerable<ModuleResult> modules, out LocalizedText summary)
    {
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentNullException.ThrowIfNull(modules);

        var problemList = problems.ToList();
        var moduleList = modules.ToList();

        var critical = problemList.Count(p => p.Severity == Severity.Critical);
        var errors = problemList.Count(p => p.Severity == Severity.Error);
        var warnings = problemList.Count(p => p.Severity is Severity.Warning or Severity.Blocked);

        var skipped = moduleList.Count(m => m.WasSkipped);
        var unknownChecks = moduleList.Count(m => m.Status == HealthStatus.Unknown && !m.WasSkipped);

        HealthStatus status;
        if (critical > 0)
        {
            status = HealthStatus.Critical;
        }
        else if (errors > 0)
        {
            status = HealthStatus.Warning;
        }
        else if (warnings > 0)
        {
            status = HealthStatus.Attention;
        }
        else if (moduleList.Count == 0 || unknownChecks == moduleList.Count)
        {
            status = HealthStatus.Unknown;
        }
        else
        {
            status = HealthStatus.Healthy;
        }

        summary = status switch
        {
            HealthStatus.Critical => LocalizedText.Of("Overall_Critical", critical),
            HealthStatus.Warning => LocalizedText.Of("Overall_Warning", errors),
            HealthStatus.Attention => LocalizedText.Of("Overall_Attention", warnings),
            HealthStatus.Healthy => skipped > 0
                ? LocalizedText.Of("Overall_Healthy_WithSkips", skipped)
                : LocalizedText.Of("Overall_Healthy"),
            _ => LocalizedText.Of("Overall_Unknown"),
        };

        return status;
    }

    /// <summary>Worst case of a set of statuses; unknown is weaker than healthy.</summary>
    public static HealthStatus Worst(IEnumerable<HealthStatus> statuses)
    {
        var list = statuses.ToList();
        if (list.Count == 0)
        {
            return HealthStatus.Unknown;
        }

        if (list.Any(s => s == HealthStatus.Critical))
        {
            return HealthStatus.Critical;
        }

        if (list.Any(s => s == HealthStatus.Warning))
        {
            return HealthStatus.Warning;
        }

        if (list.Any(s => s == HealthStatus.Attention))
        {
            return HealthStatus.Attention;
        }

        return list.Any(s => s == HealthStatus.Healthy) ? HealthStatus.Healthy : HealthStatus.Unknown;
    }

    public static HealthStatus FromSeverity(Severity severity) => severity switch
    {
        Severity.Critical => HealthStatus.Critical,
        Severity.Error => HealthStatus.Warning,
        Severity.Warning => HealthStatus.Attention,
        Severity.Blocked => HealthStatus.Attention,
        Severity.Success => HealthStatus.Healthy,
        _ => HealthStatus.Unknown,
    };

    public static string HealthKey(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "Health_Healthy",
        HealthStatus.Attention => "Health_Attention",
        HealthStatus.Warning => "Health_Warning",
        HealthStatus.Critical => "Health_Critical",
        _ => "Health_Unknown",
    };
}
