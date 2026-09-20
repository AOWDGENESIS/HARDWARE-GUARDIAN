namespace HardwareGuardian.Hardware.Wmi;

/// <summary>One firewall profile as the Windows Firewall provider reported it.</summary>
public sealed record FirewallProfileReading
{
    /// <summary>Profile name as reported, normally <c>Domain</c>, <c>Private</c> or <c>Public</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Null when the provider did not report a state for this profile.</summary>
    public bool? Enabled { get; init; }

    public string? DefaultInboundAction { get; init; }
}

/// <summary>Reading of all firewall profiles plus the reason when the read failed.</summary>
public sealed record FirewallReading
{
    public bool QueryFailed { get; init; }

    public IReadOnlyList<FirewallProfileReading> Profiles { get; init; } = Array.Empty<FirewallProfileReading>();

    public string Detail { get; init; } = string.Empty;
}

/// <summary>Aggregated firewall state across the profiles.</summary>
public enum FirewallState
{
    /// <summary>Every profile was readable and enabled.</summary>
    AllProfilesEnabled,

    /// <summary>At least one profile is switched off.</summary>
    SomeProfilesDisabled,

    /// <summary>No profile reports an unusable state, but at least one state was not reported.</summary>
    NotFullyReadable,

    /// <summary>The provider could not be asked at all.</summary>
    Unreadable,
}

/// <summary>
/// Reads the Windows Firewall profile states (acceptance rule 87, DIAG-F-009).
///
/// Source: the <c>MSFT_NetFirewallProfile</c> class in <c>Root\StandardCimv2</c>, which is the same
/// class the <c>Get-NetFirewallProfile</c> cmdlet reads. Its documented properties are
/// <c>Name</c>, <c>Enabled</c> (0 = off, 1 = on), <c>DefaultInboundAction</c> and
/// <c>DefaultOutboundAction</c>.
///
/// Fail closed: a state that was not reported is not a state. The class never changes anything -
/// Hardware Guardian does not disable a security feature (spec section 12), it only reports.
/// </summary>
public static class FirewallReader
{
    public const string Scope = @"\\.\root\StandardCimv2";

    public const string WmiClass = "MSFT_NetFirewallProfile";

    /// <summary>Asks the firewall provider for all profiles.</summary>
    public static async Task<FirewallReading> ReadAsync(WmiReader wmi, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wmi);

        var rows = await wmi.QueryAsync(WmiClass, null, Scope, cancellationToken).ConfigureAwait(false);
        var error = wmi.LastError ?? string.Empty;

        if (rows.Count == 0)
        {
            return new FirewallReading
            {
                QueryFailed = true,
                Detail = error.Length > 0 ? error : "the firewall provider returned no profile",
            };
        }

        var profiles = rows.Select(row => new FirewallProfileReading
        {
            Name = row.GetString("Name") ?? "unnamed profile",
            Enabled = ReadEnabled(row),
            DefaultInboundAction = row.GetString("DefaultInboundAction"),
        }).ToList();

        return new FirewallReading
        {
            Profiles = profiles,
            Detail = $"MSFT_NetFirewallProfile: {profiles.Count} profile(s) read",
        };
    }

    /// <summary>
    /// The provider reports the state as a number (0/1) and PowerShell shows it as a word, so both
    /// shapes are accepted. Anything else counts as not reported.
    /// </summary>
    private static bool? ReadEnabled(WmiObject row)
    {
        if (row.TryGetBool("Enabled", out var flag))
        {
            return flag;
        }

        if (row.TryGetUInt("Enabled", out var number))
        {
            return number switch
            {
                0 => false,
                1 => true,
                _ => null,
            };
        }

        return null;
    }

    /// <summary>Aggregates the profiles. A profile that is off is always a finding.</summary>
    public static FirewallState Judge(FirewallReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        if (reading.QueryFailed || reading.Profiles.Count == 0)
        {
            return FirewallState.Unreadable;
        }

        if (reading.Profiles.Any(profile => profile.Enabled == false))
        {
            return FirewallState.SomeProfilesDisabled;
        }

        return reading.Profiles.Any(profile => profile.Enabled is null)
            ? FirewallState.NotFullyReadable
            : FirewallState.AllProfilesEnabled;
    }

    /// <summary>Names of the profiles that are switched off - the evidence line of the finding.</summary>
    public static IReadOnlyList<string> DisabledProfiles(FirewallReading reading) =>
        reading.Profiles.Where(profile => profile.Enabled == false).Select(profile => profile.Name).ToList();

    /// <summary>Names of the profiles without a reported state.</summary>
    public static IReadOnlyList<string> UnreportedProfiles(FirewallReading reading) =>
        reading.Profiles.Where(profile => profile.Enabled is null).Select(profile => profile.Name).ToList();
}
