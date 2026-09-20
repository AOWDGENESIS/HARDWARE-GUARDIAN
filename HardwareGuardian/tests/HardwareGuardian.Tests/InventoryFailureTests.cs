using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Diagnostics;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Services;
using HardwareGuardian.Simulation;
using Xunit;

namespace HardwareGuardian.Tests;

/// <summary>
/// The inventory is the base of every finding: if a read fails, the snapshot must say so.
///
/// These tests cover a defect that a manual review found in the orchestrator: the problems the
/// inventory reader produced for a failed read were never registered, so a scan in which a whole
/// WMI class was unreadable could still be reported as healthy - the values were simply empty.
/// </summary>
public sealed class InventoryFailureTests
{
    [Fact]
    public void StepCount_matches_the_read_methods_of_the_provider()
    {
        // The progress bar of a full scan uses this constant as the total number of inventory steps.
        // A new read method on the provider has to update it, otherwise the progress is wrong and
        // the estimate becomes a guess.
        var readMethods = typeof(IHardwareProvider)
            .GetMethods()
            .Count(method => !method.IsSpecialName && method.Name.StartsWith("Get", StringComparison.Ordinal));

        Assert.Equal(readMethods, InventoryReader.StepCount);
    }

    [Fact]
    public async Task A_failed_read_becomes_a_problem_in_the_snapshot()
    {
        var harness = new Harness(failing: new[] { "GetStorageDevicesAsync", "GetDriversAsync" });

        var snapshot = await harness.Orchestrator.RunFullScanAsync(CancellationToken.None);

        Assert.Equal(2, snapshot.InventoryFailedReads);
        Assert.Equal(2, snapshot.Problems.Count);
        Assert.Contains(snapshot.Problems, problem => problem.Id.StartsWith("HW-STORAGE", StringComparison.Ordinal));
        Assert.Contains(snapshot.Problems, problem => problem.Id.StartsWith("DRV", StringComparison.Ordinal));

        // The evidence has to name the failed call, not just say "empty".
        Assert.All(snapshot.Problems, problem => Assert.Contains("Get", problem.Evidence, StringComparison.Ordinal));
        Assert.Equal(2, snapshot.InventoryNotes.Count);

        // Nothing was read from storage, so the list is empty - and exactly that is why the status
        // must not be "healthy". Attention is the documented status for a warning level finding.
        Assert.Empty(snapshot.Storage);
        Assert.NotEqual(HealthStatus.Healthy, snapshot.OverallStatus);
        Assert.Equal(HealthStatus.Attention, snapshot.OverallStatus);

        // The user sees the reason in the always visible live protocol as well.
        Assert.Contains(
            harness.Protocol.Snapshot(),
            entry => entry.Severity == Severity.Warning && entry.Detail is not null);
    }

    [Fact]
    public async Task A_later_pass_does_not_present_the_findings_of_an_earlier_one()
    {
        var harness = new Harness(failing: new[] { "GetStorageDevicesAsync", "GetDriversAsync" });

        var first = await harness.Orchestrator.RunFullScanAsync(CancellationToken.None);
        Assert.Equal(2, first.Problems.Count);

        // The read works again: the next pass must be clean instead of repeating the old finding.
        harness.Provider.Heal("GetStorageDevicesAsync");
        harness.Provider.Heal("GetDriversAsync");

        var second = await harness.Orchestrator.RunModuleAsync("noop", CancellationToken.None);

        Assert.Empty(second.Problems);
        Assert.Equal(0, second.InventoryFailedReads);
        Assert.Single(second.Modules);
        Assert.Equal(HealthStatus.Healthy, second.OverallStatus);

        // The registry was cleared for the new pass, so the dashboard cannot show stale findings.
        Assert.Empty(harness.Problems.All);
    }

    [Fact]
    public async Task An_unavailable_provider_is_reported_instead_of_producing_an_empty_snapshot()
    {
        var harness = new Harness(failing: Array.Empty<string>(), available: false);

        var snapshot = await harness.Orchestrator.RunFullScanAsync(CancellationToken.None);

        var problem = Assert.Single(snapshot.Problems);
        Assert.Equal("HW-SYS", problem.Id[..6]);
        Assert.Equal(Severity.Error, problem.Severity);
        Assert.Equal(HealthStatus.Warning, snapshot.OverallStatus);
    }

    /// <summary>Everything one orchestrator needs, wired with real Core services and doubles.</summary>
    private sealed class Harness
    {
        public Harness(IReadOnlyList<string> failing, bool available = true)
        {
            Clock = new FakeClock();
            Events = new RecordingEventBus();
            Protocol = new LiveProtocol(Clock);
            Problems = new ProblemRegistry(Clock, Events);
            Progress = new ProgressReporter(Clock);
            Environment = new FakeEnvironmentProbe();
            Settings = new FakeSettingsService();

            var mock = new MockHardwareProvider(Clock);
            Provider = new FailingHardwareProvider(mock, failing.ToArray());

            Orchestrator = new ScanOrchestrator(
                available ? Provider : new UnavailableHardwareProvider(Provider),
                Protocol,
                Events,
                Progress,
                Problems,
                Clock,
                Environment,
                Settings,
                new EmptyServiceProvider(),
                new SystemStateMachine(Clock, Events),
                new IDiagnosticModule[] { new NoOpDiagnosticModule() });
        }

        public FakeClock Clock { get; }

        public RecordingEventBus Events { get; }

        public LiveProtocol Protocol { get; }

        public ProblemRegistry Problems { get; }

        public ProgressReporter Progress { get; }

        public FakeEnvironmentProbe Environment { get; }

        public FakeSettingsService Settings { get; }

        public FailingHardwareProvider Provider { get; }

        public ScanOrchestrator Orchestrator { get; }
    }

    /// <summary>Provider that reports itself as unusable, which is the non Windows case in production.</summary>
    private sealed class UnavailableHardwareProvider : IHardwareProvider
    {
        private readonly IHardwareProvider _inner;

        public UnavailableHardwareProvider(IHardwareProvider inner) => _inner = inner;

        public string ProviderName => "UnavailableHardwareProvider";

        public bool IsAvailable => false;

        public bool IsSimulation => false;

        public Task<SystemIdentity> GetSystemIdentityAsync(CancellationToken cancellationToken) => _inner.GetSystemIdentityAsync(cancellationToken);

        public Task<IReadOnlyList<ProcessorInfo>> GetProcessorsAsync(CancellationToken cancellationToken) => _inner.GetProcessorsAsync(cancellationToken);

        public Task<MemoryInfo> GetMemoryAsync(CancellationToken cancellationToken) => _inner.GetMemoryAsync(cancellationToken);

        public Task<MotherboardInfo> GetMotherboardAsync(CancellationToken cancellationToken) => _inner.GetMotherboardAsync(cancellationToken);

        public Task<BiosIdentification> GetBiosAsync(CancellationToken cancellationToken) => _inner.GetBiosAsync(cancellationToken);

        public Task<IReadOnlyList<GraphicsAdapterInfo>> GetGraphicsAdaptersAsync(CancellationToken cancellationToken) => _inner.GetGraphicsAdaptersAsync(cancellationToken);

        public Task<IReadOnlyList<StorageDeviceInfo>> GetStorageDevicesAsync(CancellationToken cancellationToken) => _inner.GetStorageDevicesAsync(cancellationToken);

        public Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken cancellationToken) => _inner.GetNetworkAdaptersAsync(cancellationToken);

        public Task<IReadOnlyList<AudioDeviceInfo>> GetAudioDevicesAsync(CancellationToken cancellationToken) => _inner.GetAudioDevicesAsync(cancellationToken);

        public Task<IReadOnlyList<MonitorInfo>> GetMonitorsAsync(CancellationToken cancellationToken) => _inner.GetMonitorsAsync(cancellationToken);

        public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken) => _inner.GetPrintersAsync(cancellationToken);

        public Task<BatteryInfo?> GetBatteryAsync(CancellationToken cancellationToken) => _inner.GetBatteryAsync(cancellationToken);

        public Task<IReadOnlyList<PnpDeviceInfo>> GetPnpDevicesAsync(CancellationToken cancellationToken) => _inner.GetPnpDevicesAsync(cancellationToken);

        public Task<IReadOnlyList<DriverRecord>> GetDriversAsync(CancellationToken cancellationToken) => _inner.GetDriversAsync(cancellationToken);

        public Task<IReadOnlyList<ThermalZoneReading>> GetThermalZonesAsync(CancellationToken cancellationToken) => _inner.GetThermalZonesAsync(cancellationToken);

        public Task<WindowsIdentityInfo> GetWindowsIdentityAsync(CancellationToken cancellationToken) => _inner.GetWindowsIdentityAsync(cancellationToken);

        public Task<IReadOnlyList<StorageReliabilityCounter>> GetStorageReliabilityAsync(CancellationToken cancellationToken) => _inner.GetStorageReliabilityAsync(cancellationToken);
    }
}
