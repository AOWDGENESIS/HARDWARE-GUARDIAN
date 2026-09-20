using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Maintenance;

/// <summary>
/// Known process signatures. Only names are compared, and every hit is reported as evidence in the
/// workload assessment so that the user can check it (spec section 19).
/// </summary>
internal static class WorkloadSignatures
{
    public static ProcessCategory ClassifyProcess(string name) => name.ToUpperInvariant() switch
    {
        "SYSTEM" or "IDLE" or "REGISTRY" or "MEMORY COMPRESSION" or "SMSS.EXE" or "CSRSS.EXE" or "WININIT.EXE"
            or "SERVICES.EXE" or "LSASS.EXE" or "WINLOGON.EXE" or "FONTCACHE.DAT" => ProcessCategory.System,
        "MSIEXEC.EXE" or "SETUP.EXE" or "SETUPAPP.EXE" or "INSTALLER.EXE" or "DPSETUP.EXE" or "FIRMWAREUPDATE.EXE" => ProcessCategory.Installer,
        "MSMPENG.EXE" or "NISSERV.EXE" or "AVP.EXE" or "AVPUI.EXE" or "MBAM.EXE" => ProcessCategory.Security,
        _ => ProcessCategory.User,
    };

    public static bool IsInstaller(string name) => ClassifyProcess(name) == ProcessCategory.Installer;

    /// <summary>A workload that must not be disturbed by a change. Keyed by process name.</summary>
    public static string? WorkloadFor(string name) => name.ToUpperInvariant() switch
    {
        "OBS64" or "OBS32" or "STREAMLABS" or "XSPLIT" => "streaming",
        "ZOOM" or "TEAMS" or "MS-TEAMS" or "DISCORD" or "SKYPE" or "WEBEXMEETINGS" => "communication",
        "VMWARE" or "VMWARE-VMX" or "VBOXHEADLESS" or "VIRTUALBOX" or "QEMU-SYSTEM-X86_64" or "VMMEM" or "VMMEMWSL" => "virtualization",
        "STEAM" or "STEAMWEBHELPER" or "EPICGAMESLAUNCHER" or "GOGGALAXY" or "BATTLE.NET" => "gaming-launcher",
        "DOCKER DESKTOP" or "COMSYS" or "MYSQLD" or "POSTGRES" or "MSSQLSERVER" => "development",
        _ => null,
    };

    /// <summary>Human readable evidence for a workload hit.</summary>
    public static string EvidenceFor(string id) => id switch
    {
        "streaming" => "streaming or capture software is running",
        "communication" => "a real-time communication client is running",
        "virtualization" => "a virtual machine or container runtime is running",
        "gaming-launcher" => "a game launcher or game is running",
        "development" => "a local development service is running",
        _ => "process name matched a known workload signature",
    };

    /// <summary>Workloads that make a change unsafe while they are running.</summary>
    public static bool MustNotBeDisturbed(string id) => id is "streaming" or "virtualization" or "communication";
}

/// <summary>
/// Workload detection for workload aware optimisation (spec section 19) and for the risk
/// assessment before a change (spec section 36). Detection is based on process names only, and it
/// never blocks anything by itself - it produces a proposal with evidence.
/// </summary>
public sealed class WorkloadDetector : IWorkloadDetector
{
    private readonly IProcessSnapshotProvider _processes;
    private readonly IClock _clock;

    public WorkloadDetector(IProcessSnapshotProvider processes, IClock clock)
    {
        _processes = processes;
        _clock = clock;
    }

    public Task<WorkloadAssessment> DetectAsync(SystemSnapshot? snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var origin = ValueOrigin.WindowsApi(_clock.Now, "Process table");
        var entries = _processes.Snapshot();
        var detected = new List<DetectedWorkload>();

        foreach (var group in entries
                     .Select(entry => (Entry: entry, Workload: WorkloadSignatures.WorkloadFor(Path.GetFileNameWithoutExtension(entry.Name))))
                     .Where(pair => pair.Workload is not null)
                     .GroupBy(pair => pair.Workload!))
        {
            var busiest = group.OrderByDescending(pair => pair.Entry.WorkingSetBytes).First();
            detected.Add(new DetectedWorkload
            {
                Id = group.Key,
                DisplayNameKey = $"Workload_{group.Key}",
                Detected = true,
                Evidence = TextInfo.Known(
                    $"{WorkloadSignatures.EvidenceFor(group.Key)}; process={Path.GetFileNameWithoutExtension(busiest.Entry.Name)}; count={group.Count()}; working set={busiest.Entry.WorkingSetBytes / (1024 * 1024)} MB",
                    origin),
                MustNotBeDisturbed = WorkloadSignatures.MustNotBeDisturbed(group.Key),
            });
        }

        var profile = DetermineProfile(detected, snapshot);
        var suggestions = BuildSuggestions(detected);

        var assessment = new WorkloadAssessment
        {
            Profile = profile,
            Summary = detected.Count == 0
                ? LocalizedText.Of("Workload_Summary_None")
                : LocalizedText.Of("Workload_Summary_Detected", detected.Count, LocalizedText.Of($"WorkloadProfile_{profile}")),
            Detected = detected,
            Suggestions = suggestions,
        };

        return Task.FromResult(assessment);
    }

    /// <summary>
    /// The profile is an inference from the detected processes plus the machine's memory size.
    /// It is reported as an inference, never as a measurement.
    /// </summary>
    private static WorkloadProfile DetermineProfile(IReadOnlyList<DetectedWorkload> detected, SystemSnapshot? snapshot)
    {
        if (detected.Count == 0)
        {
            return WorkloadProfile.Unknown;
        }

        if (detected.Any(d => d.Id == "virtualization"))
        {
            return WorkloadProfile.VirtualizationHost;
        }

        if (detected.Any(d => d.Id == "streaming"))
        {
            return WorkloadProfile.Gaming;
        }

        if (detected.Any(d => d.Id == "gaming-launcher"))
        {
            return WorkloadProfile.Gaming;
        }

        if (detected.Any(d => d.Id == "development"))
        {
            var memoryGb = snapshot?.Memory.TotalPhysicalBytes.HasValue == true
                ? snapshot.Memory.TotalPhysicalBytes.Value!.Value / (1024d * 1024 * 1024)
                : 0d;

            return memoryGb >= 32 ? WorkloadProfile.LocalAiWorkstation : WorkloadProfile.DeveloperWorkstation;
        }

        return detected.Any(d => d.Id == "communication") ? WorkloadProfile.GeneralOffice : WorkloadProfile.Unknown;
    }

    /// <summary>
    /// Proposals only. Every suggestion names a maintenance category that the maintenance engine can
    /// execute after approval; suggestions whose workload must not be disturbed are blocked with the
    /// reason instead of being offered (fail closed).
    /// </summary>
    private static IReadOnlyList<OptimizationSuggestion> BuildSuggestions(IReadOnlyList<DetectedWorkload> detected)
    {
        var suggestions = new List<OptimizationSuggestion>();

        foreach (var workload in detected)
        {
            if (workload.MustNotBeDisturbed)
            {
                suggestions.Add(new OptimizationSuggestion
                {
                    Id = $"opt-blocked-{workload.Id}",
                    DisplayNameKey = "Optimization_Blocked_Name",
                    What = LocalizedText.Of("Optimization_Blocked_What", workload.DisplayNameKey),
                    Why = workload.Evidence.IsKnown ? LocalizedText.Of("Optimization_Blocked_Why", workload.Evidence.Value) : LocalizedText.Of("Optimization_Blocked_Why_Unknown"),
                    Risk = LocalizedText.Of("Optimization_Blocked_Risk"),
                    RiskLevel = RiskLevel.High,
                    RequiresAdministrator = false,
                    IsReversible = true,
                    BlockedReasonCode = BlockReasons.ProtectionActive,
                    BlockedReason = LocalizedText.Of("Optimization_Blocked_Reason", workload.DisplayNameKey),
                });
            }
        }

        if (detected.Any(d => d.Id == "gaming-launcher"))
        {
            suggestions.Add(new OptimizationSuggestion
            {
                Id = $"opt-{MaintenanceCategory.ShaderCache}",
                DisplayNameKey = "Optimization_ShaderCache_Name",
                What = LocalizedText.Of("Optimization_ShaderCache_What"),
                Why = LocalizedText.Of("Optimization_ShaderCache_Why"),
                Risk = LocalizedText.Of("Optimization_ShaderCache_Risk"),
                RiskLevel = RiskLevel.Medium,
                RequiresAdministrator = false,
                IsReversible = true,
            });
        }

        if (detected.Any(d => d.Id == "virtualization"))
        {
            suggestions.Add(new OptimizationSuggestion
            {
                Id = $"opt-{MaintenanceCategory.WindowsUpdateCache}",
                DisplayNameKey = "Optimization_UpdateCache_Name",
                What = LocalizedText.Of("Optimization_UpdateCache_What"),
                Why = LocalizedText.Of("Optimization_UpdateCache_Why"),
                Risk = LocalizedText.Of("Optimization_UpdateCache_Risk"),
                RiskLevel = RiskLevel.Medium,
                RequiresAdministrator = true,
                IsReversible = true,
            });
        }

        return suggestions;
    }
}
