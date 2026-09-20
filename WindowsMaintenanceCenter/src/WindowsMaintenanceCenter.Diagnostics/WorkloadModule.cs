using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Diagnostics;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Diagnostics;

/// <summary>
/// Workload detection (spec section 46). Maintenance must not disturb a running workload, so each
/// detected workload becomes a visible warning - not an automatic exception.
/// </summary>
public sealed class WorkloadModule : DiagnosticModuleBase
{
    public WorkloadModule(IClock clock) : base(clock)
    {
    }

    public override string Id => "WORKLOAD";

    public override string DisplayNameKey => "Module_Workload";

    public override ComponentCategory Category => ComponentCategory.Maintenance;

    protected override string ProtocolModule => "WORKLOAD";

    protected override async Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var detector = context.GetRequiredService<IWorkloadDetector>();
        var assessment = await detector.DetectAsync(cancellationToken).ConfigureAwait(false);

        var problems = new List<ProblemDraft>();
        var evidence = new List<string>
        {
            $"profile={assessment.Profile}",
            assessment.Summary.Key,
        };

        var detected = assessment.Detected.Where(d => d.Detected).ToList();
        foreach (var workload in detected)
        {
            evidence.Add($"{workload.Id}: {workload.Evidence.Display}; mustNotBeDisturbed={workload.MustNotBeDisturbed}");
        }

        foreach (var suggestion in assessment.Suggestions)
        {
            evidence.Add($"suggestion={suggestion.Category}: {suggestion.Reason.Key}");
        }

        var protectedWorkloads = detected.Where(d => d.MustNotBeDisturbed).ToList();
        if (protectedWorkloads.Count > 0)
        {
            problems.Add(new ProblemDraft
            {
                IdPrefix = "MNT-WORKLOAD",
                Category = ComponentCategory.Maintenance,
                Severity = Severity.Warning,
                Title = LocalizedText.Of("Problem_WorkloadDetected_Title", LocalizedText.Of(protectedWorkloads[0].DisplayNameKey)),
                Description = LocalizedText.Of("Problem_WorkloadDetected_Description", string.Join(", ", protectedWorkloads.Select(d => d.Id))),
                Evidence = string.Join("; ", protectedWorkloads.Select(d => $"{d.Id}: {d.Evidence.Display}")),
                Impact = LocalizedText.Of("Problem_WorkloadDetected_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_WorkloadDetected_Action"),
                References = new[] { Id },
            });
        }

        // The module is informative: it never changes its own status to healthy/critical by itself.
        return new ModuleBody
        {
            Problems = problems,
            ChecksExecuted = assessment.Detected.Count,
            StatusOverride = problems.Count > 0 ? HealthStatus.Attention : null,
            Evidence = evidence,
        };
    }
}
