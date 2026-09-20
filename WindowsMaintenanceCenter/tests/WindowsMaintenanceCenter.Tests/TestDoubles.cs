using System.Net;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Simulation;

namespace WindowsMaintenanceCenter.Tests;

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
/// Environment double. It reports Windows behaviour by default and can be switched to a non Windows
/// host without touching the machine - the tests must never depend on the host operating system.
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
        DataRoot = Path.Combine(Path.GetTempPath(), "windowsmaintenancecenter-tests", Guid.NewGuid().ToString("N"));
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

/// <summary>Event bus double that keeps the published events so a test can look at them.</summary>
internal sealed class RecordingEventBus : IEventBus
{
    private readonly List<object> _published = new();
    private readonly object _gate = new();

    public IReadOnlyList<object> Published
    {
        get
        {
            lock (_gate)
            {
                return _published.ToList();
            }
        }
    }

    public int SubscriberCount => 0;

    public void Publish<TEvent>(TEvent @event) where TEvent : notnull
    {
        lock (_gate)
        {
            _published.Add(@event);
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : notnull =>
        new NoSubscription();

    public TEvent? Last<TEvent>() where TEvent : class =>
        Published.OfType<TEvent>().LastOrDefault();

    private sealed class NoSubscription : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Service provider double for code paths that look up an optional service. It answers with null,
/// which is exactly the case the callers have to handle; a test must not need a container for this.
/// </summary>
internal sealed class EmptyServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}

/// <summary>
/// Hardware provider that behaves like the simulation fixture except for the methods named in
/// <c>failing</c>: those throw, which is what a missing WMI class or a denied read looks like in
/// production. It exists to prove that such a failure becomes a visible problem instead of an
/// empty list that looks like "this machine has no storage".
/// </summary>
internal sealed class FailingHardwareProvider : IHardwareProvider
{
    private readonly MockHardwareProvider _inner;
    private readonly HashSet<string> _failing;

    public FailingHardwareProvider(MockHardwareProvider inner, params string[] failing)
    {
        _inner = inner;
        _failing = new HashSet<string>(failing, StringComparer.Ordinal);
    }

    /// <summary>Lets a read succeed again, so a test can show that a later pass is clean.</summary>
    public void Heal(string method)
    {
        lock (_failing)
        {
            _failing.Remove(method);
        }
    }

    public string ProviderName => $"FailingHardwareProvider({_inner.ProviderName})";

    public bool IsAvailable => true;

    public bool IsSimulation => true;

    public Task<SystemIdentity> GetSystemIdentityAsync(CancellationToken cancellationToken) =>
        Fail<SystemIdentity>(nameof(GetSystemIdentityAsync)) ?? _inner.GetSystemIdentityAsync(cancellationToken);

    public Task<IReadOnlyList<ProcessorInfo>> GetProcessorsAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<ProcessorInfo>>(nameof(GetProcessorsAsync)) ?? _inner.GetProcessorsAsync(cancellationToken);

    public Task<MemoryInfo> GetMemoryAsync(CancellationToken cancellationToken) =>
        Fail<MemoryInfo>(nameof(GetMemoryAsync)) ?? _inner.GetMemoryAsync(cancellationToken);

    public Task<MotherboardInfo> GetMotherboardAsync(CancellationToken cancellationToken) =>
        Fail<MotherboardInfo>(nameof(GetMotherboardAsync)) ?? _inner.GetMotherboardAsync(cancellationToken);

    public Task<BiosIdentification> GetBiosAsync(CancellationToken cancellationToken) =>
        Fail<BiosIdentification>(nameof(GetBiosAsync)) ?? _inner.GetBiosAsync(cancellationToken);

    public Task<IReadOnlyList<GraphicsAdapterInfo>> GetGraphicsAdaptersAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<GraphicsAdapterInfo>>(nameof(GetGraphicsAdaptersAsync)) ?? _inner.GetGraphicsAdaptersAsync(cancellationToken);

    public Task<IReadOnlyList<StorageDeviceInfo>> GetStorageDevicesAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<StorageDeviceInfo>>(nameof(GetStorageDevicesAsync)) ?? _inner.GetStorageDevicesAsync(cancellationToken);

    public Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<NetworkAdapterInfo>>(nameof(GetNetworkAdaptersAsync)) ?? _inner.GetNetworkAdaptersAsync(cancellationToken);

    public Task<IReadOnlyList<AudioDeviceInfo>> GetAudioDevicesAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<AudioDeviceInfo>>(nameof(GetAudioDevicesAsync)) ?? _inner.GetAudioDevicesAsync(cancellationToken);

    public Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<MonitorInfo>>(nameof(GetMonitorsAsync)) ?? _inner.GetMonitorsAsync(cancellationToken);

    public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<PrinterInfo>>(nameof(GetPrintersAsync)) ?? _inner.GetPrintersAsync(cancellationToken);

    public Task<BatteryInfo?> GetBatteryAsync(CancellationToken cancellationToken) =>
        Fail<BatteryInfo?>(nameof(GetBatteryAsync)) ?? _inner.GetBatteryAsync(cancellationToken);

    public Task<IReadOnlyList<PnpDeviceInfo>> GetPnpDevicesAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<PnpDeviceInfo>>(nameof(GetPnpDevicesAsync)) ?? _inner.GetPnpDevicesAsync(cancellationToken);

    public Task<IReadOnlyList<DriverRecord>> GetDriversAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<DriverRecord>>(nameof(GetDriversAsync)) ?? _inner.GetDriversAsync(cancellationToken);

    public Task<IReadOnlyList<ThermalZoneReading>> GetThermalZonesAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<ThermalZoneReading>>(nameof(GetThermalZonesAsync)) ?? _inner.GetThermalZonesAsync(cancellationToken);

    public Task<WindowsIdentityInfo> GetWindowsIdentityAsync(CancellationToken cancellationToken) =>
        Fail<WindowsIdentityInfo>(nameof(GetWindowsIdentityAsync)) ?? _inner.GetWindowsIdentityAsync(cancellationToken);

    public Task<IReadOnlyList<StorageReliabilityCounter>> GetStorageReliabilityAsync(CancellationToken cancellationToken) =>
        Fail<IReadOnlyList<StorageReliabilityCounter>>(nameof(GetStorageReliabilityAsync)) ?? _inner.GetStorageReliabilityAsync(cancellationToken);

    private Task<T>? Fail<T>(string method)
    {
        bool failing;
        lock (_failing)
        {
            failing = _failing.Contains(method);
        }

        return failing
            ? Task.FromException<T>(new InvalidOperationException($"the read was refused by this test double: {method}"))
            : null;
    }
}

/// <summary>
/// Diagnostic module that runs and reports nothing. It exists to exercise the module path of the
/// orchestrator (progress, problem registry, snapshot) without any analysis of its own.
/// </summary>
internal sealed class NoOpDiagnosticModule : IDiagnosticModule
{
    public string Id => "noop";

    public string DisplayNameKey => "Module_Driver";

    public ComponentCategory Category => ComponentCategory.System;

    public bool RequiresAdministrator => false;

    public bool RequiresNetwork => false;

    public Task<ModuleResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(new ModuleResult
        {
            ModuleId = Id,
            DisplayNameKey = DisplayNameKey,
            Category = Category,
            Status = HealthStatus.Healthy,
            ChecksExecuted = 1,
            Evidence = new[] { "no-op module executed" },
        });
    }
}

/// <summary>
/// HTTP transport for tests: answers every request from a function instead of the network. Shared by
/// all tests that exercise real decision logic against a prepared response.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    /// <summary>Number of requests that were actually sent.</summary>
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(_responder(request));
    }

    /// <summary>An OK response with the given body.</summary>
    public static HttpResponseMessage Ok(string body = "ok")
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent(body);
        return response;
    }

    /// <summary>A redirect to the given absolute target.</summary>
    public static HttpResponseMessage Redirect(string target)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new Uri(target);
        return response;
    }
}

/// <summary>Hands out clients that use a prepared handler.</summary>
internal sealed class StubHttpClientProvider : IHttpClientProvider
{
    private readonly HttpMessageHandler _handler;

    public StubHttpClientProvider(HttpMessageHandler handler) => _handler = handler;

    public HttpClient GetClient(string name, TimeSpan timeout) => new(_handler, disposeHandler: false)
    {
        Timeout = timeout,
    };
}

/// <summary>
/// Backup service for tests: reports a sufficient backup and hands out a record for every request.
/// It records what it was asked to secure, so a test can prove that the backup step ran before the
/// execution and covered the right operation.
/// </summary>
internal sealed class RecordingBackupService : IBackupService
{
    private readonly IClock _clock;
    private readonly List<BackupRecord> _records = new();

    public RecordingBackupService(IClock clock) => _clock = clock;

    public string Location => "memory://backups";

    /// <summary>Operations that were handed to <see cref="CreateAsync"/>.</summary>
    public IReadOnlyList<string> OperationIds => _records.Select(r => r.OperationId).ToList();

    public Task<BackupAvailability> CheckAvailabilityAsync(RiskLevel risk, CancellationToken cancellationToken) =>
        Task.FromResult(new BackupAvailability
        {
            RequiredLevel = risk,
            RestorePointAvailable = true,
            ConfigurationBackupAvailable = true,
            RegistryBackupAvailable = true,
        });

    public Task<BackupRecord> CreateAsync(BackupRequest request, IProgress<ProgressSnapshot>? progress, CancellationToken cancellationToken)
    {
        var record = new BackupRecord
        {
            Id = $"BKP-TEST-{_records.Count + 1:D3}",
            OperationId = request.OperationId,
            Kind = request.Kind,
            Risk = request.Risk,
            CreatedAt = _clock.Now,
            ArtifactPath = $"memory://backups/{request.OperationId}",
            RestoreSteps = new[] { "test: nothing to restore" },
            Evidence = new[] { $"operation={request.OperationId}", "availability=sufficient" },
        };

        _records.Add(record);
        return Task.FromResult(record);
    }

    public Task<BackupRecord?> FindAsync(string recordId, CancellationToken cancellationToken) =>
        Task.FromResult(_records.FirstOrDefault(r => r.Id == recordId));

    public Task<IReadOnlyList<BackupRecord>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult((IReadOnlyList<BackupRecord>)_records.ToList());
}
