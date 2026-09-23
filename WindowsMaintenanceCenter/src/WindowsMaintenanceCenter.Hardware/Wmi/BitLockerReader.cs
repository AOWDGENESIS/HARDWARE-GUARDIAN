namespace WindowsMaintenanceCenter.Hardware.Wmi;

/// <summary>
/// The protection state of one volume as <c>Win32_EncryptableVolume</c> reports it.
///
/// Everything here is nullable on purpose. The provider only reports volumes it can describe, and a
/// volume that is locked answers <c>PROTECTION UNKNOWN</c>; a missing value is never turned into
/// "not encrypted" and never into "encrypted".
/// </summary>
public sealed record EncryptableVolumeReading
{
    /// <summary>Drive letter as reported, e.g. <c>C:</c>. Empty for volumes without one.</summary>
    public string DriveLetter { get; init; } = string.Empty;

    /// <summary>0 = protection off, 1 = protection on, 2 = protection unknown. Null = not reported.</summary>
    public uint? ProtectionStatus { get; init; }

    /// <summary>0 fully decrypted … 5 decryption paused. Null = not reported.</summary>
    public uint? ConversionStatus { get; init; }

    /// <summary>0 not encrypted, 1–7 documented algorithms. Null = not reported.</summary>
    public uint? EncryptionMethod { get; init; }

    /// <summary>0 system volume, 1 fixed disk, 2 removable. Null = not reported.</summary>
    public uint? VolumeType { get; init; }

    /// <summary>PersistentVolumeID, empty for a standard fully decrypted NTFS volume.</summary>
    public string? PersistentVolumeId { get; init; }
}

/// <summary>Result of the BitLocker read: which volumes answered, and why not when none did.</summary>
public sealed record BitLockerReading
{
    /// <summary>True when the provider could not be asked at all (namespace missing, access denied).</summary>
    public bool QueryFailed { get; init; }

    public IReadOnlyList<EncryptableVolumeReading> Volumes { get; init; } = Array.Empty<EncryptableVolumeReading>();

    /// <summary>Plain text explanation for the protocol and the report - never a substitute for a value.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>Values are stored when the class is instantiated; this is the moment of the read.</summary>
    public DateTimeOffset? ReadAt { get; init; }
}

/// <summary>What the protection state of this machine amounts to.</summary>
public enum BitLockerState
{
    /// <summary>Every reported volume carries protection: fully encrypted with the key not in the clear.</summary>
    AllVolumesProtected,

    /// <summary>At least one volume is not protected (protection off, or only partially converted).</summary>
    SomeVolumesUnprotected,

    /// <summary>Encryption or decryption is running right now.</summary>
    ConversionInProgress,

    /// <summary>No volume reported protection and none reported its absence - the state is unknown.</summary>
    UnknownProtection,

    /// <summary>No encryptable volume was reported at all (for example Windows Home without BitLocker).</summary>
    NoEncryptableVolume,

    /// <summary>The provider could not be asked.</summary>
    Unreadable,
}

/// <summary>
/// Reads the BitLocker state of the local volumes (module M02, "BitLocker" in the must-have list of
/// chapter 8).
///
/// Source: the <c>Win32_EncryptableVolume</c> class in the namespace
/// <c>Root\CIMV2\Security\MicrosoftVolumeEncryption</c>, which is the class the BitLocker cmdlets use.
/// Its documented values are read as they are:
///
/// * <c>ProtectionStatus</c> 0 = PROTECTION OFF, 1 = PROTECTION ON, 2 = PROTECTION UNKNOWN,
/// * <c>ConversionStatus</c> 0 = fully decrypted … 5 = decryption paused,
/// * <c>EncryptionMethod</c> 0 = not encrypted, 6 = XTS-AES 128 (the Windows 10 default),
/// * <c>VolumeType</c> 0 = operating system, 1 = fixed disk, 2 = removable.
///
/// Three things this reader deliberately does *not* do:
///
/// * It never reports "the volume is not encrypted" from a missing answer. A volume that does not
///   appear in the result, or that answers PROTECTION UNKNOWN, leaves the state unknown.
/// * It never touches a key protector, never unlocks and never changes a BitLocker setting - the
///   methods of this provider class require administrator rights and would change the system
///   (chapter 12). Only the read-only properties are queried.
/// * It does not claim a recovery key was verified: whether a recovery key is escrowed cannot be
///   determined from this class and is therefore not stated.
/// </summary>
public static class BitLockerReader
{
    public const string Scope = @"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption";

    public const string WmiClass = "Win32_EncryptableVolume";

    /// <summary>Asks the BitLocker provider for every encryptable volume.</summary>
    public static async Task<BitLockerReading> ReadAsync(
        WmiReader wmi,
        DateTimeOffset readAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wmi);

        var rows = await wmi.QueryAsync(WmiClass, null, Scope, cancellationToken).ConfigureAwait(false);
        var error = wmi.LastError ?? string.Empty;

        if (rows.Count == 0)
        {
            return new BitLockerReading
            {
                QueryFailed = true,
                ReadAt = readAt,
                Detail = error.Length > 0
                    ? error
                    : "Win32_EncryptableVolume returned no volume (no NTFS volume is encryptable here, or the provider is not available)",
            };
        }

        var volumes = rows.Select(row => new EncryptableVolumeReading
        {
            DriveLetter = row.GetString("DriveLetter") ?? string.Empty,
            ProtectionStatus = row.TryGetUInt("ProtectionStatus", out var protection) ? protection : null,
            ConversionStatus = row.TryGetUInt("ConversionStatus", out var conversion) ? conversion : null,
            EncryptionMethod = row.TryGetUInt("EncryptionMethod", out var method) ? method : null,
            VolumeType = row.TryGetUInt("VolumeType", out var volumeType) ? volumeType : null,
            PersistentVolumeId = row.GetString("PersistentVolumeID"),
        }).ToList();

        return new BitLockerReading
        {
            Volumes = volumes,
            ReadAt = readAt,
            Detail = $"Win32_EncryptableVolume: {volumes.Count} volume(s) read",
        };
    }

    /// <summary>
    /// Decides what the reading amounts to. The order matters: a running conversion is reported as
    /// such (it is not an error and not a success yet), and an unprotected volume is named even when
    /// other volumes are protected.
    /// </summary>
    public static BitLockerState Judge(BitLockerReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (reading.QueryFailed)
        {
            return BitLockerState.Unreadable;
        }

        if (reading.Volumes.Count == 0)
        {
            return BitLockerState.NoEncryptableVolume;
        }

        if (reading.Volumes.Any(volume => IsConverting(volume.ConversionStatus)))
        {
            return BitLockerState.ConversionInProgress;
        }

        if (reading.Volumes.Any(IsUnprotected))
        {
            return BitLockerState.SomeVolumesUnprotected;
        }

        // "All volumes protected" requires a positive answer for every volume. A volume whose status
        // was not reported must not disappear into a green verdict.
        return reading.Volumes.All(volume => volume.ProtectionStatus == 1)
            ? BitLockerState.AllVolumesProtected
            : BitLockerState.UnknownProtection;
    }

    /// <summary>
    /// A volume counts as unprotected when protection is off, or when it is fully decrypted while
    /// claiming protection - the second case is a contradiction the provider itself reports, and
    /// reporting it as protected would be exactly the false success the specification forbids.
    /// </summary>
    public static bool IsUnprotected(EncryptableVolumeReading volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        if (volume.ProtectionStatus == 0)
        {
            return true;
        }

        return volume.ProtectionStatus == 1 && volume.ConversionStatus == 0;
    }

    private static bool IsConverting(uint? conversionStatus) =>
        conversionStatus is 2 or 3 or 4 or 5;

    /// <summary>Names (drive letter or a neutral description) of the unprotected volumes.</summary>
    public static IReadOnlyList<string> UnprotectedVolumes(BitLockerReading reading) =>
        reading.Volumes.Where(IsUnprotected).Select(Describe).ToList();

    /// <summary>Names of the volumes whose protection state was not reported.</summary>
    public static IReadOnlyList<string> UnreportedVolumes(BitLockerReading reading) =>
        reading.Volumes.Where(volume => volume.ProtectionStatus is null or 2).Select(Describe).ToList();

    /// <summary>A volume without a drive letter is still a volume; it gets a neutral name.</summary>
    public static string Describe(EncryptableVolumeReading volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        if (!string.IsNullOrWhiteSpace(volume.DriveLetter))
        {
            return volume.DriveLetter!;
        }

        return string.IsNullOrWhiteSpace(volume.PersistentVolumeId)
            ? "volume without drive letter"
            : $"volume {volume.PersistentVolumeId}";
    }

    /// <summary>
    /// Non-localised, machine readable token for the evidence line. It names what was read, never a
    /// recommendation.
    /// </summary>
    public static string Token(EncryptableVolumeReading volume)
    {
        ArgumentNullException.ThrowIfNull(volume);

        return $"protection={Show(volume.ProtectionStatus)}; conversion={Show(volume.ConversionStatus)}; " +
               $"method={Show(volume.EncryptionMethod)}; type={Show(volume.VolumeType)}";
    }

    private static string Show(uint? value) => value?.ToString() ?? "not reported";
}
