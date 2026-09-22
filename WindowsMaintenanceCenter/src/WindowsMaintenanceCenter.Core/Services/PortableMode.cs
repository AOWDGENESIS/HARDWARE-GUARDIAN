using System.Reflection;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// Decides whether the application runs in portable mode (spec section 71, delivery form "portable").
///
/// Why this is a function of its own and not three lines inside the path provider:
///
/// The first delivery of the portable artefact was **not portable**. The rule was "portable when the
/// file <c>WindowsMaintenanceCenter.portable</c> sits next to the executable", and the release wrote
/// that file into the *build* folder - while the user receives a single executable,
/// <c>WindowsMaintenanceCenter-Portable-x64.exe</c>, with no folder around it. That executable
/// therefore ran in installed mode and wrote its configuration, reports and audit log to
/// <c>%ProgramData%</c>, which is the opposite of what a portable build promises. Nobody could see
/// that in a build log: the file was there, the promise was not kept.
///
/// The mode is therefore decided from three facts, all of them checkable:
///
/// 1. the command line (<c>--portable</c>), which always wins,
/// 2. the marker file next to the executable (the folder form),
/// 3. the marker <b>inside</b> the executable: a published assembly can carry
///    <c>[AssemblyMetadata("WmcPortableDefault", "true")]</c>, which is how the single-file artefact
///    states its own mode. This is what makes the shipped executable portable without any second
///    file next to it.
/// </summary>
public static class PortableMode
{
    /// <summary>Name of the marker file that makes a folder portable.</summary>
    public const string MarkerFileName = "WindowsMaintenanceCenter.portable";

    /// <summary>Assembly metadata key of a build that is portable by itself.</summary>
    public const string EmbeddedMarkerKey = "WmcPortableDefault";

    /// <summary>Command line switches that request portable mode.</summary>
    public static bool IsRequestedOnCommandLine(IEnumerable<string>? arguments) =>
        arguments?.Any(a =>
            a.Equals("--portable", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("-portable", StringComparison.OrdinalIgnoreCase)) ?? false;

    /// <summary>
    /// True when the executable carries the portable marker in its own metadata.
    ///
    /// The <b>entry</b> assembly is asked, not the assembly this code lives in: the marker describes
    /// how the application was published, and that is a property of the program the user started.
    /// When there is no entry assembly (a test host, a design-time call) the answer is false - an
    /// unproven marker is not a marker.
    /// </summary>
    public static bool IsEmbedded() => IsEmbedded(Assembly.GetEntryAssembly());

    /// <summary>Same question for a handed-in assembly; used by the tests.</summary>
    public static bool IsEmbedded(Assembly? entryAssembly)
    {
        if (entryAssembly is null)
        {
            return false;
        }

        try
        {
            return entryAssembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Any(attribute =>
                    string.Equals(attribute.Key, EmbeddedMarkerKey, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(attribute.Value, "true", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            // An assembly that cannot be read cannot promise anything.
            return false;
        }
    }

    /// <summary>
    /// The decision itself: command line first, then the embedded marker, then the marker file next
    /// to the executable.
    /// </summary>
    /// <param name="arguments">Command line arguments of the process.</param>
    /// <param name="applicationRoot">Folder the executable was started from; null when unknown.</param>
    /// <param name="embeddedMarker">Result of <see cref="IsEmbedded()"/>.</param>
    /// <param name="fileExists">File probe; defaults to <see cref="File.Exists(string)"/>.</param>
    public static bool IsActive(
        IEnumerable<string>? arguments,
        string? applicationRoot,
        bool embeddedMarker,
        Func<string, bool>? fileExists = null)
    {
        if (IsRequestedOnCommandLine(arguments))
        {
            return true;
        }

        if (embeddedMarker)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(applicationRoot))
        {
            return false;
        }

        var probe = fileExists ?? File.Exists;
        try
        {
            return probe(Path.Combine(applicationRoot, MarkerFileName));
        }
        catch (Exception)
        {
            // A path that cannot be probed (permissions, invalid characters) is not a portable marker.
            return false;
        }
    }
}
