using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;

namespace HardwareGuardian.Tests;

/// <summary>Clock with a fixed, controllable time so tests are reproducible.</summary>
internal sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset? now = null) =>
        Now = now ?? new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    public DateTimeOffset Now { get; set; }

    public DateTimeOffset UtcNow => Now.ToUniversalTime();

    public void Advance(TimeSpan delta) => Now += delta;
}

/// <summary>
/// Environment double. It reports "not Windows" by default and can be switched to Windows behaviour
/// without touching the machine - the tests must never depend on the host operating system.
/// </summary>
internal sealed class FakeEnvironmentProbe : IEnvironmentProbe
{
    public bool IsWindows { get; set; } = true;

    public string MachineName { get; set; } = "TEST-MACHINE";

    public string UserName { get; set; } = "tester";

    public string UserProfilePath { get; set; } = Path.Combine(Path.GetTempPath(), "hg-test-profile");

    public SessionPrivilege Privilege { get; set; } = SessionPrivilege.StandardUser;

    public bool IsElevated => Privilege == SessionPrivilege.Administrator;

    public string OsDescription { get; set; } = "Test OS";

    public string OsArchitecture { get; set; } = "X64";

    public string ProcessArchitecture { get; set; } = "X64";

    public Version OsVersion { get; set; } = new(10, 0, 26100, 0);

    public int ProcessorCount { get; set; } = 8;

    public long TotalPhysicalMemoryBytes { get; set; } = 24L * 1024 * 1024 * 1024;

    public IReadOnlyList<string> CommandLineArguments { get; set; } = Array.Empty<string>();

    public bool IsSimulationRequested { get; set; }

    public bool IsOfflineRequested { get; set; }

    public bool IsPortableRequested { get; set; }
}

/// <summary>Settings double that keeps everything in memory.</summary>
internal sealed class FakeSettingsService : ISettingsService
{
    public FakeSettingsService(AppSettings? initial = null) => Current = initial ?? new AppSettings();

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current { get; private set; }

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Current);

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Current = settings;
        Changed?.Invoke(this, Current);
        return Task.CompletedTask;
    }

    public Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> mutator, CancellationToken cancellationToken)
    {
        Current = mutator(Current);
        Changed?.Invoke(this, Current);
        return Task.FromResult(Current);
    }
}

/// <summary>
/// Path provider rooted in a temporary directory. Disposing removes the directory, so a test can
/// never leave files behind in the user profile.
/// </summary>
internal sealed class TempPathProvider : IPathProvider, IDisposable
{
    public TempPathProvider()
    {
        DataRoot = Path.Combine(Path.GetTempPath(), "hardwareguardian-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataRoot);
        ApplicationRoot = DataRoot;
    }

    public bool IsPortable => false;

    public string ApplicationRoot { get; }

    public string ConfigurationDirectory => Path.Combine(DataRoot, "config");

    public string DataRoot { get; }

    public string LogDirectory => Path.Combine(DataRoot, "log");

    public string ReportDirectory => _reportDirectoryOverride ?? Path.Combine(DataRoot, "reports");

    public string CacheDirectory => Path.Combine(DataRoot, "cache");

    public string HistoryDirectory => Path.Combine(DataRoot, "history");

    public string AuditDirectory => Path.Combine(DataRoot, "audit");

    public string BackupDirectory => Path.Combine(DataRoot, "backup");

    public string DownloadDirectory => Path.Combine(DataRoot, "download");

    public string SnapshotDirectory => Path.Combine(DataRoot, "snapshots");

    public string SettingsFilePath => Path.Combine(ConfigurationDirectory, "settings.json");

    private string? _reportDirectoryOverride;

    public string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    public bool IsInsideDataRoot(string path)
    {
        var full = Path.GetFullPath(path);
        return full.StartsWith(DataRoot, StringComparison.OrdinalIgnoreCase);
    }

    public void SetReportDirectoryOverride(string? reportDirectory) => _reportDirectoryOverride = reportDirectory;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not a test failure.
        }
    }
}
