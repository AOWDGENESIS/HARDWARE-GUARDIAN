using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Abstractions;

/// <summary>
/// Read/write access to the Windows registry, restricted to the operations Windows Maintenance Center
/// actually needs. There is deliberately no "clean up the registry" capability (spec section 18).
/// </summary>
public interface IRegistryAccess
{
    /// <summary>Reads a value. Returns an unknown <see cref="TextInfo"/> when the value does not exist.</summary>
    TextInfo ReadString(RegistryScope scope, string subKey, string valueName, DateTimeOffset retrievedAt);

    Measured<int> ReadInt(RegistryScope scope, string subKey, string valueName, DateTimeOffset retrievedAt);

    Measured<long> ReadLong(RegistryScope scope, string subKey, string valueName, DateTimeOffset retrievedAt);

    bool KeyExists(RegistryScope scope, string subKey);

    IReadOnlyList<string> EnumerateSubKeys(RegistryScope scope, string subKey);

    IReadOnlyList<string> EnumerateValueNames(RegistryScope scope, string subKey);

    /// <summary>
    /// Writes a value. Only called from approved actions after a backup was created.
    /// Returns false when the write was refused (for example protected keys).
    /// </summary>
    bool TryWriteString(RegistryScope scope, string subKey, string valueName, string value, out string? error);

    bool TryDeleteValue(RegistryScope scope, string subKey, string valueName, out string? error);
}

public enum RegistryScope
{
    LocalMachine,
    CurrentUser,
    Users,
}
