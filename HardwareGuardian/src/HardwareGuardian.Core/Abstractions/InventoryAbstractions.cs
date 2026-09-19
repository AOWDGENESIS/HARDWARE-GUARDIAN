using HardwareGuardian.Core.Models;

namespace HardwareGuardian.Core.Abstractions;

/// <summary>
/// Reads the installed software from the documented uninstall keys (spec section 19).
/// The service is read only: it never repairs, updates or removes software.
/// </summary>
public interface ISoftwareInventoryService
{
    Task<IReadOnlyList<InstalledSoftwareRecord>> GetInstalledSoftwareAsync(CancellationToken cancellationToken);
}

/// <summary>Reads the running processes. Metadata only, no command lines, no window titles.</summary>
public interface IProcessInventoryService
{
    Task<IReadOnlyList<ProcessRecord>> GetRunningProcessesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Platform-provided process table. Implemented in the Windows layer; tests use a fixture
/// implementation. Kept separate so that process analysis contains no Win32 calls itself.
/// </summary>
public interface IProcessSnapshotProvider
{
    /// <summary>Returns the current process table. Never throws: an unreadable process is skipped.</summary>
    IReadOnlyList<ProcessSnapshotEntry> Snapshot();
}

// IMaintenanceService lives in FeatureAbstractions.cs - deliberately not repeated here, so that
// exactly one declaration of the maintenance contract exists in the code base.
