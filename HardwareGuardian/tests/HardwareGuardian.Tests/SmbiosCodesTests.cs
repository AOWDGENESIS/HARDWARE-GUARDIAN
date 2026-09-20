using HardwareGuardian.Core.Values;
using Xunit;

namespace HardwareGuardian.Tests;

/// <summary>
/// The SMBIOS code tables (spec sections 1.3 and 61: no invented data).
///
/// These tests exist for one reason: the lookup table that circulates on the internet labels 20/21/22
/// as DDR/DDR2/DDR2 FB-DIMM, while the specification has those values at 0x12/0x13/0x14 (18/19/20).
/// A provider that follows the wrong table reports a wrong memory type on a real machine - a silent
/// false statement about the user's hardware. The documented anchors are therefore pinned here.
/// </summary>
public sealed class SmbiosCodesTests
{
    [Theory]
    [InlineData(18u, "DDR")]
    [InlineData(19u, "DDR2")]
    [InlineData(20u, "DDR2 FB-DIMM")]
    [InlineData(24u, "DDR3")]
    [InlineData(26u, "DDR4")]
    [InlineData(34u, "DDR5")]
    public void Memory_type_follows_the_specification_not_the_circulating_table(uint code, string expected)
    {
        Assert.Equal(expected, SmbiosCodes.MemoryType(code));
    }

    [Theory]
    [InlineData(0u, "Unknown")]
    [InlineData(8u, "DIMM")]
    [InlineData(11u, "RIMM")]
    [InlineData(12u, "SODIMM")]
    [InlineData(13u, "SRIMM")]
    public void Memory_form_factor_follows_the_cim_enumeration(uint code, string expected)
    {
        Assert.Equal(expected, SmbiosCodes.MemoryFormFactor(code));
    }

    [Theory]
    [InlineData(1u, "Other")]
    [InlineData(3u, "Desktop")]
    [InlineData(8u, "Portable")]
    [InlineData(9u, "Laptop")]
    [InlineData(10u, "Notebook")]
    [InlineData(13u, "All in one")]
    [InlineData(23u, "Rack mount chassis")]
    [InlineData(24u, "Sealed-case PC")]
    [InlineData(30u, "Tablet")]
    [InlineData(31u, "Convertible")]
    [InlineData(32u, "Detachable")]
    public void Chassis_type_follows_the_cim_enumeration(uint code, string expected)
    {
        Assert.Equal(expected, SmbiosCodes.ChassisType(code));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(25u)]
    [InlineData(29u)]
    [InlineData(77u)]
    [InlineData(uint.MaxValue)]
    public void An_undocumented_code_is_reported_as_a_code_and_never_guessed(uint code)
    {
        // A value that is not in the documented table must not receive a plausible sounding name.
        // The text says that it is a code, so nobody can read it as a hardware statement.
        var text = SmbiosCodes.MemoryType(code);
        Assert.Contains(code.ToString(System.Globalization.CultureInfo.InvariantCulture), text, StringComparison.Ordinal);
        Assert.Contains("not in the documented table", text, StringComparison.Ordinal);
        Assert.DoesNotContain("DDR", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_code_stays_visible_next_to_the_name()
    {
        // The reader of a report must be able to verify the statement against the specification,
        // which is only possible when the raw code is part of the text.
        Assert.Equal("DDR4 (SMBIOS code 26)", SmbiosCodes.Describe(SmbiosCodes.MemoryType(26), 26));
        Assert.Equal("Desktop (SMBIOS code 3)", SmbiosCodes.Describe(SmbiosCodes.ChassisType(3), 3));
    }
}
