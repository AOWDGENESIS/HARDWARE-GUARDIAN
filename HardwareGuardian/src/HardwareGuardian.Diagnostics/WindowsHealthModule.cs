using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Diagnostics;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Services;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Diagnostics;

/// <summary>
/// Windows health (spec sections 30 to 33): component store, event log, Defender, update status,
/// Secure Boot and free space.
///
/// Online checks only run when the settings allow it and offline mode is off. The module never
/// repairs anything - repairs are separate, approved actions with an administrator check.
/// </summary>
public sealed class WindowsHealthModule : DiagnosticModuleBase
{
    public WindowsHealthModule(IClock clock)
        : base(clock)
    {
    }

    public override string Id => "WIN-HEALTH";

    public override string DisplayNameKey => "Module_Windows";

    public override ComponentCategory Category => ComponentCategory.Windows;

    public override bool RequiresNetwork => true;

    protected override async Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var service = context.GetRequiredService<IWindowsHealthService>();

        // The storage check needs volume data. The scan keeps its own inventory, so the module reads
        // the volumes it needs directly instead of asking for a global snapshot that does not exist yet.
        SystemSnapshot? snapshot = null;
        try
        {
            var storage = await context.Hardware.GetStorageDevicesAsync(cancellationToken).ConfigureAwait(false);
            var identity = await context.Hardware.GetWindowsIdentityAsync(cancellationToken).ConfigureAwait(false);
            snapshot = new SystemSnapshot { Storage = storage, Windows = identity };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Left null on purpose: the checks that need volume data report "no data" instead of failing.
            context.Protocol.Warning(ProtocolModule, LocalizedText.Of("Protocol_ProviderUnavailable", "storage"), $"{ex.GetType().Name}: {ex.Message}");
        }

        var onlineAllowed = !context.Offline && context.Settings.Current.UpdateCheckEnabled;
        var report = await service.AssessAsync(snapshot, onlineAllowed, context.Progress, cancellationToken).ConfigureAwait(false);

        var evidence = new List<string>
        {
            $"status={report.Status}",
            $"assessedAt={report.AssessedAt:O}",
            $"checks={report.Checks.Count}",
            $"onlineChecked={onlineAllowed}",
        };

        if (!string.IsNullOrWhiteSpace(report.PendingRebootReason))
        {
            evidence.Add($"pendingRebootReason={report.PendingRebootReason}");
        }

        foreach (var check in report.Checks)
        {
            evidence.Add($"check={check.Check} performed={check.Performed} status={check.Status} outcome={check.Outcome} summary={check.Summary.Key} detail={check.Detail ?? "n/a"}");
        }

        if (report.Defender is { } defender)
        {
            evidence.Add($"defender available={defender.Available} realTime={defender.RealTimeProtectionEnabled} signatureAge=<see signature version {defender.SignatureVersion}> summary={defender.Summary.Key}");
        }

        if (report.Updates is { } updates)
        {
            evidence.Add($"updates searchPerformed={updates.SearchPerformed} pending={updates.PendingCount} pendingReboot={updates.PendingReboot} outcome={updates.Outcome}");
        }

        var drafts = report.Checks
            .Where(check => check.Performed)
            .Where(check => check.Status is HealthStatus.Attention or HealthStatus.Warning or HealthStatus.Critical)
            .Select(check => new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Windows),
                Category = ComponentCategory.Windows,
                Severity = check.Status switch
                {
                    HealthStatus.Critical => Severity.Critical,
                    HealthStatus.Warning => Severity.Warning,
                    _ => Severity.Info,
                },
                Title = LocalizedText.Of("Problem_WindowsCheck_Title", LocalizedText.Of(check.DisplayNameKey)),
                Description = check.Summary,
                Impact = LocalizedText.Of("Problem_WindowsCheck_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_WindowsCheck_Action"),
                RequiresAdministrator = check.RequiresAdministrator,
                Evidence = string.Join("; ", check.Evidence.Append(check.Detail ?? string.Empty).Where(e => e.Length > 0)),
                References = new[] { Id, check.Check.ToString() },
            })
            .ToList();

        return new ModuleBody
        {
            Problems = drafts,
            ChecksExecuted = report.Checks.Count(c => c.Performed),
            Evidence = evidence,
            StatusOverride = report.Checks.Any(c => c.Performed)
                ? report.Status
                : HealthStatus.Unknown,
        };
    }
}
