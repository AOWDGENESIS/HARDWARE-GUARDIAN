using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Models;

/// <summary>One completed diagnostic run, kept for the history view (spec section 66).</summary>
public sealed record HistoryEntry
{
    public string Id { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; }

    public string Kind { get; init; } = "scan";

    public string OperationKey { get; init; } = "History_Operation_Scan";

    public HealthStatus OverallStatus { get; init; } = HealthStatus.Unknown;

    public int ProblemCount { get; init; }

    public int CriticalCount { get; init; }

    public int WarningCount { get; init; }

    public int InformationCount { get; init; }

    public int ChangeCount { get; init; }

    public IReadOnlyList<string> ChangeSummary { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ProblemIds { get; init; } = Array.Empty<string>();

    public string? SnapshotPath { get; init; }

    public TimeSpan Duration { get; init; }

    public string ApplicationVersion { get; init; } = string.Empty;

    /// <summary>True when the run was a simulation (mock provider) run.</summary>
    public bool IsSimulation { get; init; }
}

/// <summary>Recorded difference between two runs.</summary>
public sealed record DetectedChange
{
    public string Id { get; init; } = string.Empty;

    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    public LocalizedText Description { get; init; } = LocalizedText.Of("Change_Unknown");

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public Severity Severity { get; init; } = Severity.Info;

    public DateTimeOffset DetectedAt { get; init; }
}

/// <summary>Everything a scan produced. This is the single source of truth for the UI and reports.</summary>
public sealed record SystemSnapshot
{
    public string Id { get; init; } = string.Empty;

    public DateTimeOffset CapturedAt { get; init; }

    public TimeSpan Duration { get; init; }

    public WindowsIdentityInfo Windows { get; init; } = new();

    public SystemIdentity System { get; init; } = new();

    public BiosIdentification Bios { get; init; } = new();

    public MotherboardInfo Motherboard { get; init; } = new();

    public IReadOnlyList<ProcessorInfo> Processors { get; init; } = Array.Empty<ProcessorInfo>();

    public MemoryInfo Memory { get; init; } = new();

    public IReadOnlyList<GraphicsAdapterInfo> Graphics { get; init; } = Array.Empty<GraphicsAdapterInfo>();

    public IReadOnlyList<StorageDeviceInfo> Storage { get; init; } = Array.Empty<StorageDeviceInfo>();

    public IReadOnlyList<NetworkAdapterInfo> Network { get; init; } = Array.Empty<NetworkAdapterInfo>();

    public IReadOnlyList<AudioDeviceInfo> Audio { get; init; } = Array.Empty<AudioDeviceInfo>();

    public IReadOnlyList<MonitorInfo> Monitors { get; init; } = Array.Empty<MonitorInfo>();

    public IReadOnlyList<PrinterInfo> Printers { get; init; } = Array.Empty<PrinterInfo>();

    public BatteryInfo? Battery { get; init; }

    public IReadOnlyList<PnpDeviceInfo> PnpDevices { get; init; } = Array.Empty<PnpDeviceInfo>();

    public IReadOnlyList<DriverRecord> Drivers { get; init; } = Array.Empty<DriverRecord>();

    public IReadOnlyList<HardwareComponent> Components { get; init; } = Array.Empty<HardwareComponent>();

    public IReadOnlyList<Problem> Problems { get; init; } = Array.Empty<Problem>();

    public IReadOnlyList<ModuleResult> Modules { get; init; } = Array.Empty<ModuleResult>();

    public IReadOnlyList<DetectedChange> Changes { get; init; } = Array.Empty<DetectedChange>();

    public HealthStatus OverallStatus { get; init; } = HealthStatus.Unknown;

    public LocalizedText OverallSummary { get; init; } = LocalizedText.Of("Overall_Unknown");

    public IReadOnlyList<SensorReading> SensorSnapshot { get; init; } = Array.Empty<SensorReading>();

    public WorkloadAssessment Workload { get; init; } = new();

    public bool IsSimulation { get; init; }

    /// <summary>
    /// Reads that failed during the inventory. A snapshot with failed reads is incomplete, and the
    /// report and the UI have to say so instead of presenting the missing data as "nothing found".
    /// </summary>
    public int InventoryFailedReads { get; init; }

    /// <summary>Plain text notes of the inventory, one line per failed read.</summary>
    public IReadOnlyList<string> InventoryNotes { get; init; } = Array.Empty<string>();

    public string ApplicationVersion { get; init; } = string.Empty;

    public ProblemCounts ProblemCounts => ProblemCounts.From(Problems);

    public IReadOnlyList<HardwareComponent> ComponentsOf(ComponentCategory category) =>
        Components.Where(c => c.Category == category).ToList();
}

/// <summary>Outcome of a single diagnostic module.</summary>
public sealed record ModuleResult
{
    public string ModuleId { get; init; } = string.Empty;

    public string DisplayNameKey { get; init; } = "Module_Unknown";

    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    public HealthStatus Status { get; init; } = HealthStatus.Unknown;

    public IReadOnlyList<HardwareComponent> Components { get; init; } = Array.Empty<HardwareComponent>();

    public IReadOnlyList<Problem> Problems { get; init; } = Array.Empty<Problem>();

    public TimeSpan Duration { get; init; }

    public bool WasSkipped { get; init; }

    public string? SkipReasonCode { get; init; }

    public LocalizedText? SkipReason { get; init; }

    /// <summary>Number of individual checks that were actually executed.</summary>
    public int ChecksExecuted { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>Report formats supported by the reporting engine (spec section 56).</summary>
public enum ReportFormat
{
    Html,
    Text,
    Json,
    Pdf,
}

/// <summary>A generated report file.</summary>
public sealed record ReportArtifact
{
    public string FilePath { get; init; } = string.Empty;

    public ReportFormat Format { get; init; }

    public long SizeBytes { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public HashResult Hash { get; init; } = new();

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Report_Created");
}

/// <summary>Query/filter for the history view.</summary>
public sealed record HistoryQuery
{
    public string? Text { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public HealthStatus? MinimumStatus { get; init; }

    public int MaxResults { get; init; } = 200;
}
