using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Hardware.Wmi;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// BitLocker interpretation (chapter 8, module M02 - BitLocker is one of the must-have discoveries).
///
/// These tests pin down the mapping of the documented property values and, more importantly, the
/// difference between "not protected" and "not reported":
///
/// * <c>ProtectionStatus</c> 0 = off, 1 = on, 2 = unknown,
/// * <c>ConversionStatus</c> 0 = fully decrypted … 5 = decryption paused,
/// * <c>EncryptionMethod</c> 0 = not encrypted, 6 = XTS-AES 128 (the Windows 10/11 default),
/// * <c>VolumeType</c> 0 = system volume, 1 = fixed disk, 2 = removable.
///
/// A missing answer must never turn into "unprotected" - a wrong "your drive is not encrypted" is as
/// bad as a wrong "everything is fine". Both would be invented diagnoses (chapter 86).
/// </summary>
public sealed class BitLockerReadingTests
{
    private static readonly DateTimeOffset ReadAt = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Protection_on_with_a_full_conversion_counts_as_protected()
    {
        var reading = Reading(Volume("C:", protection: 1, conversion: 1, method: 6, type: 0));

        Assert.Equal(BitLockerState.AllVolumesProtected, BitLockerReader.Judge(reading));
        Assert.Empty(BitLockerReader.UnprotectedVolumes(reading));
    }

    [Fact]
    public void Protection_off_is_reported_as_unprotected()
    {
        var reading = Reading(Volume("D:", protection: 0, conversion: 0, method: 0, type: 1));

        Assert.Equal(BitLockerState.SomeVolumesUnprotected, BitLockerReader.Judge(reading));
        Assert.Equal(new[] { "D:" }, BitLockerReader.UnprotectedVolumes(reading));
    }

    [Fact]
    public void Protection_claimed_on_a_fully_decrypted_volume_is_a_contradiction_and_counts_as_unprotected()
    {
        // The provider can report this after an interrupted decryption. Trusting the "on" here would
        // produce a green check for a volume that is not encrypted.
        var reading = Reading(Volume("C:", protection: 1, conversion: 0, method: 0, type: 0));

        Assert.Equal(BitLockerState.SomeVolumesUnprotected, BitLockerReader.Judge(reading));
        Assert.Equal(new[] { "C:" }, BitLockerReader.UnprotectedVolumes(reading));
    }

    [Fact]
    public void Protection_unknown_never_becomes_unprotected_and_never_becomes_a_green_check()
    {
        var reading = Reading(Volume("E:", protection: 2, conversion: null, method: null, type: 2));

        Assert.Equal(BitLockerState.UnknownProtection, BitLockerReader.Judge(reading));
        Assert.Empty(BitLockerReader.UnprotectedVolumes(reading));
        Assert.Equal(new[] { "E:" }, BitLockerReader.UnreportedVolumes(reading));
    }

    [Fact]
    public void A_volume_without_a_reported_protection_state_keeps_the_whole_verdict_unknown()
    {
        // One protected system volume and one volume that answered nothing: "all protected" would be
        // a claim about a volume nobody asked successfully.
        var reading = Reading(
            Volume("C:", protection: 1, conversion: 1, method: 6, type: 0),
            Volume("F:", protection: null, conversion: null, method: null, type: 1));

        Assert.Equal(BitLockerState.UnknownProtection, BitLockerReader.Judge(reading));
        Assert.Equal(new[] { "F:" }, BitLockerReader.UnreportedVolumes(reading));
    }

    [Theory]
    [InlineData(2u)] // encryption in progress
    [InlineData(3u)] // decryption in progress
    [InlineData(4u)] // encryption paused
    [InlineData(5u)] // decryption paused
    public void A_running_conversion_is_reported_as_in_progress_not_as_a_result(uint conversionStatus)
    {
        var reading = Reading(Volume("C:", protection: 1, conversion: conversionStatus, method: 6, type: 0));

        Assert.Equal(BitLockerState.ConversionInProgress, BitLockerReader.Judge(reading));
    }

    [Fact]
    public void No_reported_volume_is_not_a_statement_about_encryption()
    {
        var reading = new BitLockerReading { Volumes = Array.Empty<EncryptableVolumeReading>(), ReadAt = ReadAt, Detail = "read" };

        Assert.Equal(BitLockerState.NoEncryptableVolume, BitLockerReader.Judge(reading));
        Assert.Empty(BitLockerReader.UnprotectedVolumes(reading));
    }

    [Fact]
    public void A_failed_provider_read_is_reported_as_unreadable()
    {
        var reading = new BitLockerReading { QueryFailed = true, Detail = "access denied", ReadAt = ReadAt };

        Assert.Equal(BitLockerState.Unreadable, BitLockerReader.Judge(reading));
    }

    [Fact]
    public void A_volume_without_a_drive_letter_still_has_a_name_in_the_evidence()
    {
        var unnamed = Volume(string.Empty, protection: 0, conversion: 0, method: 0, type: 1) with
        {
            PersistentVolumeId = "{1234-5678}",
        };
        var reading = Reading(unnamed);

        Assert.Equal(new[] { "volume {1234-5678}" }, BitLockerReader.UnprotectedVolumes(reading));
        Assert.Equal("volume without drive letter", BitLockerReader.Describe(Volume(string.Empty, 1, 1, 6, 1)));
    }

    [Fact]
    public void The_evidence_token_names_the_read_values_and_nothing_else()
    {
        var token = BitLockerReader.Token(Volume("C:", protection: 1, conversion: 1, method: 6, type: 0));

        Assert.Equal("protection=1; conversion=1; method=6; type=0", token);
        Assert.Equal(
            "protection=not reported; conversion=not reported; method=not reported; type=not reported",
            BitLockerReader.Token(Volume("C:", null, null, null, null)));
    }

    [Fact]
    public void The_new_check_is_a_named_check_of_the_windows_health_report()
    {
        // The check id must exist as a value of the enumeration the report uses - otherwise the
        // reading would be performed and could never be shown.
        Assert.True(Enum.IsDefined(typeof(WindowsCheckId), WindowsCheckId.BitLocker));
    }

    private static EncryptableVolumeReading Volume(
        string driveLetter,
        uint? protection,
        uint? conversion,
        uint? method,
        uint? type) => new()
        {
            DriveLetter = driveLetter,
            ProtectionStatus = protection,
            ConversionStatus = conversion,
            EncryptionMethod = method,
            VolumeType = type,
        };

    private static BitLockerReading Reading(params EncryptableVolumeReading[] volumes) => new()
    {
        Volumes = volumes,
        ReadAt = ReadAt,
        Detail = $"Win32_EncryptableVolume: {volumes.Length} volume(s) read",
    };
}
