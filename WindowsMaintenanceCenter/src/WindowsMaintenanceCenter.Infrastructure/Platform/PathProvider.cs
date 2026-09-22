using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Services;

namespace WindowsMaintenanceCenter.Infrastructure.Platform;

/// <summary>
/// Resolves all application directories (spec sections 37, 38, 71).
/// Portable mode: everything below the executable directory.
/// Installed mode: machine data below %ProgramData%\WindowsMaintenanceCenter, user data below
/// %LocalAppData%\WindowsMaintenanceCenter. No drive letter is ever assumed.
/// </summary>
public sealed class PathProvider : IPathProvider, IEnvironmentProbe
{
    private const string ProductFolderName = "WindowsMaintenanceCenter";

    private readonly Lazy<string> _applicationRoot;
    private readonly Lazy<bool> _isPortable;
    private readonly Lazy<string> _dataRoot;
    private string? _reportDirectoryOverride;

    public PathProvider()
    {
        _applicationRoot = new Lazy<string>(ResolveApplicationRoot);
        _isPortable = new Lazy<bool>(ResolvePortable);
        _dataRoot = new Lazy<string>(ResolveDataRoot);
    }

    public string ApplicationRoot => _applicationRoot.Value;

    public bool IsPortable => _isPortable.Value;

    /// <summary>
    /// Portable when the command line asks for it, when the executable carries the marker inside its
    /// own metadata (the single-file artefact does) or when the marker file lies next to the
    /// executable (the folder form). Portable layouts must never write to ProgramData. The decision
    /// lives in <see cref="PortableMode"/> because it is the part a delivered artefact can get wrong
    /// without anything failing, and a decision nobody tests is a decision nobody knows.
    /// </summary>
    private bool ResolvePortable() =>
        PortableMode.IsActive(CommandLineArguments, _applicationRoot.Value, PortableMode.IsEmbedded());

    private string ResolveApplicationRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            baseDirectory = Directory.GetCurrentDirectory();
        }

        return Path.GetFullPath(baseDirectory);
    }

    private string ResolveDataRoot()
    {
        if (IsPortable)
        {
            return Path.Combine(ApplicationRoot, "data");
        }

        var machineRoot = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(machineRoot))
        {
            // Fallback keeps the application usable in constrained environments and is documented
            // in docs/TROUBLESHOOTING.md. It never writes outside the user profile.
            machineRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductFolderName);
        }

        return Path.Combine(machineRoot, ProductFolderName);
    }

    public string DataRoot => _dataRoot.Value;

    public string ConfigurationDirectory => Path.Combine(DataRoot, "config");

    public string LogDirectory => Path.Combine(DataRoot, "logs");

    public string CacheDirectory => Path.Combine(DataRoot, "cache");

    public string HistoryDirectory => Path.Combine(DataRoot, "history");

    public string AuditDirectory => Path.Combine(DataRoot, "audit");

    public string BackupDirectory => Path.Combine(DataRoot, "backup");

    public string DownloadDirectory => Path.Combine(DataRoot, "downloads");

    public string SnapshotDirectory => Path.Combine(DataRoot, "snapshots");

    public string ReportDirectory => string.IsNullOrWhiteSpace(_reportDirectoryOverride)
        ? Path.Combine(DataRoot, "reports")
        : Path.GetFullPath(_reportDirectoryOverride);

    public string SettingsFilePath => Path.Combine(ConfigurationDirectory, "settings.json");

    public string EnsureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Directory path must not be empty.", nameof(path));
        }

        Directory.CreateDirectory(path);
        return path;
    }

    public bool IsInsideDataRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(DataRoot);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void SetReportDirectoryOverride(string? reportDirectory) => _reportDirectoryOverride = reportDirectory;

    // ---- IEnvironmentProbe -------------------------------------------------

    public bool IsWindows => OperatingSystem.IsWindows();

    public string MachineName => Environment.MachineName;

    public string UserName => Environment.UserName;

    public string UserProfilePath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public SessionPrivilege Privilege => IsWindows ? ElevationProbe.Current() : SessionPrivilege.Unknown;

    /// <summary>Alias used by the elevation service.</summary>
    public bool IsElevated => Privilege == SessionPrivilege.Administrator;

    public string OsDescription => System.Runtime.InteropServices.RuntimeInformation.OSDescription;

    public string OsArchitecture => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString();

    public string ProcessArchitecture => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();

    public Version OsVersion => Environment.OSVersion.Version;

    public int ProcessorCount => Environment.ProcessorCount;

    public long TotalPhysicalMemoryBytes => GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

    public IReadOnlyList<string> CommandLineArguments { get; } = Environment.GetCommandLineArgs().Skip(1).ToList();

    public bool IsSimulationRequested => CommandLineArguments.Any(a =>
        a.Equals("--simulation", StringComparison.OrdinalIgnoreCase) ||
        a.Equals("-simulation", StringComparison.OrdinalIgnoreCase));

    public bool IsOfflineRequested => CommandLineArguments.Any(a =>
        a.Equals("--offline", StringComparison.OrdinalIgnoreCase) ||
        a.Equals("-offline", StringComparison.OrdinalIgnoreCase));

    public bool IsPortableRequested => CommandLineArguments.Any(a =>
        a.Equals("--portable", StringComparison.OrdinalIgnoreCase) ||
        a.Equals("-portable", StringComparison.OrdinalIgnoreCase));
}
