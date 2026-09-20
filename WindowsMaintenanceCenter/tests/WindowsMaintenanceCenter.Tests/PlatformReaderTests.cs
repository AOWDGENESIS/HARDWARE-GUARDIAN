using System.Globalization;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Hardware.Wmi;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The two readers that completed the Windows assessment (acceptance rule 87: DIAG-F-009 firewall,
/// DIAG-F-011 TPM) plus the size formatter that replaced the hard coded "MB"/"UNKNOWN" strings.
///
/// The readers themselves talk to WMI and therefore only run on Windows with real hardware. Their
/// verdicts, however, are pure functions - and the verdict is the part that a user reads as a
/// statement about the security of the machine. These tests pin the fail closed behaviour: a state
/// that was not reported must never become "disabled" (which would look like a finding) and never
/// become "enabled" (which would look like a clean bill of health).
/// </summary>
public sealed class PlatformReaderTests
{
    // ---------------------------------------------------------------------------------------------
    // TPM (DIAG-F-011)
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void An_unreadable_tpm_is_unknown_and_never_disabled()
    {
        var reading = new TpmReading { QueryFailed = true, Detail = "namespace root\\CIMV2\\Security\\MicrosoftTpm not found" };

        Assert.Equal(TpmVerdict.Unknown, TpmReader.Judge(reading));
    }

    [Fact]
    public void A_machine_without_a_tpm_instance_is_reported_as_not_present()
    {
        var reading = new TpmReading { HasInstance = false, Detail = "the firmware provider returned no TPM instance" };

        Assert.Equal(TpmVerdict.NotPresent, TpmReader.Judge(reading));
    }

    [Theory]
    [InlineData(false, null, TpmVerdict.Disabled)]
    [InlineData(true, false, TpmVerdict.EnabledNotActivated)]
    [InlineData(true, true, TpmVerdict.Ready)]
    public void The_verdict_follows_the_reported_state(bool enabled, bool? activated, TpmVerdict expected)
    {
        var reading = new TpmReading { HasInstance = true, Enabled = enabled, Activated = activated };

        Assert.Equal(expected, TpmReader.Judge(reading));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, null)]
    public void A_partially_reported_state_stays_unknown(bool? enabled, bool? activated)
    {
        var reading = new TpmReading { HasInstance = true, Enabled = enabled, Activated = activated };

        Assert.Equal(TpmVerdict.Unknown, TpmReader.Judge(reading));
    }

    [Theory]
    [InlineData("2.0, 0, 1.16", "2.0")]
    [InlineData("1.2, 2, 0", "1.2")]
    [InlineData("  2.0  ", "2.0")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_specification_generation_is_taken_from_the_reported_version(string? reported, string? expected)
    {
        Assert.Equal(expected, TpmReader.SpecificationMajor(reported));
    }

    [Fact]
    public void The_reader_only_reads_the_documented_win32_tpm_class()
    {
        // The class and the namespace are pinned: a typo here would silently report "no TPM" on a
        // machine that has one.
        Assert.Equal("Win32_Tpm", TpmReader.WmiClass);
        Assert.Equal(@"\\.\root\CIMV2\Security\MicrosoftTpm", TpmReader.Scope);
    }

    // ---------------------------------------------------------------------------------------------
    // Firewall (DIAG-F-009)
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void An_unreadable_firewall_provider_is_reported_as_unreadable()
    {
        var reading = new FirewallReading { QueryFailed = true, Detail = "root\\StandardCimv2 not reachable" };

        Assert.Equal(FirewallState.Unreadable, FirewallReader.Judge(reading));
    }

    [Fact]
    public void An_empty_profile_list_is_unreadable_and_not_healthy()
    {
        Assert.Equal(FirewallState.Unreadable, FirewallReader.Judge(new FirewallReading()));
    }

    [Fact]
    public void All_readable_profiles_enabled_is_the_healthy_state()
    {
        var reading = Profiles(("Domain", true), ("Private", true), ("Public", true));

        Assert.Equal(FirewallState.AllProfilesEnabled, FirewallReader.Judge(reading));
    }

    [Fact]
    public void A_disabled_profile_is_a_finding_even_when_another_state_was_not_reported()
    {
        var reading = Profiles(("Domain", true), ("Private", false), ("Public", null));

        Assert.Equal(FirewallState.SomeProfilesDisabled, FirewallReader.Judge(reading));
        Assert.Equal(new[] { "Private" }, FirewallReader.DisabledProfiles(reading));
        Assert.Equal(new[] { "Public" }, FirewallReader.UnreportedProfiles(reading));
    }

    [Fact]
    public void A_state_that_was_not_reported_is_not_turned_into_enabled()
    {
        var reading = Profiles(("Domain", true), ("Private", true), ("Public", null));

        Assert.Equal(FirewallState.NotFullyReadable, FirewallReader.Judge(reading));
        Assert.Empty(FirewallReader.DisabledProfiles(reading));
    }

    [Fact]
    public void The_reader_uses_the_same_class_as_the_operating_system_cmdlet()
    {
        Assert.Equal("MSFT_NetFirewallProfile", FirewallReader.WmiClass);
        Assert.Equal(@"\\.\root\StandardCimv2", FirewallReader.Scope);
    }

    // ---------------------------------------------------------------------------------------------
    // Sizes (rule 119: no hard coded "MB" and no English "UNKNOWN" in visible text)
    // ---------------------------------------------------------------------------------------------
    [Theory]
    [InlineData(0u, "0 B")]
    [InlineData(999u, "999 B")]
    [InlineData(1536u, "1.5 KiB")]
    [InlineData(1073741824u, "1 GiB")]
    public void A_size_is_formatted_with_the_unit_that_fits(uint bytes, string expected)
    {
        Assert.Equal(expected, SizeText.Format(bytes, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_number_of_a_size_follows_the_active_culture()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        var german = SizeText.Format(1536, culture);

        // Verglichen wird gegen das Trennzeichen der Kultur selbst: der Test prüft, dass die Kultur
        // benutzt wird, und hängt nicht daran, ob die Maschine vollständige ICU-Daten hat.
        Assert.Equal("1" + culture.NumberFormat.NumberDecimalSeparator + "5 KiB", german);
    }

    [Fact]
    public void A_size_that_was_not_measured_uses_the_text_of_the_caller()
    {
        var unknown = Measured<ulong>.NotAvailable("directory not readable");

        Assert.Equal("NOT AVAILABLE", SizeText.Format(unknown, CultureInfo.InvariantCulture, "NOT AVAILABLE"));
        Assert.Equal("2 KiB", SizeText.Format(Measured<ulong>.Known(2048, ValueOrigin.Unknown), CultureInfo.InvariantCulture, "NOT AVAILABLE"));
    }

    [Fact]
    public void A_signed_size_is_formatted_the_same_way_and_a_negative_one_is_not_a_size()
    {
        // Verzeichnisgrößen sind im Modell vorzeichenbehaftet, Downloadgrößen nicht.
        Assert.Equal("1 GiB", SizeText.Format(Measured<long>.Known(1073741824, ValueOrigin.Unknown), CultureInfo.InvariantCulture, "NOT AVAILABLE"));
        Assert.Equal("NOT AVAILABLE", SizeText.Format(Measured<long>.Known(-1, ValueOrigin.Unknown), CultureInfo.InvariantCulture, "NOT AVAILABLE"));
        Assert.Equal("NOT AVAILABLE", SizeText.Format(Measured<long>.NotAvailable("not measured"), CultureInfo.InvariantCulture, "NOT AVAILABLE"));
    }

    // ---------------------------------------------------------------------------------------------
    // The enum and the localisation must stay in step (rule 119, LANG-F-003)
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void Every_windows_check_can_be_named_in_both_languages()
    {
        foreach (var language in new[] { "en", "de" })
        {
            var keys = ResourceKeys(language);
            foreach (var check in Enum.GetValues<WindowsCheckId>())
            {
                Assert.True(
                    keys.Contains("WindowsCheck_" + check),
                    $"{language}: WindowsCheck_{check} is missing - the check would be listed without a name");
            }
        }
    }

    private static FirewallReading Profiles(params (string Name, bool? Enabled)[] profiles) => new()
    {
        Profiles = profiles.Select(p => new FirewallProfileReading { Name = p.Name, Enabled = p.Enabled }).ToList(),
        Detail = "test",
    };

    private static HashSet<string> ResourceKeys(string language)
    {
        var assembly = typeof(LocalizedText).Assembly;
        var name = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith($"Resources.{language}.json", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var document = System.Text.Json.JsonDocument.Parse(stream);
        var strings = document.RootElement.GetProperty("strings");
        return strings.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
    }
}
