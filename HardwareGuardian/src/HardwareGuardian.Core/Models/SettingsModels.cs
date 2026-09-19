using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Models;

/// <summary>
/// Persisted user settings (spec sections 40 and 72). All defaults are the safe defaults:
/// no telemetry, dry run first, confirmations on, no data leaves the machine.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Settings file schema version, used for migrations.</summary>
    public int SchemaVersion { get; init; } = 1;

    public LanguagePreference Language { get; init; } = LanguagePreference.System;

    public ThemePreference Theme { get; init; } = ThemePreference.Dark;

    /// <summary>Technical log level (Trace, Debug, Information, Warning, Error, Critical).</summary>
    public string LogLevel { get; init; } = "Information";

    public int LogRetentionDays { get; init; } = 30;

    public int CacheRetentionDays { get; init; } = 7;

    public int HistoryRetentionEntries { get; init; } = 500;

    /// <summary>Allows the application to contact official sources at all.</summary>
    public bool UpdateCheckEnabled { get; init; } = true;

    public bool UpdateCheckOnStartup { get; init; } = false;

    /// <summary>Telemetry is off and cannot be enabled: the application has no telemetry endpoint (spec section 28).</summary>
    public bool TelemetryEnabled { get; init; }

    public bool ManufacturerSourcesEnabled { get; init; } = true;

    /// <summary>Uses vendor command line tools (for example nvidia-smi) when they are installed.</summary>
    public bool UseVendorTools { get; init; } = true;

    public int SensorIntervalMilliseconds { get; init; } = 2000;

    public bool NotificationsEnabled { get; init; } = true;

    public bool LiveProtocolVisible { get; init; } = true;

    public ConfirmationPolicy ConfirmationPolicy { get; init; } = ConfirmationPolicy.AlwaysConfirm;

    public ExecutionMode DefaultExecutionMode { get; init; } = ExecutionMode.DryRun;

    /// <summary>Browser cache maintenance is opt in because it touches a user profile directory.</summary>
    public bool MaintenanceIncludeBrowserCache { get; init; }

    public bool MaintenanceIncludeWindowsUpdateCache { get; init; }

    public bool IncludePrefetchedData { get; init; }

    public string? ReportDirectory { get; init; }

    public bool MaskSerialNumbersInReports { get; init; } = true;

    public bool MaskUserNameInReports { get; init; } = true;

    public bool IncludeEvidenceInReports { get; init; } = true;

    public bool DetailedDiagnostics { get; init; }

    public string? LastPage { get; init; }

    public DateTimeOffset? LastFullScanUtc { get; init; }

    public DateTimeOffset? LastUpdateCheckUtc { get; init; }

    /// <summary>True when the application was started in simulation mode at least once (diagnostic hint).</summary>
    public bool LastRunWasSimulation { get; init; }

    public AppSettings WithLastFullScan(DateTimeOffset timestamp) => this with { LastFullScanUtc = timestamp };

    public AppSettings WithLastUpdateCheck(DateTimeOffset timestamp) => this with { LastUpdateCheckUtc = timestamp };
}

/// <summary>Build and runtime information shown on the about page (spec section 58).</summary>
public sealed record AppBuildInfo
{
    public string Version { get; init; } = "0.0.0";

    public string Commit { get; init; } = "unknown";

    public string BuildDate { get; init; } = "unspecified";

    public string Configuration { get; init; } = "unknown";

    public string TargetFramework { get; init; } = "unknown";

    public string RuntimeIdentifier { get; init; } = "unknown";

    public string ProductName { get; init; } = "Hardware Guardian";

    public string ProductTagline { get; init; } = string.Empty;

    public string RepositoryUrl { get; init; } = string.Empty;

    public string DotNetVersion { get; init; } = string.Empty;

    public bool IsPortable { get; init; }

    public bool IsSimulation { get; init; }

    public bool IsOffline { get; init; }

    public SessionPrivilege Privilege { get; init; } = SessionPrivilege.Unknown;

    public string DataRoot { get; init; } = string.Empty;

    public IReadOnlyList<string> CommandLineArguments { get; init; } = Array.Empty<string>();

    public LocalizedText PrivilegeSummary => Privilege == SessionPrivilege.Administrator
        ? LocalizedText.Of("Build_Privilege_Administrator")
        : LocalizedText.Of("Build_Privilege_StandardUser");
}
