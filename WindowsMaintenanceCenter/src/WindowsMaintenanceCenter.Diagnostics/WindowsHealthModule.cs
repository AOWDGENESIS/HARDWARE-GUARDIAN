using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Diagnostics;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Core.Services;

namespace WindowsMaintenanceCenter.Diagnostics;

/// <summary>
/// Windows health (spec sections 36 to 39). The module repeats what the checks actually observed:
/// a check that was not performed is reported as not performed, never as "passed".
/// </summary>
public sealed class WindowsHealthModule : DiagnosticModuleBase
{
    public WindowsHealthModule(IClock clock) : base(clock)
    {
    }

    public override string Id => "WIN-HEALTH";

    public override string DisplayNameKey => "Module_Windows";

    public override ComponentCategory Category => ComponentCategory.Windows;

    public override bool RequiresAdministrator => false;

    public override bool RequiresNetwork => true;

    protected override string ProtocolModule => "WIN";

    protected override async Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var service = context.GetRequiredService<IWindowsHealthService>();
        var includeOnline = context.Settings.Current.UpdateCheckEnabled && !context.Offline;

        var report = await service.AssessAsync(null, includeOnline, context.Progress, cancellationToken).ConfigureAwait(false);

        var problems = new List<ProblemDraft>();
        var evidence = new List<string>
        {
            $"identity={report.Identity.ProductName.Display} {report.Identity.DisplayVersion.Display} ({report.Identity.BuildNumber.Display})",
            $"defender={report.Defender.Summary.Key}; signatures={report.Defender.SignatureVersion.Display}",
            $"updates={report.Updates.Outcome}; pendingReboot={report.PendingRebootReason ?? "no"}",
        };

        var performed = 0;
        foreach (var check in report.Checks)
        {
            if (check.Performed)
            {
                performed++;
            }

            evidence.Add($"{check.Check}: performed={check.Performed}; status={check.Status}; outcome={check.Outcome}"
                + (check.Detail is null ? string.Empty : $"; detail={check.Detail}"));

            if (!check.Performed)
            {
                continue;
            }

            if (check.Status is HealthStatus.Critical or HealthStatus.Warning or HealthStatus.Attention)
            {
                problems.Add(new ProblemDraft
                {
                    IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Windows) + "-CHECK",
                    Category = ComponentCategory.Windows,
                    Severity = check.Status switch
                    {
                        HealthStatus.Critical => Severity.Critical,
                        HealthStatus.Warning => Severity.Warning,
                        _ => Severity.Info,
                    },
                    Title = LocalizedText.Of("Problem_WindowsCheck_Title", LocalizedText.Of(check.DisplayNameKey)),
                    Description = check.Summary,
                    Evidence = check.Detail ?? string.Join("; ", check.Evidence),
                    Impact = LocalizedText.Of("Problem_WindowsCheck_Impact"),
                    RecommendedAction = LocalizedText.Of(check.RequiresAdministrator
                        ? "Problem_WindowsCheck_ActionElevated"
                        : "Problem_WindowsCheck_Action"),
                    RequiresAdministrator = check.RequiresAdministrator,
                    References = new[] { check.Check.ToString() },
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(report.PendingRebootReason))
        {
            problems.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Windows) + "-REBOOT",
                Category = ComponentCategory.Windows,
                Severity = Severity.Warning,
                Title = LocalizedText.Of("Problem_PendingReboot_Title"),
                Description = LocalizedText.Of("Problem_PendingReboot_Description"),
                Evidence = report.PendingRebootReason!,
                Impact = LocalizedText.Of("Problem_PendingReboot_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_PendingReboot_Action"),
                References = new[] { Id },
            });
        }

        if (!report.Defender.IsEnabled || report.Defender.IsSignatureOutdated)
        {
            problems.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Security),
                Category = ComponentCategory.Security,
                Severity = report.Defender.IsEnabled ? Severity.Warning : Severity.Error,
                Title = LocalizedText.Of("Problem_DefenderState_Title"),
                Description = LocalizedText.Of("Problem_DefenderState_Description"),
                Evidence = $"isEnabled={report.Defender.IsEnabled}; signatureVersion={report.Defender.SignatureVersion.Display}; signatureOutdated={report.Defender.IsSignatureOutdated}",
                Impact = LocalizedText.Of("Problem_DefenderState_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_DefenderState_Action"),
                RequiresAdministrator = true,
                References = new[] { Id },
            });
        }

        if (report.Updates.Outcome is UpdateStatus.UpdateAvailable or UpdateStatus.Optional)
        {
            problems.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Update) + "-WINDOWS",
                Category = ComponentCategory.Windows,
                Severity = report.Updates.Outcome == UpdateStatus.UpdateAvailable ? Severity.Warning : Severity.Info,
                Title = LocalizedText.Of("Problem_WindowsUpdates_Title"),
                Description = report.Updates.Summary,
                Evidence = $"outcome={report.Updates.Outcome}; pending={report.Updates.PendingCount}",
                Impact = LocalizedText.Of("Problem_WindowsUpdates_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_WindowsUpdates_Action"),
                References = new[] { Id },
            });
        }

        return new ModuleBody
        {
            Problems = problems,
            ChecksExecuted = performed,
            Evidence = evidence,
            SkipReasonKey = performed == 0 ? "Module_Skipped_NoWindowsCheck" : null,
            SkipReasonCode = performed == 0 ? "NO_WINDOWS_CHECK_PERFORMED" : null,
        };
    }
}
