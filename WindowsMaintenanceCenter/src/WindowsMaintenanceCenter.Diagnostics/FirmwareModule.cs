using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Diagnostics;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Diagnostics;

/// <summary>
/// Firmware/BIOS assessment (spec sections 26 and 27). The module only ever *assesses*: flashing is
/// a manual, documented step outside this application, therefore a firmware recommendation always
/// carries the manual step as its action.
/// </summary>
public sealed class FirmwareModule : DiagnosticModuleBase
{
    public FirmwareModule(IClock clock) : base(clock)
    {
    }

    public override string Id => "BIOS-ASSESS";

    public override string DisplayNameKey => "Module_Bios";

    public override ComponentCategory Category => ComponentCategory.Bios;

    public override bool RequiresNetwork => true;

    protected override string ProtocolModule => "BIOS";

    protected override async Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var bios = await context.Hardware.GetBiosAsync(cancellationToken).ConfigureAwait(false);
        var board = await context.Hardware.GetMotherboardAsync(cancellationToken).ConfigureAwait(false);
        var identity = await context.Hardware.GetSystemIdentityAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = new SystemSnapshot
        {
            Id = $"FW-{context.Clock.Now:yyyyMMddHHmmss}",
            CapturedAt = context.Clock.Now,
            System = identity,
            Bios = bios,
            Motherboard = board,
            IsSimulation = context.Simulation,
        };

        var service = context.GetRequiredService<IBiosService>();
        var assessment = await service.AssessUpdateAsync(snapshot, cancellationToken).ConfigureAwait(false);

        var evidence = new List<string>
        {
            $"vendor={bios.Manufacturer.Display}; version={bios.Version.Display}; released={bios.ReleaseDate.Display}",
            $"uefi={bios.IsUefi}; firmwareType={bios.FirmwareType.Display}; secureBoot={bios.SecureBootEnabled}",
            $"board={board.Manufacturer.Display} {board.Product.Display}; revision={board.SmbiosBoardRevision.Display}; revisionVerified={board.RevisionVerified}",
            $"status={assessment.Status}; freshness={assessment.Freshness}; current={assessment.Current.Raw.Display}; latest={assessment.Latest?.Raw.Display ?? "UNKNOWN"}",
            $"requiresManualFlash={assessment.RequiresManualFlash}; blockedReason={assessment.BlockedReasonCode ?? "none"}",
        };

        var problems = new List<ProblemDraft>();

        // The board revision decides which firmware file is even valid. If it could not be verified,
        // that is reported instead of being quietly assumed.
        if (!assessment.RevisionVerified)
        {
            problems.Add(new ProblemDraft
            {
                IdPrefix = "BIOS-REV",
                Category = ComponentCategory.Bios,
                Severity = Severity.Info,
                Title = LocalizedText.Of("Problem_BoardRevisionUnverified_Title"),
                Description = LocalizedText.Of("Problem_BoardRevisionUnverified_Description"),
                Evidence = $"{board.RevisionVerificationDetail ?? "no verification detail"}; smbiosRevision={board.SmbiosBoardRevision.Display}",
                Impact = LocalizedText.Of("Problem_BoardRevisionUnverified_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_BoardRevisionUnverified_Action"),
                References = new[] { Id },
            });
        }

        switch (assessment.Status)
        {
            case UpdateStatus.UpdateAvailable:
                problems.Add(new ProblemDraft
                {
                    IdPrefix = "BIOS-UPD",
                    Category = ComponentCategory.Bios,
                    Severity = assessment.IsSecurityRelevant ? Severity.Warning : Severity.Info,
                    Title = LocalizedText.Of("Problem_FirmwareUpdate_Title", assessment.Current.Raw.Display, assessment.Latest?.Raw.Display ?? "UNKNOWN"),
                    Description = assessment.Reason,
                    Evidence = string.Join("; ", assessment.Evidence),
                    Impact = assessment.WhyItMatters ?? LocalizedText.Of("Problem_FirmwareUpdate_Impact"),
                    RecommendedAction = LocalizedText.Of("Problem_FirmwareUpdate_Action"),
                    RequiresAdministrator = true,
                    References = new[] { Id },
                });
                break;

            case UpdateStatus.Blocked:
                problems.Add(new ProblemDraft
                {
                    IdPrefix = "BIOS-BLOCKED",
                    Category = ComponentCategory.Bios,
                    Severity = Severity.Info,
                    Title = LocalizedText.Of("Problem_FirmwareCheckBlocked_Title"),
                    Description = assessment.Reason,
                    Evidence = $"blockedReason={assessment.BlockedReasonCode ?? "unknown"}; source={assessment.Source.DisplayNameKey}; trust={assessment.Source.Trust}",
                    Impact = LocalizedText.Of("Problem_FirmwareCheckBlocked_Impact"),
                    RecommendedAction = LocalizedText.Of("Problem_FirmwareCheckBlocked_Action"),
                    References = new[] { Id },
                });
                break;

            case UpdateStatus.Unknown:
            case UpdateStatus.Warning:
                problems.Add(new ProblemDraft
                {
                    IdPrefix = "BIOS-UNKNOWN",
                    Category = ComponentCategory.Bios,
                    Severity = Severity.Info,
                    Title = LocalizedText.Of("Problem_FirmwareUnknown_Title"),
                    Description = assessment.Reason,
                    Evidence = string.Join("; ", assessment.Evidence),
                    Impact = LocalizedText.Of("Problem_FirmwareUnknown_Impact"),
                    RecommendedAction = LocalizedText.Of("Problem_FirmwareUnknown_Action"),
                    References = new[] { Id },
                });
                break;
        }

        return new ModuleBody
        {
            Problems = problems,
            ChecksExecuted = 5, // firmware identity, board identity, revision state, source assessment, update comparison
            Evidence = evidence,
        };
    }
}
