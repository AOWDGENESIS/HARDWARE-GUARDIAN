using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Diagnostics;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Diagnostics;

/// <summary>
/// Driver and PnP health (spec sections 17, 18, 43). The module reports what the operating system
/// reports and never decides on its own that a driver is "old": age alone is not a defect, and
/// WHQL/date based conclusions are explicitly out of scope.
/// </summary>
public sealed class DriverHealthModule : DiagnosticModuleBase
{
    public DriverHealthModule(IClock clock) : base(clock)
    {
    }

    public override string Id => "DRV-HEALTH";

    public override string DisplayNameKey => "Module_Driver";

    public override ComponentCategory Category => ComponentCategory.Driver;

    public override bool RequiresAdministrator => true;

    protected override string ProtocolModule => "DRV";

    protected override async Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var drivers = context.GetRequiredService<IDriverInventoryService>();
        var problems = new List<ProblemDraft>();
        var evidence = new List<string>();

        var snapshot = await drivers.CaptureAsync(cancellationToken).ConfigureAwait(false);
        evidence.Add($"devices={snapshot.Devices.Count}; drivers={snapshot.Drivers.Count}; captured={snapshot.CapturedAt:O}");

        // The inventory service owns the concrete analysis; this module only adds the context that
        // the hardware itself provides, so that a problem always names the affected component.
        var analyzed = await drivers.AnalyzeAsync(snapshot, cancellationToken).ConfigureAwait(false);
        foreach (var problem in analyzed)
        {
            problems.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(problem.Category),
                Category = problem.Category,
                Severity = problem.Severity,
                Title = problem.Title,
                Description = problem.Description,
                Evidence = problem.Evidence,
                Impact = problem.Impact,
                RecommendedAction = problem.RecommendedAction,
                ComponentId = problem.ComponentId,
                ComponentName = problem.ComponentName,
                ActionId = problem.ActionId,
                RequiresAdministrator = problem.RequiresAdministrator,
                References = problem.References,
            });
        }

        var withoutProblemCode = snapshot.Devices.Count(d => !d.ProblemCode.HasValue);
        var withErrorCode = snapshot.Devices.Count(d => d.ProblemCode.HasValue && d.ProblemCode.Value != 0);
        var withoutService = snapshot.Devices.Count(d => !d.ServiceOrDriver.IsKnown);
        evidence.Add($"problemCodeReported={snapshot.Devices.Count - withoutProblemCode}; errorCode={withErrorCode}");
        evidence.Add($"devicesWithoutService={withoutService}");
        evidence.Add($"missingDevices={snapshot.Devices.Count(d => d.IsPresent == false)}; phantom={snapshot.Devices.Count(d => d.IsPhantomDevice)}");

        var unsigned = snapshot.Drivers.Count(d => d.IsSigned == false);
        var signed = snapshot.Drivers.Count(d => d.IsSigned == true);
        var signatureUnknown = snapshot.Drivers.Count - unsigned - signed;
        evidence.Add($"signed={signed}; unsigned={unsigned}; signatureUnknown={signatureUnknown}");

        if (unsigned > 0)
        {
            problems.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Driver),
                Category = ComponentCategory.Driver,
                Severity = Severity.Error,
                Title = LocalizedText.Of("Problem_DriverUnsigned_Title", unsigned),
                Description = LocalizedText.Of("Problem_DriverUnsigned_Description"),
                Evidence = string.Join("; ", snapshot.Drivers.Where(d => d.IsSigned == false).Take(10).Select(d => $"{d.DeviceName.Display}: {d.InfName.Display} / {d.DriverFileName.Display}")),
                Impact = LocalizedText.Of("Problem_DriverUnsigned_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_DriverUnsigned_Action"),
                RequiresAdministrator = true,
                References = new[] { Id },
            });
        }

        if (signatureUnknown > 0)
        {
            // Fail closed: an unverified signature is not a valid signature.
            problems.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Driver),
                Category = ComponentCategory.Driver,
                Severity = Severity.Info,
                Title = LocalizedText.Of("Problem_DriverSignatureUnknown_Title", signatureUnknown),
                Description = LocalizedText.Of("Problem_DriverSignatureUnknown_Description"),
                Evidence = string.Join("; ", snapshot.Drivers.Where(d => d.IsSigned is null).Take(10).Select(d => d.DeviceName.Display)),
                Impact = LocalizedText.Of("Problem_DriverSignatureUnknown_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_DriverSignatureUnknown_Action"),
                References = new[] { Id },
            });
        }

        // Components are built from the same snapshot so the hardware inventory and the driver view
        // never disagree with each other.
        if (snapshot.Drivers.Count == 0)
        {
            // An empty inventory is a statement about the read operation, not about the machine.
            problems.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Driver),
                Category = ComponentCategory.Driver,
                Severity = Severity.Info,
                Title = LocalizedText.Of("Problem_DriverInventoryEmpty_Title"),
                Description = LocalizedText.Of("Problem_DriverInventoryEmpty_Description"),
                Evidence = $"devices={snapshot.Devices.Count}; drivers=0; elevated={context.Environment.IsElevated}; provider={context.Hardware.ProviderName}",
                Impact = LocalizedText.Of("Problem_DriverInventoryEmpty_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_DriverInventoryEmpty_Action"),
                RequiresAdministrator = true,
                References = new[] { Id },
            });
        }

        var components = await drivers.BuildComponentsAsync(snapshot, cancellationToken).ConfigureAwait(false);

        return new ModuleBody
        {
            Components = components,
            Problems = problems,
            ChecksExecuted =
                3 // problem codes, service binding, signature state
                + analyzed.Count,
            Evidence = evidence,
        };
    }
}
