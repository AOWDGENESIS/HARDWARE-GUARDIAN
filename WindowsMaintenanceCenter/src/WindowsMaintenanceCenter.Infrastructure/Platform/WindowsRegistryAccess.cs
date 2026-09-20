using System.Security;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Values;
using Microsoft.Win32;

namespace WindowsMaintenanceCenter.Infrastructure.Platform;

/// <summary>
/// Registry access limited to what Windows Maintenance Center needs (spec section 18: there is no
/// "registry cleaning"). Reads never throw; writes are restricted to an allow list of keys whose
/// previous state was backed up by the calling action.
/// </summary>
public sealed class WindowsRegistryAccess : IRegistryAccess
{
    /// <summary>Keys that Windows Maintenance Center is allowed to write to.</summary>
    public const string OwnKeyRoot = @"Software\WindowsMaintenanceCenter";
    public const string UserRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string MachineRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ServicesKeyPrefix = @"SYSTEM\CurrentControlSet\Services\";

    private readonly IClock _clock;

    public WindowsRegistryAccess(IClock clock)
    {
        _clock = clock;
    }

    public TextInfo ReadString(RegistryScope scope, string subKey, string valueName, DateTimeOffset retrievedAt)
    {
        var origin = ValueOrigin.Registry(retrievedAt, $"{scope}\\{subKey}\\{valueName}");
        if (!OperatingSystem.IsWindows())
        {
            return TextInfo.Unknown(origin, "registry is only available on Windows");
        }

        try
        {
            // The base keys (HKLM/HKCU/HKU) are shared static RegistryKey instances: never dispose them.
            var baseKey = GetBaseKey(scope);
            if (baseKey is null)
            {
                return TextInfo.Unknown(origin, $"unsupported registry scope {scope}");
            }

            using var key = baseKey.OpenSubKey(subKey, writable: false);
            if (key is null)
            {
                return TextInfo.Unknown(origin, "key does not exist");
            }

            var raw = string.IsNullOrEmpty(valueName) ? key.GetValue(null) : key.GetValue(valueName);
            if (raw is null)
            {
                return TextInfo.Unknown(origin, "value does not exist");
            }

            var text = raw switch
            {
                string s => s,
                string[] array => string.Join("; ", array),
                byte[] bytes => Convert.ToHexString(bytes),
                int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => raw.ToString(),
            };

            return TextInfo.From(text, origin);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException or ObjectDisposedException)
        {
            return TextInfo.Unknown(origin, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public Measured<int> ReadInt(RegistryScope scope, string subKey, string valueName, DateTimeOffset retrievedAt)
    {
        var origin = ValueOrigin.Registry(retrievedAt, $"{scope}\\{subKey}\\{valueName}");
        var text = ReadString(scope, subKey, valueName, retrievedAt);
        if (!text.IsKnown)
        {
            return Measured<int>.NotAvailable(text.UnknownReason ?? "value not readable", origin);
        }

        return int.TryParse(text.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Measured<int>.Known(value, origin)
            : Measured<int>.NotAvailable("value is not an integer", origin);
    }

    public Measured<long> ReadLong(RegistryScope scope, string subKey, string valueName, DateTimeOffset retrievedAt)
    {
        var origin = ValueOrigin.Registry(retrievedAt, $"{scope}\\{subKey}\\{valueName}");
        var text = ReadString(scope, subKey, valueName, retrievedAt);
        if (!text.IsKnown)
        {
            return Measured<long>.NotAvailable(text.UnknownReason ?? "value not readable", origin);
        }

        return long.TryParse(text.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? Measured<long>.Known(value, origin)
            : Measured<long>.NotAvailable("value is not an integer", origin);
    }

    public bool KeyExists(RegistryScope scope, string subKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            // The base keys (HKLM/HKCU/HKU) are shared static RegistryKey instances: never dispose them.
            var baseKey = GetBaseKey(scope);
            using var key = baseKey?.OpenSubKey(subKey, writable: false);
            return key is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public IReadOnlyList<string> EnumerateSubKeys(RegistryScope scope, string subKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<string>();
        }

        try
        {
            // The base keys (HKLM/HKCU/HKU) are shared static RegistryKey instances: never dispose them.
            var baseKey = GetBaseKey(scope);
            using var key = baseKey?.OpenSubKey(subKey, writable: false);
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<string> EnumerateValueNames(RegistryScope scope, string subKey)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<string>();
        }

        try
        {
            // The base keys (HKLM/HKCU/HKU) are shared static RegistryKey instances: never dispose them.
            var baseKey = GetBaseKey(scope);
            using var key = baseKey?.OpenSubKey(subKey, writable: false);
            return key?.GetValueNames() ?? Array.Empty<string>();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public bool TryWriteString(RegistryScope scope, string subKey, string valueName, string value, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            error = "registry is only available on Windows";
            return false;
        }

        if (!IsWriteAllowed(scope, subKey))
        {
            error = $"writing to {scope}\\{subKey} is not on the allowed list";
            return false;
        }

        try
        {
            // The base keys (HKLM/HKCU/HKU) are shared static RegistryKey instances: never dispose them.
            var baseKey = GetBaseKey(scope);
            if (baseKey is null)
            {
                error = $"unsupported registry scope {scope}";
                return false;
            }

            using var key = baseKey.CreateSubKey(subKey, writable: true);
            if (key is null)
            {
                error = "key could not be opened for writing";
                return false;
            }

            key.SetValue(valueName, value, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public bool TryDeleteValue(RegistryScope scope, string subKey, string valueName, out string? error)
    {
        error = null;

        if (!OperatingSystem.IsWindows())
        {
            error = "registry is only available on Windows";
            return false;
        }

        if (!IsWriteAllowed(scope, subKey))
        {
            error = $"modifying {scope}\\{subKey} is not on the allowed list";
            return false;
        }

        try
        {
            // The base keys (HKLM/HKCU/HKU) are shared static RegistryKey instances: never dispose them.
            var baseKey = GetBaseKey(scope);
            using var key = baseKey?.OpenSubKey(subKey, writable: true);
            if (key is null)
            {
                error = "key does not exist or is not writable";
                return false;
            }

            key.DeleteValue(valueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Only three key families may be modified, and only by an approved, backed up action:
    /// our own key, the user autostart key and the service key of a specific service.
    /// </summary>
    public static bool IsWriteAllowed(RegistryScope scope, string subKey)
    {
        if (string.IsNullOrWhiteSpace(subKey))
        {
            return false;
        }

        if (scope == RegistryScope.CurrentUser)
        {
            return subKey.StartsWith(OwnKeyRoot, StringComparison.OrdinalIgnoreCase)
                || subKey.Equals(UserRunKey, StringComparison.OrdinalIgnoreCase);
        }

        if (scope == RegistryScope.LocalMachine)
        {
            return subKey.StartsWith(OwnKeyRoot, StringComparison.OrdinalIgnoreCase)
                || subKey.Equals(MachineRunKey, StringComparison.OrdinalIgnoreCase)
                || subKey.StartsWith(ServicesKeyPrefix, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static RegistryKey? GetBaseKey(RegistryScope scope) => scope switch
    {
        RegistryScope.LocalMachine => Registry.LocalMachine,
        RegistryScope.CurrentUser => Registry.CurrentUser,
        RegistryScope.Users => Registry.Users,
        _ => null,
    };
}
