using HardwareGuardian.Core;
using HardwareGuardian.Core.Values;
using Xunit;

namespace HardwareGuardian.Tests;

/// <summary>
/// Pins the device classification. The earlier table ended with <c>_ =&gt; ComponentCategory.Pci</c>,
/// so an unlisted setup class was reported as a PCI device (spec section 1.3: no invented data).
/// </summary>
public sealed class PnpClassMapTests
{
    [Theory]
    [InlineData("Processor", ComponentCategory.Cpu)]
    [InlineData("Display", ComponentCategory.Graphics)]
    [InlineData("Net", ComponentCategory.Network)]
    [InlineData("Media", ComponentCategory.Audio)]
    [InlineData("Monitor", ComponentCategory.Monitor)]
    [InlineData("Printer", ComponentCategory.Printer)]
    [InlineData("Battery", ComponentCategory.Battery)]
    [InlineData("HDC", ComponentCategory.Storage)]
    [InlineData("SCSIAdapter", ComponentCategory.Storage)]
    [InlineData("DiskDrive", ComponentCategory.Storage)]
    [InlineData("Volume", ComponentCategory.Storage)]
    [InlineData("CDROM", ComponentCategory.Storage)]
    [InlineData("Thermal", ComponentCategory.Sensor)]
    [InlineData("System", ComponentCategory.System)]
    [InlineData("Computer", ComponentCategory.System)]
    [InlineData("Firmware", ComponentCategory.Firmware)]
    public void A_documented_setup_class_keeps_its_category(string pnpClass, ComponentCategory expected)
    {
        Assert.Equal(expected, PnpClassMap.Map(pnpClass));
    }

    [Fact]
    public void The_case_of_the_class_does_not_matter()
    {
        Assert.Equal(ComponentCategory.Network, PnpClassMap.Map("net"));
        Assert.Equal(ComponentCategory.Network, PnpClassMap.Map("NET"));
    }

    [Theory]
    [InlineData("HIDClass")]
    [InlineData("Mouse")]
    [InlineData("Camera")]
    [InlineData("SmartCardReader")]
    [InlineData("SomethingNewFromAFutureWindows")]
    public void An_unlisted_class_is_never_reported_as_a_bus(string pnpClass)
    {
        var category = PnpClassMap.Map(pnpClass);

        Assert.Equal(ComponentCategory.Unknown, category);
        Assert.NotEqual(ComponentCategory.Pci, category);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_class_is_unknown_and_not_a_guess(string? pnpClass)
    {
        Assert.Equal(ComponentCategory.Unknown, PnpClassMap.Map(pnpClass));
        Assert.False(PnpClassMap.IsKnown(pnpClass));
    }

    [Fact]
    public void Known_classes_are_reported_as_known_and_unknown_ones_are_not()
    {
        Assert.True(PnpClassMap.IsKnown("DiskDrive"));
        Assert.True(PnpClassMap.IsKnown(" thermal "));
        Assert.False(PnpClassMap.IsKnown("HIDClass"));
        Assert.Contains("THERMAL", PnpClassMap.KnownClasses);
    }
}
