using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Models;

/// <summary>
/// One entry of the installed software inventory (spec section 19). Read exclusively from the
/// documented uninstall registry keys. No licence keys, no user documents, no file scanning.
/// </summary>
public sealed record InstalledSoftwareRecord
{
    public TextInfo DisplayName { get; init; }

    public TextInfo Publisher { get; init; }

    public TextInfo Version { get; init; }

    public TextInfo InstallDate { get; init; }

    public Measured<ulong> EstimatedSizeBytes { get; init; } = Measured<ulong>.NotAvailable("EstimatedSize not reported");

    public TextInfo InstallLocation { get; init; }

    /// <summary>Whether an uninstall command exists. The command itself is never executed.</summary>
    public TextInfo UninstallStringPresent { get; init; }

    /// <summary>True when the entry is flagged as a system component and is therefore hidden.</summary>
    public bool IsSystemComponent { get; init; }

    /// <summary>Which registry hive the entry came from. Documented, never a guess.</summary>
    public TextInfo RegistryHive { get; init; }

    /// <summary>Reported architecture of the entry (32-bit or 64-bit), as derived from the hive.</summary>
    public TextInfo Architecture { get; init; }

    /// <summary>Problem identifier when this entry is part of a finding, otherwise null.</summary>
    public string? ProblemId { get; init; }
}

/// <summary>Coarse classification of a process, used to avoid disturbing important work.</summary>
public enum ProcessCategory
{
    Unknown = 0,
    System,
    User,
    Installer,
    Security,
}

/// <summary>
/// A running process (spec section 17). Only process metadata is read; command lines are never
/// recorded in reports and paths are masked when masking is enabled.
/// </summary>
public sealed record ProcessRecord
{
    public int Id { get; init; }

    public TextInfo Name { get; init; }

    public TextInfo ExecutablePath { get; init; }

    public Measured<ulong> WorkingSetBytes { get; init; } = Measured<ulong>.NotAvailable("working set not readable");

    public Measured<ulong> PrivateBytes { get; init; } = Measured<ulong>.NotAvailable("private bytes not readable");

    public bool? Responding { get; init; }

    public int SessionId { get; init; }

    public DateTimeOffset? StartTime { get; init; }

    /// <summary>True when the process is able to install drivers (installer/firmware updater).</summary>
    public bool CanInstallDrivers { get; init; }

    public ProcessCategory Category { get; init; } = ProcessCategory.Unknown;
}

/// <summary>
/// Raw process snapshot as delivered by the platform. Keeping the raw shape separate from
/// <see cref="ProcessRecord"/> makes the analysis testable without a running process table.
/// </summary>
public sealed record ProcessSnapshotEntry
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? ExecutablePath { get; init; }

    public long WorkingSetBytes { get; init; }

    public long PrivateBytes { get; init; }

    public bool? Responding { get; init; }

    public int SessionId { get; init; }

    public DateTimeOffset? StartTime { get; init; }
}

/// <summary>State of the storage stack for one volume, as reported by the platform.</summary>
public sealed record VolumeStateCapture
{
    public string Root { get; init; } = string.Empty;

    public Measured<ulong> FreeBytes { get; init; } = Measured<ulong>.NotAvailable("free space not reported");

    public Measured<ulong> SizeBytes { get; init; } = Measured<ulong>.NotAvailable("volume size not reported");

    public DateTimeOffset CapturedAt { get; init; }
}
