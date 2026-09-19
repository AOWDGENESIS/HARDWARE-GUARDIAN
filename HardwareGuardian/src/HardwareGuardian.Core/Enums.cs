namespace HardwareGuardian.Core;

/// <summary>
/// Overall or per-component health. There is deliberately no numeric score:
/// a single number cannot be justified with the available evidence (see docs/ARCHITECTURE.md).
/// </summary>
public enum HealthStatus
{
    Unknown = 0,
    Healthy = 1,
    Attention = 2,
    Warning = 3,
    Critical = 4,
}

/// <summary>Severity of a protocol line, problem or finding (spec section 22).</summary>
public enum Severity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3,
    Critical = 4,
    Blocked = 5,
}

/// <summary>Central state machine states (spec section 5).</summary>
public enum SystemState
{
    Idle,
    Scanning,
    Analyzing,
    CheckingUpdates,
    WaitingForApproval,
    BackupRequired,
    Executing,
    Verifying,
    Success,
    Warning,
    Error,
    Blocked,
    Rollback,
    Cancelled,
}

/// <summary>Risk classification that drives backup level and approval requirement (spec section 25).</summary>
public enum RiskLevel
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

/// <summary>Lifecycle of a tracked problem (spec section 67).</summary>
public enum ProblemStatus
{
    Open,
    Acknowledged,
    Resolved,
    Blocked,
    Ignored,
}

/// <summary>Comparison result of the update engine (spec section 13).</summary>
public enum UpdateStatus
{
    Current,
    UpdateAvailable,
    Optional,
    Warning,
    Incompatible,
    Unknown,
    Blocked,
}

/// <summary>Where a value or an update actually comes from (spec section 12).</summary>
public enum DataSource
{
    Unknown,
    LocalData,
    Wmi,
    Registry,
    WindowsApi,
    Smbios,
    Pnp,
    FileSystem,
    WindowsUpdate,
    MicrosoftUpdateCatalog,
    OfficialManufacturer,
    VerifiedOem,
    VendorTool,
    UserProvided,
    ThirdParty,
}

/// <summary>Trust class of an update source. Order defines the priority used by the update engine (spec section 12).</summary>
public enum SourceTrust
{
    ThirdParty = 0,
    Unknown = 1,
    VerifiedOem = 2,
    WindowsUpdate = 3,
    Microsoft = 4,
    Manufacturer = 5,
}

/// <summary>How reliable a sensor value is. Shown in the UI next to every measurement (spec section 8).</summary>
public enum SensorQuality
{
    High = 0,
    Medium = 1,
    Limited = 2,
    Unknown = 3,
}

/// <summary>Freshness of cached, externally retrieved information (spec section 65).</summary>
public enum Freshness
{
    Live,
    Recent,
    Cached,
    Stale,
    Unknown,
}

/// <summary>How far an external fact has actually been verified (spec section 48).</summary>
public enum VerificationLevel
{
    NotVerified = 0,
    SourceReachable = 1,
    MetadataMatch = 2,
    HashMatch = 3,
    SignatureVerified = 4,
    PinnedCatalog = 5,
}

/// <summary>Safety class of a maintenance category (spec section 18).</summary>
public enum SafetyClass
{
    Safe,
    Optional,
    Protected,
    Unknown,
}

/// <summary>Component grouping used by the dashboard, problem centre and reports.</summary>
public enum ComponentCategory
{
    Unknown = 0,
    System,
    Cpu,
    Motherboard,
    Chipset,
    Bios,
    Firmware,
    Memory,
    Graphics,
    Storage,
    Network,
    Audio,
    Usb,
    Pci,
    Monitor,
    Printer,
    Battery,
    Sensor,
    Driver,
    Windows,
    Update,
    Maintenance,
    Security,
}

/// <summary>Kind of operation, used for audit logging and backup level selection.</summary>
public enum OperationKind
{
    Detect,
    Verify,
    Analyze,
    Backup,
    Execute,
    VerifyResult,
    Rollback,
    Download,
    Report,
    Maintenance,
    DriverUpdate,
    FirmwareUpdate,
    Service,
    Startup,
    FileDeletion,
    Repair,
    Optimization,
}

/// <summary>Outcome of an approval request.</summary>
public enum ApprovalDecision
{
    Pending,
    Approved,
    Rejected,
    Expired,
    Cancelled,
}

/// <summary>Elevation state of the running process (spec section 52).</summary>
public enum SessionPrivilege
{
    StandardUser,
    Administrator,
    Unknown,
}

/// <summary>Theme preference (spec section 31).</summary>
public enum ThemePreference
{
    Dark,
    Light,
    System,
}

/// <summary>Language preference (spec section 31).</summary>
public enum LanguagePreference
{
    System,
    German,
    English,
}

/// <summary>When the application asks for confirmation (spec section 24).</summary>
public enum ConfirmationPolicy
{
    /// <summary>Ask for every write operation.</summary>
    AlwaysConfirm = 0,

    /// <summary>Ask for medium risk and above; low risk operations still show a preview.</summary>
    ConfirmMediumAndAbove = 1,

    /// <summary>Only ever plan, never execute. Dry run mode for administrators who want evidence only.</summary>
    DryRunOnly = 2,
}

/// <summary>Known maintenance categories (spec sections 18 and 77).</summary>
public enum MaintenanceCategory
{
    TemporaryFiles,
    WindowsUpdateCache,
    DeliveryOptimizationCache,
    RecycleBin,
    CrashDumps,
    OldLogs,
    ThumbnailCache,
    ShaderCache,
    BrowserCache,
    InstallerLeftovers,
    PrefetchedData,
    ErrorReports,
    WindowsInstallerCache,
}

/// <summary>Execution mode of a maintenance or update action (spec section 78).</summary>
public enum ExecutionMode
{
    DryRun,
    Execute,
}

/// <summary>What kind of artefact an update candidate represents.</summary>
public enum UpdateKind
{
    Driver,
    Bios,
    Firmware,
    WindowsUpdate,
    ChipsetPackage,
    VendorTool,
}

/// <summary>A single step of the safety pipeline (spec section 1.1).</summary>
public enum DiagnosticStage
{
    Detect,
    Verify,
    Analyze,
    Backup,
    Approval,
    Execute,
    VerifyResult,
    Rollback,
}

/// <summary>Outcome of a single stage.</summary>
public enum StageOutcome
{
    NotRun,
    Running,
    Succeeded,
    Failed,
    Skipped,
    Blocked,
    Cancelled,
}

/// <summary>Workload classification for workload aware optimisation (spec section 19).</summary>
public enum WorkloadProfile
{
    Unknown,
    GeneralOffice,
    DeveloperWorkstation,
    LocalAiWorkstation,
    VirtualizationHost,
    Gaming,
    ServerLike,
}

/// <summary>How a cached manufacturer fact may be used.</summary>
public enum CacheDisposition
{
    /// <summary>Fetched within the freshness window; may be shown as current.</summary>
    Usable,

    /// <summary>Older than the freshness window but still reported, clearly marked as stale.</summary>
    Stale,

    /// <summary>Too old to be used at all; must be refreshed before a decision is made.</summary>
    Expired,
}

/// <summary>Reason codes for blocked operations. They are stable machine identifiers.</summary>
public static class BlockReasons
{
    public const string ManufacturerSourceUnknown = "SOURCE_UNKNOWN";
    public const string OfficialSourceUnreachable = "SOURCE_UNREACHABLE";
    public const string ManufacturerSourceNotVerifiable = "SOURCE_NOT_VERIFIABLE";
    public const string BoardRevisionUnknown = "BOARD_REVISION_UNKNOWN";
    public const string HardwareMatchNotProven = "HARDWARE_MATCH_NOT_PROVEN";
    public const string CompatibilityUnknown = "COMPATIBILITY_UNKNOWN";
    public const string SignatureNotVerified = "SIGNATURE_NOT_VERIFIED";
    public const string HashMismatch = "HASH_MISMATCH";
    public const string NotElevated = "ADMINISTRATOR_REQUIRED";
    public const string RecoveryNotAvailable = "RECOVERY_NOT_AVAILABLE";
    public const string DryRunOnly = "DRY_RUN_ONLY";
    public const string UserRejected = "USER_REJECTED";
    public const string SimulationMode = "SIMULATION_MODE";
    public const string ThirdPartySource = "THIRD_PARTY_SOURCE";
    public const string OfflineMode = "OFFLINE_MODE";
    public const string UnsupportedPlatform = "UNSUPPORTED_PLATFORM";
    public const string FirmwareFlashNotAutomated = "FIRMWARE_FLASH_NOT_AUTOMATED";
    public const string ProtectionActive = "PROTECTED_CATEGORY";
}
