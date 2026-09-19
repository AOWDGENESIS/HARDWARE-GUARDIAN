namespace HardwareGuardian.Core.Abstractions;

/// <summary>Abstracts the system clock so that all timestamp logic is testable.</summary>
public interface IClock
{
    DateTimeOffset Now { get; }

    DateTimeOffset UtcNow { get; }
}

/// <summary>Default clock used by the application.</summary>
public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTimeOffset Now => DateTimeOffset.Now;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>
/// Resolves every directory the application may write to. In portable mode all of them live
/// below the executable directory; in installed mode below %ProgramData%/%LocalAppData%
/// (spec sections 37 and 71). No path is ever hard coded elsewhere in the code base.
/// </summary>
public interface IPathProvider
{
    bool IsPortable { get; }

    string ApplicationRoot { get; }

    string ConfigurationDirectory { get; }

    string DataRoot { get; }

    string LogDirectory { get; }

    string ReportDirectory { get; }

    string CacheDirectory { get; }

    string HistoryDirectory { get; }

    string AuditDirectory { get; }

    string BackupDirectory { get; }

    string DownloadDirectory { get; }

    string SnapshotDirectory { get; }

    string SettingsFilePath { get; }

    string EnsureDirectory(string path);

    /// <summary>True when the path is inside the application data root. Used by safety checks.</summary>
    bool IsInsideDataRoot(string path);

    /// <summary>Applies the configured report location (settings) without hard coding a path.</summary>
    void SetReportDirectoryOverride(string? reportDirectory);
}

/// <summary>Read only view of the process and machine environment.</summary>
public interface IEnvironmentProbe
{
    bool IsWindows { get; }

    /// <summary>Local only, masked in reports. Never transmitted.</summary>
    string MachineName { get; }

    /// <summary>Local only, masked in reports. Never transmitted.</summary>
    string UserName { get; }

    string UserProfilePath { get; }

    SessionPrivilege Privilege { get; }

    bool IsElevated { get; }

    string OsDescription { get; }

    string OsArchitecture { get; }

    string ProcessArchitecture { get; }

    Version OsVersion { get; }

    int ProcessorCount { get; }

    long TotalPhysicalMemoryBytes { get; }

    IReadOnlyList<string> CommandLineArguments { get; }

    /// <summary>True when the process was started with <c>--simulation</c> (mock hardware provider).</summary>
    bool IsSimulationRequested { get; }

    /// <summary>True when the process was started with <c>--offline</c> (no network access at all).</summary>
    bool IsOfflineRequested { get; }

    /// <summary>True when the process was started with <c>--portable</c> or a portable marker file exists.</summary>
    bool IsPortableRequested { get; }
}

public enum NotificationKind
{
    Information,
    Warning,
    Critical,
    MaintenanceCompleted,
    UpdateAvailable,
    ActionRequired,
}

/// <summary>Only important events are surfaced (spec section 73).</summary>
public interface INotificationService
{
    event EventHandler<NotificationMessage>? NotificationRaised;

    IReadOnlyList<NotificationMessage> History { get; }

    void Notify(NotificationMessage message);

    void Clear();
}

public sealed record NotificationMessage
{
    public string Id { get; init; } = string.Empty;

    public NotificationKind Kind { get; init; } = NotificationKind.Information;

    public string ModuleKey { get; init; } = "Module_System";

    public Values.LocalizedText Title { get; init; } = Values.LocalizedText.Of("Notification_Untitled");

    public Values.LocalizedText Message { get; init; } = Values.LocalizedText.Of("Notification_Empty");

    public DateTimeOffset RaisedAt { get; init; }

    /// <summary>Problem or action this notification belongs to, if any.</summary>
    public string? ReferenceId { get; init; }

    public bool IsPersistent { get; init; }
}

/// <summary>Runs external tools (DISM, SFC, reg.exe, powercfg, nvidia-smi, ...) without shell interpretation.</summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, ProcessRunOptions? options = null, CancellationToken cancellationToken = default);
}

public sealed record ProcessRunOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    public int MaxOutputCharacters { get; init; } = 500_000;
}

public sealed record ProcessResult
{
    public string Executable { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public TimeSpan Duration { get; init; }

    public bool TimedOut { get; init; }

    public string? ErrorDetail { get; init; }

    public bool Succeeded => ExitCode == 0 && !TimedOut && ErrorDetail is null;

    public string CombinedOutput => string.IsNullOrEmpty(StandardError)
        ? StandardOutput
        : StandardOutput + Environment.NewLine + StandardError;

    public static ProcessResult Failure(string executable, string error) => new()
    {
        Executable = executable,
        ExitCode = -1,
        ErrorDetail = error,
    };
}

/// <summary>
/// Elevation is requested per operation, never for the whole session (spec section 52).
/// </summary>
public interface IElevationService
{
    bool IsElevated { get; }

    /// <summary>Explains why an operation needs administrator rights, in the selected language.</summary>
    Values.LocalizedText ExplainRequirement(string reasonCode);

    /// <summary>Restarts the application elevated. Returns false when the user declined.</summary>
    Task<bool> RestartElevatedAsync(CancellationToken cancellationToken = default);
}
