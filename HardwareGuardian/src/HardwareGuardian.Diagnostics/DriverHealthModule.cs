using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Diagnostics;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Services;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Diagnostics;

/// <summary>
/// Driver and plug and play health (spec section 29).
///
/// The module does not judge versions itself: <see cref="IDriverInventoryService"/> owns the
/// comparison against the executed state, and its problems already carry identifiers and evidence.
/// The status derived here therefore comes from those problems, not from a guess.
/// </summary>
public sealed class DriverHealthModule : DiagnosticModuleBase
{
    public DriverHealthModule(IClock clock)
        : base(clock)
    {
    }

    public override string Id => "DRV-HEALTH";

    public override string DisplayNameKey => "Module_Driver";

    public override ComponentCategory Category => ComponentCategory.Driver;

    protected override async Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var service = context.GetRequiredService<IDriverInventoryService>();

        var snapshot = await service.CaptureAsync(cancellationToken).ConfigureAwait(false);
        var problems = await service.AnalyzeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        var components = await service.BuildComponentsAsync(snapshot, cancellationToken).ConfigureAwait(false);

        context.Problems.RegisterRange(problems);

        var devicesWithProblemCode = snapshot.Devices.Count(d => d.ProblemCode.HasValue && d.ProblemCode.Value!.Value != 0);
        var devicesWithoutService = snapshot.Devices.Count(d => !d.ServiceOrDriver.IsKnown);
        var unsignedDrivers = snapshot.Drivers.Count(d => d.IsSigned == false);

        var evidence = new List<string>
        {
            $"snapshot={snapshot.Id}",
            $"drivers={snapshot.Drivers.Count}",
            $"devices={snapshot.Devices.Count}",
            $"devices-with-problem-code={devicesWithProblemCode}",
            $"devices-without-service={devicesWithoutService}",
            $"drivers-reported-unsigned={unsignedDrivers}",
            $"problems={problems.Count}",
        };

        foreach (var problem in problems.Take(20))
        {
            evidence.Add($"{problem.Id}: {problem.Severity} - {problem.Evidence}");
        }

        var drafts = new List<ProblemDraft>();

        if (snapshot.Drivers.Count == 0)
        {
            // An empty inventory is not a clean bill of health, it is a missing reading.
            drafts.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Driver),
                Category = ComponentCategory.Driver,
                Severity = Severity.Info,
                Title = LocalizedText.Of("Problem_DriverInventoryEmpty_Title"),
                Description = LocalizedText.Of("Problem_DriverInventoryEmpty_Description"),
                Impact = LocalizedText.Of("Problem_DriverInventoryEmpty_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_DriverInventoryEmpty_Action"),
                Evidence = string.Join("; ", evidence),
                References = new[] { Id },
            });
        }

        return new ModuleBody
        {
            Components = components,
            Problems = drafts,
            ChecksExecuted = Math.Max(snapshot.Drivers.Count, 1),
            Evidence = evidence,
            StatusOverride = problems.Count > 0
                ? OverallStatusCalculator.Worst(problems.Select(p => OverallStatusCalculator.FromSeverity(p.Severity)))
                : snapshot.Drivers.Count > 0 ? HealthStatus.Healthy : HealthStatus.Unknown,
        };
    }
}
