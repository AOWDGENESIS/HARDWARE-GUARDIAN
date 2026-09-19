using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Maintenance;

/// <summary>
/// Installed software inventory (spec section 19). Read exclusively from the documented uninstall
/// registry keys of the three hives. There is no file scanning, no licence key reading and no
/// transmission of the list - it stays on the machine.
/// </summary>
public sealed class SoftwareInventoryService : ISoftwareInventoryService
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKeyWow64 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallKeyUser = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private readonly IRegistryAccess _registry;
    private readonly IClock _clock;

    public SoftwareInventoryService(IRegistryAccess registry, IClock clock)
    {
        _registry = registry;
        _clock = clock;
    }

    public Task<IReadOnlyList<InstalledSoftwareRecord>> GetInstalledSoftwareAsync(CancellationToken cancellationToken)
    {
        var now = _clock.Now;
        var origin = ValueOrigin.Registry(now, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        var list = new List<InstalledSoftwareRecord>();

        var scopes = new (RegistryScope Scope, string Path, string HiveLabel, string Architecture)[]
        {
            (RegistryScope.LocalMachine, UninstallKey, "HKLM", "64-bit"),
            (RegistryScope.LocalMachine, UninstallKeyWow64, @"HKLM\WOW6432Node", "32-bit"),
            (RegistryScope.CurrentUser, UninstallKeyUser, "HKCU", "64-bit"),
        };

        foreach (var (scope, path, hiveLabel, architecture) in scopes)
        {
            foreach (var subKey in _registry.EnumerateSubKeys(scope, path))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var keyPath = $@"{path}\{subKey}";
                var displayName = _registry.ReadString(scope, keyPath, "DisplayName", now);
                if (!displayName.IsKnown)
                {
                    // Entries without a display name are technical sub-entries (updates,
                    // components) and are not part of the software list.
                    continue;
                }

                var systemComponent = _registry.ReadInt(scope, keyPath, "SystemComponent", now);
                var isSystemComponent = systemComponent.HasValue && systemComponent.Value!.Value == 1;

                var estimated = _registry.ReadLong(scope, keyPath, "EstimatedSize", now);
                var uninstall = _registry.ReadString(scope, keyPath, "UninstallString", now);

                list.Add(new InstalledSoftwareRecord
                {
                    DisplayName = displayName,
                    Publisher = _registry.ReadString(scope, keyPath, "Publisher", now),
                    Version = _registry.ReadString(scope, keyPath, "DisplayVersion", now),
                    InstallDate = ParseInstallDate(_registry.ReadString(scope, keyPath, "InstallDate", now), origin),
                    EstimatedSizeBytes = estimated.HasValue
                        ? Measured<ulong>.Known((ulong)Math.Max(0, estimated.Value!.Value) * 1024UL, estimated.Origin)
                        : Measured<ulong>.NotAvailable("EstimatedSize is not reported for this entry"),
                    InstallLocation = _registry.ReadString(scope, keyPath, "InstallLocation", now),
                    UninstallStringPresent = TextInfo.Known(uninstall.IsKnown ? "yes" : "no", origin with { Quality = SensorQuality.Limited }),
                    IsSystemComponent = isSystemComponent,
                    RegistryHive = TextInfo.Known(hiveLabel, origin),
                    Architecture = TextInfo.Known(architecture, origin),
                });
            }
        }

        var ordered = list
            .Where(entry => !entry.IsSystemComponent)
            .OrderBy(entry => entry.DisplayName.Display, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Version.Display, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult<IReadOnlyList<InstalledSoftwareRecord>>(ordered);
    }

    /// <summary>Registry install dates use the format <c>yyyyMMdd</c>; anything else stays unknown.</summary>
    private static TextInfo ParseInstallDate(TextInfo raw, ValueOrigin origin)
    {
        if (!raw.IsKnown || raw.Value!.Length != 8)
        {
            return TextInfo.Unknown(origin, raw.UnknownReason ?? "InstallDate is not in the documented yyyyMMdd format");
        }

        return DateTime.TryParseExact(raw.Value, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? TextInfo.Known(parsed.ToString("yyyy-MM-dd"), origin)
            : TextInfo.Unknown(origin, $"InstallDate '{raw.Value}' could not be parsed");
    }
}

/// <summary>
/// Running process inventory (spec section 17). Metadata only: the executable path is masked when
/// the user asked for it, and command lines are never read at all.
/// </summary>
public sealed class ProcessInventoryService : IProcessInventoryService
{
    private readonly IProcessSnapshotProvider _provider;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;

    public ProcessInventoryService(IProcessSnapshotProvider provider, ISettingsService settings, IClock clock)
    {
        _provider = provider;
        _settings = settings;
        _clock = clock;
    }

    public Task<IReadOnlyList<ProcessRecord>> GetRunningProcessesAsync(CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.WindowsApi(_clock.Now, "Process table");
        var maskPaths = _settings.Current.MaskUserNameInReports;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var records = _provider.Snapshot()
            .OrderByDescending(entry => entry.WorkingSetBytes)
            .Select(entry => new ProcessRecord
            {
                Id = entry.Id,
                Name = TextInfo.From(Path.GetFileNameWithoutExtension(entry.Name), origin, "process name not readable"),
                ExecutablePath = TextInfo.From(Mask(entry.ExecutablePath, maskPaths, profile), origin, "executable path is not accessible for this process"),
                WorkingSetBytes = entry.WorkingSetBytes > 0
                    ? Measured<ulong>.Known((ulong)entry.WorkingSetBytes, origin)
                    : Measured<ulong>.NotAvailable("working set is not readable"),
                PrivateBytes = entry.PrivateBytes > 0
                    ? Measured<ulong>.Known((ulong)entry.PrivateBytes, origin)
                    : Measured<ulong>.NotAvailable("private bytes are not readable"),
                Responding = entry.Responding,
                SessionId = entry.SessionId,
                StartTime = entry.StartTime,
                CanInstallDrivers = WorkloadSignatures.IsInstaller(entry.Name),
                Category = WorkloadSignatures.ClassifyProcess(entry.Name),
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<ProcessRecord>>(records);
    }

    private static string? Mask(string? path, bool mask, string userProfile)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (!mask)
        {
            return path;
        }

        return !string.IsNullOrEmpty(userProfile) && path.StartsWith(userProfile, StringComparison.OrdinalIgnoreCase)
            ? "%USERPROFILE%" + path[userProfile.Length..]
            : path;
    }
}
