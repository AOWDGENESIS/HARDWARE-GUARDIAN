using WindowsMaintenanceCenter.Core.Services;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The portable mode (spec section 71, delivery form "portable", chapter 82 data location).
///
/// Why these tests exist: the first delivery of the portable artefact was **not portable**. Its mode
/// depended on a marker file next to the executable, and the file was written into the build folder -
/// while the user receives one single executable. Nothing failed: the build was green, the tests were
/// green, and the artefact wrote its data into %ProgramData%. A decision like this has to be pinned by
/// tests, because a defect in it produces no error message anywhere.
/// </summary>
public sealed class PortableModeTests
{
    [Theory]
    [InlineData("--portable")]
    [InlineData("-portable")]
    [InlineData("-Portable")]
    [InlineData("--PORTABLE")]
    public void The_command_line_can_ask_for_portable_mode(string argument)
    {
        // Nothing on disk matters once the user asked for it.
        Assert.True(PortableMode.IsActive(new[] { argument }, applicationRoot: null, embeddedMarker: false));
    }

    [Fact]
    public void A_marker_file_next_to_the_executable_switches_a_folder_to_portable()
    {
        var probes = new List<string>();

        var portable = PortableMode.IsActive(
            Array.Empty<string>(),
            @"C:\Tools\WindowsMaintenanceCenter",
            embeddedMarker: false,
            fileExists: path => { probes.Add(path); return true; });

        Assert.True(portable);
        Assert.Equal(@"C:\Tools\WindowsMaintenanceCenter\" + PortableMode.MarkerFileName, Assert.Single(probes));
    }

    [Fact]
    public void Without_a_marker_and_without_a_switch_the_installed_layout_is_used()
    {
        Assert.False(PortableMode.IsActive(
            Array.Empty<string>(),
            @"C:\Program Files\Windows Maintenance Center",
            embeddedMarker: false,
            fileExists: _ => false));
    }

    /// <summary>
    /// The case the delivery got wrong: the single executable carries its own marker, so it is
    /// portable without a second file - and it does not even look at the file system.
    /// </summary>
    [Fact]
    public void An_executable_that_carries_the_marker_is_portable_on_its_own()
    {
        var probed = false;

        var portable = PortableMode.IsActive(
            Array.Empty<string>(),
            @"C:\Users\Test\Downloads",
            embeddedMarker: true,
            fileExists: _ => { probed = true; return false; });

        Assert.True(portable);
        Assert.False(probed);
    }

    [Fact]
    public void An_argument_that_only_looks_similar_does_not_switch_the_mode()
    {
        Assert.False(PortableMode.IsActive(
            new[] { "--portable-data", "/portable", "portable" },
            applicationRoot: null,
            embeddedMarker: false));
    }

    /// <summary>
    /// The marker is read from the entry assembly - the program the user started. An assembly without
    /// the metadata, or without an entry assembly at all (a test host), is not portable.
    /// </summary>
    [Fact]
    public void The_marker_is_read_from_an_assembly_metadata_and_nowhere_else()
    {
        Assert.False(PortableMode.IsEmbedded(null));

        // The running test host is the real case here: it carries no marker, so the suite never
        // pretends to run in portable mode. The app that really carries one is the published artefact;
        // that the build puts the marker in and this code reads it is checked by check-projects, which
        // sees both sides of the name.
        Assert.False(PortableMode.IsEmbedded());
        Assert.False(PortableMode.IsEmbedded(typeof(PortableModeTests).Assembly));
        Assert.Equal("WmcPortableDefault", PortableMode.EmbeddedMarkerKey);
    }

    [Fact]
    public void A_probe_that_fails_is_not_a_marker()
    {
        Assert.False(PortableMode.IsActive(
            Array.Empty<string>(),
            @"C:\broken",
            embeddedMarker: false,
            fileExists: _ => throw new UnauthorizedAccessException("no access")));
    }
}
