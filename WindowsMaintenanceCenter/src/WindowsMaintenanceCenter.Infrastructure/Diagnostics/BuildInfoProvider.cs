using System.Reflection;
using System.Runtime.InteropServices;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;

namespace WindowsMaintenanceCenter.Infrastructure.Diagnostics;

/// <summary>
/// Reads the build metadata that was embedded at compile time (spec section 58).
/// Missing metadata is reported as <c>unknown</c>, never guessed.
/// </summary>
public sealed class BuildInfoProvider : IBuildInfoProvider
{
    private readonly IEnvironmentProbe _environment;
    private readonly IPathProvider _paths;
    private readonly Lazy<AppBuildInfo> _info;

    public BuildInfoProvider(IEnvironmentProbe environment, IPathProvider paths)
    {
        _environment = environment;
        _paths = paths;
        _info = new Lazy<AppBuildInfo>(Create);
    }

    public AppBuildInfo Get() => _info.Value;

    private AppBuildInfo Create()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToList();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        string Value(string key, string fallback) =>
            metadata.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase))?.Value is { Length: > 0 } value
                ? value
                : fallback;

        return new AppBuildInfo
        {
            Version = version,
            Commit = Value("Commit", "unknown"),
            BuildDate = Value("BuildDate", "unspecified"),
            Configuration = Value("BuildConfiguration", "unknown"),
            TargetFramework = Value("BuildTargetFramework", "unknown"),
            RuntimeIdentifier = Value("BuildRuntimeIdentifier", "unknown"),
            ProductName = Value("Product", "Windows Maintenance Center"),
            ProductTagline = Value("ProductTagline", string.Empty),
            RepositoryUrl = Value("RepositoryUrl", string.Empty),
            DotNetVersion = RuntimeInformation.FrameworkDescription,
            IsPortable = _paths.IsPortable,
            IsSimulation = _environment.IsSimulationRequested,
            IsOffline = _environment.IsOfflineRequested,
            Privilege = _environment.Privilege,
            DataRoot = _paths.DataRoot,
            CommandLineArguments = _environment.CommandLineArguments,
        };
    }
}
