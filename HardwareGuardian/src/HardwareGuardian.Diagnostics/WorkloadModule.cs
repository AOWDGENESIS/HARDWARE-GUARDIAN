using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Diagnostics;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Services;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Diagnostics;

/// <summary>
/// Detects workloads that must not be disturbed (spec section 19) - virtual machines, containers,
/// remote sessions, database servers and a running Windows Update session.
///
/// Findings here are informational: they explain why optimisation proposals are withheld. They are
/// never a reason to stop a service.
/// </summary>
public sealed class WorkloadModule : DiagnosticModuleBase
{
    public WorkloadModule(IClock clock)
        : base(clock)
    {
    }

    public override string Id => "WORKLOAD";

    public override string DisplayNameKey => "Module_Workload";

    public override ComponentCategory Category => ComponentCategory.Maintenance;

    protected override async Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var detector = context.GetRequiredService<IWorkloadDetector>();
        var assessment = await detector.DetectAsync(null, cancellationToken).ConfigureAwait(false);

        var evidence = new List<string>
        {
            $"profile={assessment.Profile}",
            $"summaryKey={assessment.Summary.Key}",
            $"detected={assessment.Detected.Count}",
        };

        foreach (var workload in assessment.Detected)
        {
            evidence.Add($"workload={workload.Id} detected={workload.Detected} mustNotBeDisturbed={workload.MustNotBeDisturbed} evidence={workload.Evidence.Display}");
        }

        var protectedWorkloads = assessment.Detected.Where(w => w.Detected && w.MustNotBeDisturbed).ToList();
        var drafts = protectedWorkloads
            .Select(workload => new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Maintenance),
                Category = ComponentCategory.Maintenance,
                Severity = Severity.Info,
                Title = LocalizedText.Of("Problem_WorkloadDetected_Title", LocalizedText.Of(workload.DisplayNameKey)),
                Description = LocalizedText.Of("Problem_WorkloadDetected_Description", LocalizedText.Of(workload.DisplayNameKey)),
                Impact = LocalizedText.Of("Problem_WorkloadDetected_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_WorkloadDetected_Action"),
                Evidence = workload.Evidence.Display,
                References = new[] { Id, workload.Id },
            })
            .ToList();

        return new ModuleBody
        {
            Problems = drafts,
            ChecksExecuted = assessment.Detected.Count,
            Evidence = evidence,
        };
    }
}
