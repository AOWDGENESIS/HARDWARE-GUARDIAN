using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Abstractions;

/// <summary>Reads build metadata that was embedded at compile time (spec section 58).</summary>
public interface IBuildInfoProvider
{
    AppBuildInfo Get();
}

/// <summary>How an adapter is allowed to establish the authenticity of its source.</summary>
public enum VerificationMethod
{
    /// <summary>No automated verification possible. The source is presented as a documented link only.</summary>
    None = 0,

    /// <summary>The official landing page was reachable over HTTPS.</summary>
    HttpsReachability = 1,

    /// <summary>Metadata on the page was matched against local hardware identifiers.</summary>
    MetadataMatch = 2,

    /// <summary>A documented, stable vendor API was queried.</summary>
    VendorApi = 3,

    /// <summary>Values come from a versioned catalogue that is shipped and updated with the app.</summary>
    PinnedCatalog = 4,
}

/// <summary>An official source that an adapter may use. Never invented, never a driver portal.</summary>
public sealed record ManufacturerSource
{
    public string Id { get; init; } = string.Empty;

    public string DisplayNameKey { get; init; } = "Source_Unknown";

    /// <summary>Official product/support landing page. Empty when no official page is known.</summary>
    public string LandingUrl { get; init; } = string.Empty;

    public string SourceTypeKey { get; init; } = "SourceType_Unknown";

    public VerificationMethod Method { get; init; } = VerificationMethod.None;

    /// <summary>Highest verification level that this source can reach without manual work.</summary>
    public VerificationLevel BaselineVerification { get; init; } = VerificationLevel.NotVerified;

    /// <summary>True when a human must confirm the match on the manufacturer page.</summary>
    public bool RequiresManualVerification { get; init; } = true;

    public string? Notes { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(LandingUrl);
}

/// <summary>
/// A manufacturer adapter (spec sections 11 and 47). Adapters only use official sources and
/// must state clearly when no automated check is possible.
/// </summary>
public interface IManufacturerAdapter
{
    string Id { get; }

    string DisplayNameKey { get; }

    SourceTrust Trust { get; }

    IReadOnlyList<ComponentCategory> Categories { get; }

    /// <summary>PCI/USB vendor identifiers, e.g. <c>10DE</c> for NVIDIA, <c>1002</c> for AMD.</summary>
    IReadOnlyList<string> VendorIds { get; }

    /// <summary>Name fragments used to resolve a manufacturer string to this adapter.</summary>
    IReadOnlyList<string> NamePatterns { get; }

    ManufacturerSource? Source { get; }

    /// <summary>False for the majority of OEMs: their update data is not machine readable.</summary>
    bool SupportsAutomatedCheck { get; }

    /// <summary>True when the adapter may only be used online.</summary>
    bool RequiresNetwork { get; }

    Task<SourceCheckResult> CheckAsync(ManufacturerQuery query, CancellationToken cancellationToken);
}

/// <summary>Everything an adapter may look at when answering a query.</summary>
public sealed record ManufacturerQuery
{
    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    public HardwareComponent? Component { get; init; }

    public ProcessorInfo? Processor { get; init; }

    public GraphicsAdapterInfo? Graphics { get; init; }

    public StorageDeviceInfo? Storage { get; init; }

    public MotherboardInfo? Motherboard { get; init; }

    public BiosIdentification? Bios { get; init; }

    public DriverRecord? Driver { get; init; }

    public string? Manufacturer { get; init; }

    public string? Model { get; init; }

    public string? HardwareId { get; init; }

    public bool Offline { get; init; }
}

/// <summary>Resolves manufacturers to adapters and describes their official sources.</summary>
public interface IManufacturerResolver
{
    IReadOnlyList<IManufacturerAdapter> Adapters { get; }

    IManufacturerAdapter? Resolve(string? manufacturer, string? model, ComponentCategory category);

    IManufacturerAdapter? ResolveByHardwareId(string? hardwareId);

    ManufacturerSourceRef DescribeSource(IManufacturerAdapter? adapter, VerificationLevel achieved = VerificationLevel.NotVerified);
}

/// <summary>Update centre: compares installed versions with verified official data (spec sections 13, 36).</summary>
public interface IUpdateCenter
{
    IReadOnlyList<SourceCheckResult> LastResults { get; }

    event EventHandler<UpdateAssessment>? AssessmentCompleted;

    Task<IReadOnlyList<UpdateAssessment>> EvaluateAsync(SystemSnapshot snapshot, IProgressReporter progress, CancellationToken cancellationToken);

    Task<SourceCheckResult> CheckAdapterAsync(string adapterId, SystemSnapshot snapshot, CancellationToken cancellationToken);

    Task<UpdateAssessment> EvaluateComponentAsync(HardwareComponent component, SystemSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>Driver inventory, health analysis and before/after snapshots.</summary>
public interface IDriverInventoryService
{
    Task<DriverStateSnapshot> CaptureAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Problem>> AnalyzeAsync(DriverStateSnapshot snapshot, CancellationToken cancellationToken);

    Task<IReadOnlyList<HardwareComponent>> BuildComponentsAsync(DriverStateSnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>Compares two snapshots and lists what actually changed.</summary>
    IReadOnlyList<DetectedChange> Compare(DriverStateSnapshot older, DriverStateSnapshot newer);
}

/// <summary>BIOS/UEFI detection, assessment and firmware file inspection.</summary>
public interface IBiosService
{
    Task<FirmwareAssessment> AssessUpdateAsync(SystemSnapshot snapshot, CancellationToken cancellationToken);

    Task<FirmwareFileAssessment> InspectFirmwareFileAsync(string filePath, SystemSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>Windows health, updates, integrity checks and startup analysis.</summary>
public interface IWindowsHealthService
{
    Task<WindowsHealthReport> AssessAsync(SystemSnapshot? snapshot, bool includeOnlineChecks, IProgressReporter progress, CancellationToken cancellationToken);

    /// <summary>
    /// Component store check. A repair needs the approval record of the confirmed operation: the
    /// service refuses to change the system without it (fail closed), it does not trust the caller.
    /// </summary>
    Task<IntegrityCheckResult> RunComponentStoreCheckAsync(bool repair, ApprovalRecord? approval, IProgressReporter progress, CancellationToken cancellationToken);

    /// <summary>
    /// System file integrity check. Verification runs as the read-only <c>sfc.exe /verifyonly</c>;
    /// the repair is deliberately not automated and always answers BLOCKED with that reason.
    /// </summary>
    Task<IntegrityCheckResult> RunSystemFileCheckAsync(bool repair, ApprovalRecord? approval, IProgressReporter progress, CancellationToken cancellationToken);

    Task<UpdateAvailability> CheckUpdateAvailabilityAsync(bool queryOnline, CancellationToken cancellationToken);

    IReadOnlyList<DetectedWorkloadSnapshot> DetectWorkloads(IEnumerable<ServiceStartupInfo> startup);

    Task<IReadOnlyList<ServiceStartupInfo>> GetStartupAsync(CancellationToken cancellationToken);
}

/// <summary>A workload marker used by the workload aware optimisation (spec section 19).</summary>
public sealed record DetectedWorkloadSnapshot(string Id, bool Detected, string Evidence, bool MustNotBeDisturbed);

/// <summary>Live sensor service (spec section 35).</summary>
public interface ISensorService
{
    IReadOnlyList<SensorProviderInfo> Providers { get; }

    bool IsMonitoring { get; }

    TimeSpan Interval { get; }

    event EventHandler<SensorSnapshot>? Sampled;

    Task<SensorSnapshot> SampleAsync(CancellationToken cancellationToken);

    void StartMonitoring(TimeSpan interval);

    void StopMonitoring();
}

/// <summary>A pluggable sensor source. Every provider must state its quality and measurement point.</summary>
public interface ISensorProvider
{
    SensorProviderInfo Info { get; }

    Task<IReadOnlyList<SensorReading>> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Maintenance scan and execution (spec sections 18, 77, 78).</summary>
public sealed record MaintenanceScanOptions
{
    public bool IncludeBrowserCache { get; init; }

    public bool IncludeWindowsUpdateCache { get; init; }

    public bool IncludePrefetch { get; init; }

    public bool IncludeProtectedCategories { get; init; }

    public int MaxDepth { get; init; } = 6;
}

public interface IMaintenanceService
{
    IReadOnlyList<MaintenanceCategoryDescriptor> DescribeCategories();

    Task<MaintenanceScanResult> ScanAsync(MaintenanceScanOptions options, CancellationToken cancellationToken);

    Task<MaintenancePlan> BuildPlanAsync(MaintenanceScanResult scan, IReadOnlyList<MaintenanceCategory> selection, ExecutionMode mode, CancellationToken cancellationToken);

    Task<MaintenanceResult> ExecuteAsync(MaintenancePlan plan, ApprovalRecord approval, CancellationToken cancellationToken);

    Task<MaintenanceResult> ExecuteDryRunAsync(MaintenancePlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the backup a plan requires and records it for exactly the locations of that plan.
    /// The documented order is BACKUP before USER APPROVAL and EXECUTE (spec section 44), so this
    /// must run before <see cref="ExecuteAsync"/> whenever <c>plan.BackupRequired</c> is true.
    /// Fails closed: without a backup service, without a sufficient backup, or for a plan that
    /// needs none, it throws <c>OperationBlockedException</c> instead of pretending a backup exists.
    /// </summary>
    Task<BackupRecord> RecordBackupAsync(MaintenancePlan plan, CancellationToken cancellationToken);
}

/// <summary>Static description of a maintenance category with its safety class.</summary>
public sealed record MaintenanceCategoryDescriptor
{
    public MaintenanceCategory Category { get; init; }

    public string DisplayNameKey { get; init; } = "Maintenance_Unknown";

    public LocalizedText Description { get; init; } = LocalizedText.Of("Maintenance_Unknown_Description");

    public SafetyClass SafetyClass { get; init; } = SafetyClass.Unknown;

    public bool RequiresAdministrator { get; init; }

    public bool IsOptIn { get; init; }

    public LocalizedText? Restriction { get; init; }
}

/// <summary>Workload aware optimisation proposals (spec section 19).</summary>
public interface IWorkloadDetector
{
    Task<WorkloadAssessment> DetectAsync(SystemSnapshot? snapshot, CancellationToken cancellationToken);
}

/// <summary>Report generation (spec sections 56 and 57).</summary>
public sealed record ReportOptions
{
    public string? OutputDirectory { get; init; }

    public string? FileNameHint { get; init; }

    public bool MaskSerialNumbers { get; init; } = true;

    public bool MaskUserName { get; init; } = true;

    public bool IncludeEvidence { get; init; } = true;

    public LanguagePreference? Language { get; init; }
}

public sealed record SecurityReportData
{
    public IReadOnlyList<SignatureResult> Signatures { get; init; } = Array.Empty<SignatureResult>();

    public IReadOnlyList<SourceVerificationResult> Sources { get; init; } = Array.Empty<SourceVerificationResult>();

    public IReadOnlyList<HashResult> Hashes { get; init; } = Array.Empty<HashResult>();

    public IReadOnlyList<Problem> Findings { get; init; } = Array.Empty<Problem>();

    public IReadOnlyList<BlockedOperation> BlockedOperations { get; init; } = Array.Empty<BlockedOperation>();

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

public sealed record ReportRequest
{
    public SystemSnapshot? Snapshot { get; init; }

    public IReadOnlyList<UpdateAssessment> Updates { get; init; } = Array.Empty<UpdateAssessment>();

    public IReadOnlyList<MaintenanceResult> Maintenance { get; init; } = Array.Empty<MaintenanceResult>();

    public IReadOnlyList<HistoryEntry> History { get; init; } = Array.Empty<HistoryEntry>();

    public IReadOnlyList<AuditEntry> Audit { get; init; } = Array.Empty<AuditEntry>();

    public IReadOnlyList<SensorReading> Sensors { get; init; } = Array.Empty<SensorReading>();

    public SecurityReportData? Security { get; init; }

    public bool IsSecurityReport { get; init; }
}

public interface IReportGenerator
{
    Task<ReportArtifact> GenerateAsync(ReportRequest request, ReportFormat format, ReportOptions options, CancellationToken cancellationToken);

    IReadOnlyList<ReportFormat> SupportedFormats { get; }
}

/// <summary>Cache for manufacturer data with explicit freshness (spec sections 64, 65).</summary>
public interface ISourceCacheStore
{
    string Location { get; }

    Task<SourceCacheEntry?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(SourceCacheEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceCacheEntry>> ListAsync(CancellationToken cancellationToken);

    Task ClearAsync(CancellationToken cancellationToken);
}
