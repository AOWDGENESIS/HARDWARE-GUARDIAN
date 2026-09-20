using WindowsMaintenanceCenter.Hardware.Wmi;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// Pins the interpretation of the raw firmware call. The earlier implementation returned
/// "Secure Boot is on" for ERROR_INSUFFICIENT_BUFFER (122); these tests keep that guess out
/// (spec sections 1.3, 44 and 91: a value that was not measured is UNKNOWN, never a default).
/// </summary>
public sealed class SecureBootReadingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(255)]
    public void A_non_zero_first_byte_means_enabled(byte first)
    {
        var reading = SecureBootReader.Interpret(1, 0, new[] { first });

        Assert.True(reading.IsKnown);
        Assert.True(reading.Value);
        Assert.Equal("Enabled", reading.Display);
    }

    [Fact]
    public void A_zero_first_byte_means_disabled()
    {
        var reading = SecureBootReader.Interpret(1, 0, new byte[] { 0 });

        Assert.True(reading.IsKnown);
        Assert.False(reading.Value);
        Assert.Equal("Disabled", reading.Display);
    }

    [Fact]
    public void An_insufficient_buffer_is_never_reported_as_enabled()
    {
        var reading = SecureBootReader.Interpret(0, SecureBootReader.ErrorInsufficientBuffer, new byte[1]);

        Assert.False(reading.IsKnown);
        Assert.Null(reading.Value);
        Assert.Contains("122", reading.Detail, StringComparison.Ordinal);
        Assert.Contains("unknown", reading.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Enabled", reading.Display, StringComparison.Ordinal);
    }

    [Fact]
    public void A_firmware_without_an_efi_variable_store_is_unknown_and_named_as_such()
    {
        var reading = SecureBootReader.Interpret(0, SecureBootReader.ErrorInvalidFunction, new byte[1]);

        Assert.False(reading.IsKnown);
        Assert.Contains("legacy BIOS", reading.Display, StringComparison.Ordinal);
    }

    [Fact]
    public void A_denied_read_names_the_missing_rights()
    {
        var reading = SecureBootReader.Interpret(0, SecureBootReader.ErrorAccessDenied, new byte[1]);

        Assert.False(reading.IsKnown);
        Assert.Contains("administrator", reading.Display, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_unknown_state_carries_a_reason_and_no_state()
    {
        foreach (var error in new[]
                 {
                     SecureBootReader.ErrorInsufficientBuffer,
                     SecureBootReader.ErrorInvalidFunction,
                     SecureBootReader.ErrorFileNotFound,
                     SecureBootReader.ErrorAccessDenied,
                     SecureBootReader.ErrorNotSupported,
                     0,
                     87,
                 })
        {
            var reading = SecureBootReader.Interpret(0, error, new byte[1]);

            Assert.False(reading.IsKnown);
            Assert.False(string.IsNullOrWhiteSpace(reading.Detail));
            Assert.Equal(reading.Detail, reading.Display);
        }
    }

    [Fact]
    public void An_unexpected_variable_size_stays_visible_in_the_evidence()
    {
        var reading = SecureBootReader.Interpret(4, 0, new byte[] { 1, 0, 0, 0 });

        Assert.True(reading.IsKnown);
        Assert.True(reading.Value);
        Assert.Contains("4 byte(s)", reading.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Bytes_without_data_are_contradictory_and_stay_unknown()
    {
        var reading = SecureBootReader.Interpret(2, 0, Array.Empty<byte>());

        Assert.False(reading.IsKnown);
        Assert.Contains("no data", reading.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
