using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Simulation;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The simulation fixture (spec section 5). It exists so the UI and the pipeline can be exercised
/// without hardware access - therefore every value must be recognisable as simulation and must
/// carry an origin. This is the test that keeps the fixture from becoming fake real data.
/// </summary>
public sealed class SimulationFixtureTests
{
    private readonly MockHardwareProvider _provider = new(new FakeClock());

    [Fact]
    public void The_provider_declares_itself_as_simulation() => Assert.True(_provider.IsSimulation);

    [Fact]
    public async Task System_identity_is_marked_as_simulation()
    {
        var identity = await _provider.GetSystemIdentityAsync(CancellationToken.None);

        Assert.Contains("SIMULATION", identity.Manufacturer.Display, StringComparison.OrdinalIgnoreCase);
        Assert.True(identity.Manufacturer.Origin.IsKnown);
        Assert.True(identity.ComputerModel.Origin.IsKnown);
    }

    [Fact]
    public async Task Model_values_are_prefixed_so_they_can_never_be_mistaken_for_a_measurement()
    {
        var processors = await _provider.GetProcessorsAsync(CancellationToken.None);
        var graphics = await _provider.GetGraphicsAdaptersAsync(CancellationToken.None);

        Assert.All(processors, p => Assert.StartsWith("SIMULATION", p.Name.Display, StringComparison.OrdinalIgnoreCase));
        Assert.All(graphics, g => Assert.StartsWith("SIMULATION", g.Name.Display, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Every_read_value_carries_an_origin()
    {
        var processors = await _provider.GetProcessorsAsync(CancellationToken.None);
        var memory = await _provider.GetMemoryAsync(CancellationToken.None);
        var storage = await _provider.GetStorageDevicesAsync(CancellationToken.None);
        var network = await _provider.GetNetworkAdaptersAsync(CancellationToken.None);

        Assert.All(processors, p => Assert.True(p.Name.Origin.IsKnown));
        Assert.True(memory.TotalPhysicalBytes.Origin.IsKnown);
        Assert.All(storage, s => Assert.True(s.Model.Origin.IsKnown));
        Assert.All(network, n => Assert.True(n.Name.Origin.IsKnown));
    }

    [Fact]
    public async Task A_value_that_cannot_be_provided_is_unknown_with_a_reason()
    {
        var storage = await _provider.GetStorageReliabilityAsync(CancellationToken.None);
        var device = Assert.Single(storage);

        Assert.False(device.NvmeBytesWritten.HasValue);
        Assert.False(string.IsNullOrWhiteSpace(device.NvmeBytesWritten.UnknownReason));
    }

    [Fact]
    public async Task A_desktop_machine_reports_no_battery_instead_of_a_fake_one() =>
        Assert.Null(await _provider.GetBatteryAsync(CancellationToken.None));

    [Fact]
    public async Task The_board_revision_is_not_claimed_as_verified()
    {
        var board = await _provider.GetMotherboardAsync(CancellationToken.None);

        Assert.False(board.RevisionVerified);
        Assert.False(string.IsNullOrWhiteSpace(board.RevisionVerificationDetail));
    }

    [Fact]
    public async Task The_fixture_contains_one_device_with_a_problem_code_for_the_diagnostics()
    {
        var devices = await _provider.GetPnpDevicesAsync(CancellationToken.None);

        Assert.Contains(devices, d => d.ConfigManagerErrorCode == 22);
    }

    [Fact]
    public async Task Repeated_reads_return_the_same_values()
    {
        var first = await _provider.GetProcessorsAsync(CancellationToken.None);
        var second = await _provider.GetProcessorsAsync(CancellationToken.None);

        // Display is a method, not a property: without the call the test compares two freshly created
        // delegates, which are never equal and would fail for the wrong reason. What has to be equal
        // is the shown text of both reads.
        Assert.Equal(
            first[0].Name.Display(System.Globalization.CultureInfo.InvariantCulture),
            second[0].Name.Display(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(
            first[0].Cores.Display(System.Globalization.CultureInfo.InvariantCulture),
            second[0].Cores.Display(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Windows_identity_is_present_for_the_windows_checks()
    {
        var identity = await _provider.GetWindowsIdentityAsync(CancellationToken.None);

        Assert.True(identity.ProductName.IsKnown);
        Assert.True(identity.DisplayVersion.IsKnown);
        Assert.True(identity.BuildNumber.IsKnown);
    }
}
