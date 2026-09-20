using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>Identifiers of the Windows checks that Windows Maintenance Center can actually perform.</summary>
public enum WindowsCheckId
{
    SystemFileIntegrity,
    ComponentStore,
    EventLogErrors,
    DeviceErrors,
    StorageSpace,
    DefenderStatus,
    UpdateStatus,
    SecureBoot,
    StartupImpact,

    // Drei Werte standen hier, ohne dass sie je erzeugt wurden: ServiceHealth, TimeSynchronisation
    // und DriverSignatures. Ein Prüfwert, den niemand liefert, behauptet eine Prüfung, die es nicht
    // gibt - er ist entfernt. Die Treibersignatur prüft das Treibermodul, die Dienste wertet die
    // Autostart-Prüfung aus.

    /// <summary>Trusted Platform Module state (rule 87, DIAG-F-011).</summary>
    Tpm,

    /// <summary>Windows Firewall profile state (rule 87, DIAG-F-009).</summary>
    Firewall,
}

/// <summary>One executed Windows check with its real outcome.</summary>
public sealed record WindowsCheckResult
{
    public WindowsCheckId Check { get; init; }

    public string DisplayNameKey { get; init; } = "Windows_Check_Unknown";

    public HealthStatus Status { get; init; } = HealthStatus.Unknown;

    public StageOutcome Outcome { get; init; } = StageOutcome.NotRun;

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Windows_Check_NotRun");

    public string? Detail { get; init; }

    public string? CommandLine { get; init; }

    public bool RequiresAdministrator { get; init; }

    public bool Performed { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>Full Windows health view.</summary>
public sealed record WindowsHealthReport
{
    public WindowsIdentityInfo Identity { get; init; } = new();

    public HealthStatus Status { get; init; } = HealthStatus.Unknown;

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Windows_Health_Unknown");

    public IReadOnlyList<WindowsCheckResult> Checks { get; init; } = Array.Empty<WindowsCheckResult>();

    public DefenderStatus Defender { get; init; } = new();

    public UpdateAvailability Updates { get; init; } = new();

    public IReadOnlyList<ServiceStartupInfo> Startup { get; init; } = Array.Empty<ServiceStartupInfo>();

    public string? PendingRebootReason { get; init; }

    public DateTimeOffset AssessedAt { get; init; }
}

/// <summary>
/// Result of an integrity check (DISM/SFC). Repair is only requested explicitly by the user
/// and the outcome is derived from the real tool output, never assumed (spec section 17).
/// </summary>
public sealed record IntegrityCheckResult
{
    public WindowsCheckId Check { get; init; }

    public StageOutcome Outcome { get; init; } = StageOutcome.NotRun;

    public string CommandLine { get; init; } = string.Empty;

    public int ExitCode { get; init; }

    public bool TimedOut { get; init; }

    public bool RepairRequested { get; init; }

    public bool RepairSucceeded { get; init; }

    /// <summary>True when the tool reported that changes were made.</summary>
    public bool ChangesPerformed { get; init; }

    /// <summary>True when a verification run after the repair confirmed the new state.</summary>
    public bool VerifiedAfterRepair { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Integrity_NotRun");

    public string RawOutput { get; init; } = string.Empty;

    public bool RequiresAdministrator { get; init; } = true;

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public TimeSpan Duration { get; init; }
}

/// <summary>An installed Windows update (from the local update history).</summary>
public sealed record WindowsUpdateInfo
{
    public TextInfo HotFixId { get; init; }

    public TextInfo Description { get; init; }

    public TextInfo InstalledOn { get; init; }

    public TextInfo InstalledBy { get; init; }

    public TextInfo Caption { get; init; }

    public TextInfo SupportUrl { get; init; }

    // -----------------------------------------------------------------------------------------
    // Fields of an update that is offered but not installed yet (rule 90, UPDATE-F-003).
    // All of them are optional: the update agent reports what it reports, and a value that it does
    // not report is shown as unknown instead of being filled in.
    // -----------------------------------------------------------------------------------------

    /// <summary>Knowledge base number as reported by the agent, for example <c>KB5031354</c>.</summary>
    public TextInfo KnowledgeBaseId { get; init; }

    /// <summary>Classification level as reported by the agent, for example <c>Security Updates</c>.</summary>
    public TextInfo Category { get; init; }

    /// <summary>MSRC severity rating as reported by the agent, for example <c>Important</c>.</summary>
    public TextInfo Severity { get; init; }

    /// <summary>Maximum download size in bytes as reported by the agent.</summary>
    public Measured<ulong> DownloadSizeBytes { get; init; } = Measured<ulong>.NotAvailable("download size not reported");

    /// <summary>True when the agent says that installing this update requires a reboot.</summary>
    public bool? RebootRequired { get; init; }

    /// <summary>True when the agent flags the update as mandatory for this machine.</summary>
    public bool? IsMandatory { get; init; }

    /// <summary>True when the update content is already cached locally.</summary>
    public bool? IsDownloaded { get; init; }
}

/// <summary>Availability check for Windows updates. Only the real update agent is queried.</summary>
public sealed record UpdateAvailability
{
    public StageOutcome Outcome { get; init; } = StageOutcome.NotRun;

    public bool SearchPerformed { get; init; }

    public bool RequiresAdministrator { get; init; }

    public int PendingCount { get; init; }

    public bool PendingReboot { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("WindowsUpdate_NotChecked");

    public IReadOnlyList<WindowsUpdateInfo> RecentUpdates { get; init; } = Array.Empty<WindowsUpdateInfo>();

    public IReadOnlyList<WindowsUpdateInfo> Available { get; init; } = Array.Empty<WindowsUpdateInfo>();

    public string? ErrorDetail { get; init; }
}

/// <summary>
/// Result of a download or an installation of one offered Windows update (rule 90). Every field is
/// the agent's own statement: a missing code stays missing, and the state after the action is the
/// measured state, never the expected one (UPDATE-E-001, UPDATE-R-001).
/// </summary>
public sealed record UpdateActionReport
{
    /// <summary>Position of the update in the offer list that the caller acted on.</summary>
    public int Index { get; init; } = -1;

    public TextInfo Title { get; init; }

    public StageOutcome Outcome { get; init; } = StageOutcome.NotRun;

    public bool Succeeded { get; init; }

    /// <summary>True for "succeeded with errors": installed, but the agent reported problems.</summary>
    public bool Partial { get; init; }

    public bool RequiresAdministrator { get; init; }

    public bool RebootRequired { get; init; }

    public TextInfo ResultCode { get; init; }

    /// <summary>How many updates the agent still offered after the action finished.</summary>
    public int? PendingCountAfter { get; init; }

    /// <summary>The agent's restart statement after the action.</summary>
    public bool? PendingRebootAfter { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("WindowsUpdate_NotChecked");

    public string? ErrorDetail { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>Service or autostart entry, read only (spec section 54: never changed silently).</summary>
public sealed record ServiceStartupInfo
{
    public TextInfo Name { get; init; }

    public TextInfo DisplayName { get; init; }

    public TextInfo StartMode { get; init; }

    public TextInfo State { get; init; }

    public TextInfo BinaryPath { get; init; }

    public TextInfo Account { get; init; }

    public bool IsAutomatic { get; init; }

    public bool IsDelayedAutomatic { get; init; }

    public bool IsThirdParty { get; init; }

    public bool IsDisabled { get; init; }

    /// <summary>Importance for the workload assessment: 1 = low impact, 3 = do not touch.</summary>
    public int Importance { get; init; } = 2;

    public string SourceKey { get; init; } = "Startup_Source_Service";
}

/// <summary>Microsoft Defender status as reported by the platform.</summary>
public sealed record DefenderStatus
{
    public bool Available { get; init; }

    public TextInfo AntivirusEnabled { get; init; }

    public TextInfo RealTimeProtectionEnabled { get; init; }

    public TextInfo EngineVersion { get; init; }

    public TextInfo SignatureVersion { get; init; }

    public TextInfo SignatureLastUpdated { get; init; }

    public TextInfo TamperProtection { get; init; }

    public TextInfo ThreatsDetected { get; init; }

    public TextInfo AntivirusProvider { get; init; }

    public TextInfo AntispywareEnabled { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Defender_Unknown");

    public string? ErrorDetail { get; init; }
}
