using System.ComponentModel;
using System.Diagnostics;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Infrastructure.Platform;

/// <summary>
/// Requests administrator rights for a single operation by restarting the application elevated
/// (spec section 52). The application itself never runs permanently elevated and never tries to
/// bypass user account control.
/// </summary>
public sealed class ElevationService : IElevationService
{
    private readonly IEnvironmentProbe _environment;

    public ElevationService(IEnvironmentProbe environment)
    {
        _environment = environment;
    }

    public bool IsElevated => _environment.IsElevated;

    public LocalizedText ExplainRequirement(string reasonCode) => new(
        reasonCode switch
        {
            "ADMIN_SYSTEM_INTEGRITY" => "Admin_Reason_SystemIntegrity",
            "ADMIN_WINDOWS_UPDATE_CACHE" => "Admin_Reason_UpdateCache",
            "ADMIN_DELIVERY_OPTIMIZATION" => "Admin_Reason_DeliveryOptimization",
            "ADMIN_REGISTRY_WRITE" => "Admin_Reason_RegistryWrite",
            "ADMIN_SERVICE_CHANGE" => "Admin_Reason_ServiceChange",
            "ADMIN_RESTORE_POINT" => "Admin_Reason_RestorePoint",
            "ADMIN_DRIVER_OPERATION" => "Admin_Reason_DriverOperation",
            _ => "Admin_Reason_Generic",
        });

    public Task<bool> RestartElevatedAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || IsElevated)
        {
            return Task.FromResult(IsElevated);
        }

        var host = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(host))
        {
            return Task.FromResult(false);
        }

        try
        {
            var arguments = _environment.CommandLineArguments
                .Where(a => !a.Equals("--elevated", StringComparison.OrdinalIgnoreCase))
                .ToList();
            arguments.Add("--elevated");

            var startInfo = new ProcessStartInfo
            {
                FileName = host,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(' ', arguments.Select(Quote)),
                WorkingDirectory = AppContext.BaseDirectory,
            };

            var process = Process.Start(startInfo);
            return Task.FromResult(process is not null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user declined the elevation prompt. This is a normal outcome, not an error.
            return Task.FromResult(false);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    private static string Quote(string value) =>
        value.Contains(' ', StringComparison.Ordinal) ? '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"' : value;
}
