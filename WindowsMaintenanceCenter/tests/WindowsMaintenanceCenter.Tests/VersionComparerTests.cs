using WindowsMaintenanceCenter.Core.Services;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// Version comparison (spec section 49). The tests document the real behaviour, including the cases
/// where no statement is possible - the application must never guess that a version is newer.
/// </summary>
public sealed class VersionComparerTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1", VersionComparison.AvailableIsNewer)]
    [InlineData("1.0.1", "1.0.0", VersionComparison.AvailableIsOlder)]
    [InlineData("1.0", "1.0.0", VersionComparison.Same)]
    [InlineData("2.0", "1.9.9", VersionComparison.AvailableIsOlder)]
    public void Compare_numeric_versions(string installed, string available, VersionComparison expected) =>
        Assert.Equal(expected, VersionComparer.Compare(installed, available));

    [Theory]
    [InlineData(null, "1.0")]
    [InlineData("1.0", null)]
    [InlineData("", "")]
    [InlineData("abc", "def")]
    public void Compare_without_numbers_is_unknown(string? installed, string? available) =>
        Assert.Equal(VersionComparison.Unknown, VersionComparer.Compare(installed, available));

    [Fact]
    public void Firmware_versions_with_different_prefixes_are_not_comparable() =>
        // "F67" and "1.2.3" cannot be ordered against each other; the application must not try.
        Assert.Equal(VersionComparison.NotComparable, VersionComparer.Compare("F67", "1.2.3"));

    [Fact]
    public void Firmware_versions_with_same_prefix_are_compared_numerically() =>
        Assert.Equal(VersionComparison.AvailableIsNewer, VersionComparer.Compare("F67", "F68"));

    [Fact]
    public void IsNewer_is_false_when_the_versions_cannot_be_compared() =>
        Assert.False(VersionComparer.IsNewer("F67", "1.2.3"));

    [Fact]
    public void ParseNumericSegments_reads_all_numbers()
    {
        Assert.Equal(new long[] { 5, 6, 7 }, VersionComparer.ParseNumericSegments("5.6.7"));
        Assert.Equal(new long[] { 67 }, VersionComparer.ParseNumericSegments("F67"));
        Assert.Empty(VersionComparer.ParseNumericSegments(null));
    }

    [Fact]
    public void ParsePrefix_keeps_the_letter_of_a_firmware_version()
    {
        Assert.Equal("F", VersionComparer.ParsePrefix("F67"));
        Assert.Equal(string.Empty, VersionComparer.ParsePrefix("1.2.3"));
    }
}
