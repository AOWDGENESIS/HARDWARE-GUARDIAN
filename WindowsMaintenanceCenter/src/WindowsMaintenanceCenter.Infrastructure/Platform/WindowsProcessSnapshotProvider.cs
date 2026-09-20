using System.Diagnostics;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;

namespace WindowsMaintenanceCenter.Infrastructure.Platform;

/// <summary>
/// Process table for the workload detection and the process inventory (spec section 19).
///
/// Read only and defensive: a process that denies access is skipped instead of aborting the whole
/// snapshot, because an incomplete list is still useful for workload detection whereas an exception
/// would hide every workload.
/// </summary>
public sealed class WindowsProcessSnapshotProvider : IProcessSnapshotProvider
{
    public IReadOnlyList<ProcessSnapshotEntry> Snapshot()
    {
        var result = new List<ProcessSnapshotEntry>();

        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var entry = new ProcessSnapshotEntry
                {
                    Id = process.Id,
                    Name = SafeName(process),
                    ExecutablePath = SafePath(process),
                    WorkingSetBytes = SafeCounter(() => process.WorkingSet64),
                    PrivateBytes = SafeCounter(() => process.PrivateMemorySize64),
                    Responding = SafeResponding(process),
                    SessionId = SafeSessionId(process),
                    StartTime = SafeStartTime(process),
                };

                result.Add(entry);
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                // The process ended while it was being read - that is not an error worth reporting.
            }
            finally
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static string SafeName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    private static string? SafePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            // Protected processes do not expose their path; the entry stays without a path.
            return null;
        }
    }

    private static long SafeCounter(Func<long> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }

    private static bool? SafeResponding(Process process)
    {
        try
        {
            return process.Responding;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static int SafeSessionId(Process process)
    {
        try
        {
            return process.SessionId;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return 0;
        }
    }

    private static DateTimeOffset? SafeStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
