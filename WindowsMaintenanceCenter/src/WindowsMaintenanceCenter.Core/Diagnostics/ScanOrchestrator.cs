using System.Diagnostics;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Events;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using Microsoft.Extensions.Logging;

namespace WindowsMaintenanceCenter.Core.Diagnostics;

/// <summary>
/// Runs the diagnostic modules, aggregates the findings and produces a
/// <see cref="SystemSnapshot"/>. Modules run sequentially: parallel WMI access is measurably
/// slower and makes failure attribution ambiguous, which conflicts with the transparency rule.
/// </summary>
public sealed class ScanOrchestrator : IScanOrchestrator
{
    private readonly IHardwareProvider _provider;
    private readonly ILiveProtocol _protocol;
    private readonly IEventBus _events;
    private readonly IProgressReporter _progress;
    private readonly IProblemRegistry _problems;
    private readonly IClock _clock;
    private readonly IEnvironmentProbe _environment;
    private readonly ISettingsService _settings;
    private readonly IServiceProvider _services;
    private readonly ISystemStateMachine _state;
    private readonly IReadOnlyList<IDiagnosticModule> _modules;
    private readonly ISnapshotStore? _snapshotStore;
    private readonly IHistoryStore? _historyStore;
    private readonly IBuildInfoProvider? _buildInfo;
    private readonly ILogger<ScanOrchestrator>? _logger;
    /// <summary>
    /// Inventory whose problems are already in the problem registry. Every pass reads the hardware
    /// again - a cached inventory would let a result of an earlier run look like a result of this
    /// one - and this reference keeps the registration from happening twice for the same read.
    /// </summary>
    private InventoryResult? _registeredInventory;

    public ScanOrchestrator(
        IHardwareProvider provider,
        ILiveProtocol protocol,
        IEventBus events,
        IProgressReporter progress,
        IProblemRegistry problems,
        IClock clock,
        IEnvironmentProbe environment,
        ISettingsService settings,
        IServiceProvider services,
        ISystemStateMachine state,
        IEnumerable<IDiagnosticModule> modules,
        ISnapshotStore? snapshotStore = null,
        IHistoryStore? historyStore = null,
        IBuildInfoProvider? buildInfo = null,
        ILogger<ScanOrchestrator>? logger = null)
    {
        _provider = provider;
        _protocol = protocol;
        _events = events;
        _progress = progress;
        _problems = problems;
        _clock = clock;
        _environment = environment;
        _settings = settings;
        _services = services;
        _state = state;
        _modules = modules.ToList();
        _snapshotStore = snapshotStore;
        _historyStore = historyStore;
        _buildInfo = buildInfo;
        _logger = logger;
    }

    public IReadOnlyList<IDiagnosticModule> Modules => _modules;

    public SystemSnapshot? LastSnapshot { get; private set; }

    public event EventHandler<SystemSnapshot>? SnapshotCompleted;

    public async Task<SystemSnapshot> RunFullScanAsync(CancellationToken cancellationToken)
    {
        var scanId = $"SCAN-{_clock.Now:yyyyMMddHHmmss}";
        _problems.Clear();
        _events.Publish(new ScanStartedEvent(scanId, _modules.Count, _clock.Now));
        _state.TryTransitionTo(SystemState.Discovery, "full-scan");
        _protocol.Info("SYS", LocalizedText.Of("Protocol_ScanStarted", _modules.Count));

        var totalSteps = _modules.Count + InventoryReader.StepCount; // inventory reads + module runs
        _progress.Start("Progress_FullScan", "SYS", totalSteps);
        var stopwatch = Stopwatch.StartNew();

        var inventory = await ReadInventoryDataAsync(cancellationToken).ConfigureAwait(false);
        RegisterInventoryProblems(inventory);

        var moduleResults = new List<ModuleResult>();
        var components = new List<HardwareComponent>(BuildInventoryComponents(inventory));

        foreach (var module in _modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await RunModuleSafelyAsync(module, inventory, cancellationToken).ConfigureAwait(false);
            moduleResults.Add(result);
            components.AddRange(result.Components);
            _events.Publish(new ModuleCompletedEvent(result));
        }

        stopwatch.Stop();
        var snapshot = await BuildSnapshotAsync(scanId, inventory, moduleResults, components, stopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
        LastSnapshot = snapshot;

        _progress.Complete(true);
        _events.Publish(new ScanCompletedEvent(scanId, snapshot));
        await PersistAsync(snapshot, cancellationToken).ConfigureAwait(false);
        Raise(snapshot);
        return snapshot;
    }

    public async Task<SystemSnapshot> RunModuleAsync(string moduleId, CancellationToken cancellationToken)
    {
        var module = _modules.FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown module '{moduleId}'.", nameof(moduleId));

        // A snapshot describes one pass. Whatever an earlier pass found is dropped, because
        // presenting it as a result of this pass would be a fabricated finding.
        _problems.Clear();
        var inventory = await ReadInventoryDataAsync(cancellationToken).ConfigureAwait(false);
        RegisterInventoryProblems(inventory);

        _events.Publish(new ScanStartedEvent(moduleId, 1, _clock.Now));
        _state.TryTransitionTo(SystemState.Discovery, $"module:{moduleId}");
        var stopwatch = Stopwatch.StartNew();
        var result = await RunModuleSafelyAsync(module, inventory, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        var components = BuildInventoryComponents(inventory).Concat(result.Components).ToList();
        var snapshot = await BuildSnapshotAsync($"SCAN-{_clock.Now:yyyyMMddHHmmss}", inventory, new[] { result }, components, stopwatch.Elapsed, cancellationToken).ConfigureAwait(false);
        LastSnapshot = snapshot;
        _events.Publish(new ScanCompletedEvent(snapshot.Id, snapshot));
        await PersistAsync(snapshot, cancellationToken).ConfigureAwait(false);
        Raise(snapshot);
        return snapshot;
    }

    public async Task<SystemSnapshot> ReadInventoryAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        // Same rule as for a scan: this snapshot contains what this pass found, nothing else.
        _problems.Clear();
        var inventory = await ReadInventoryDataAsync(cancellationToken).ConfigureAwait(false);
        RegisterInventoryProblems(inventory);
        stopwatch.Stop();

        var snapshot = await BuildSnapshotAsync(
            $"SNAP-{_clock.Now:yyyyMMddHHmmss}",
            inventory,
            Array.Empty<ModuleResult>(),
            BuildInventoryComponents(inventory),
            stopwatch.Elapsed,
            cancellationToken).ConfigureAwait(false);

        LastSnapshot = snapshot;
        Raise(snapshot);
        return snapshot;
    }

    /// <summary>
    /// Reads the inventory for this pass. Every pass reads, nothing is carried over.
    ///
    /// The name differs from the public method on purpose: C# cannot tell two methods apart by their
    /// return type alone, and a public method that calls itself would recurse until the stack ends.
    /// </summary>
    private async Task<InventoryResult> ReadInventoryDataAsync(CancellationToken cancellationToken) =>
        await new InventoryReader(_provider, _protocol, _progress).ReadAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Turns failed inventory reads into problems. Without this the failures only appeared in the
    /// log file: the read returned an empty value, no problem was registered, and a scan with a
    /// lost WMI class could still be reported as healthy - the exact "looks fine because the data
    /// is missing" failure the specification forbids (spec sections 1.3, 61).
    /// </summary>
    private void RegisterInventoryProblems(InventoryResult inventory)
    {
        if (ReferenceEquals(_registeredInventory, inventory))
        {
            // The same inventory is reused for a later module run; its problems are already in.
            return;
        }

        _registeredInventory = inventory;

        if (inventory.Problems.Count > 0)
        {
            _problems.AddRange(inventory.Problems);
        }

        if (inventory.FailedReads > 0 || inventory.Problems.Count > 0)
        {
            _protocol.Warning(
                "SYS",
                LocalizedText.Of("Protocol_ReadFailures", inventory.FailedReads, inventory.SuccessfulReads),
                string.Join(" | ", inventory.Notes.Take(3)));
        }
    }

    private async Task<ModuleResult> RunModuleSafelyAsync(IDiagnosticModule module, InventoryResult inventory, CancellationToken cancellationToken)
    {
        var context = new DiagnosticContext
        {
            Hardware = _provider,
            Protocol = _protocol,
            Events = _events,
            Progress = _progress,
            Problems = _problems,
            Clock = _clock,
            Environment = _environment,
            Settings = _settings,
            Services = _services,
            Offline = _environment.IsOfflineRequested,
            Simulation = _provider.IsSimulation,
            CancellationToken = cancellationToken,
        };

        if (module.RequiresAdministrator && !_environment.IsElevated)
        {
            var skipReason = LocalizedText.Of("Module_Skipped_NotElevated");
            _protocol.Warning(module.Id.ToUpperInvariant(), skipReason);
            return new ModuleResult
            {
                ModuleId = module.Id,
                DisplayNameKey = module.DisplayNameKey,
                Category = module.Category,
                Status = HealthStatus.Unknown,
                WasSkipped = true,
                SkipReasonCode = BlockReasons.NotElevated,
                SkipReason = skipReason,
            };
        }

        if (module.RequiresNetwork && (context.Offline || !_settings.Current.UpdateCheckEnabled))
        {
            var skipReason = LocalizedText.Of(context.Offline ? "Module_Skipped_Offline" : "Module_Skipped_UpdatesDisabled");
            _protocol.Info(module.Id.ToUpperInvariant(), skipReason);
            return new ModuleResult
            {
                ModuleId = module.Id,
                DisplayNameKey = module.DisplayNameKey,
                Category = module.Category,
                Status = HealthStatus.Unknown,
                WasSkipped = true,
                SkipReasonCode = context.Offline ? BlockReasons.OfflineMode : "UPDATE_CHECKS_DISABLED",
                SkipReason = skipReason,
            };
        }

        try
        {
            return await module.RunAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Module {Module} failed", module.Id);
            return new ModuleResult
            {
                ModuleId = module.Id,
                DisplayNameKey = module.DisplayNameKey,
                Category = module.Category,
                Status = HealthStatus.Unknown,
                Evidence = new[] { $"{ex.GetType().Name}: {ex.Message}" },
            };
        }
    }

    private async Task<SystemSnapshot> BuildSnapshotAsync(
        string scanId,
        InventoryResult inventory,
        IReadOnlyList<ModuleResult> moduleResults,
        IReadOnlyList<HardwareComponent> components,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var problems = _problems.All;
        var status = OverallStatusCalculator.Calculate(problems, moduleResults, out var summary);

        WorkloadAssessment workload;
        var detector = _services.GetService(typeof(IWorkloadDetector)) as IWorkloadDetector;
        if (detector is null)
        {
            workload = new WorkloadAssessment();
        }
        else
        {
            workload = await detector.DetectAsync(null, cancellationToken).ConfigureAwait(false);
        }

        var snapshot = new SystemSnapshot
        {
            Id = scanId,
            CapturedAt = _clock.Now,
            Duration = duration,
            Windows = inventory.Windows,
            System = inventory.System,
            Bios = inventory.Bios,
            Motherboard = inventory.Motherboard,
            Processors = inventory.Processors,
            Memory = inventory.Memory,
            Graphics = inventory.Graphics,
            Storage = ApplyReliability(inventory.Storage, inventory.StorageReliability),
            Network = inventory.Network,
            Audio = inventory.Audio,
            Monitors = inventory.Monitors,
            Printers = inventory.Printers,
            Battery = inventory.Battery,
            PnpDevices = inventory.PnpDevices,
            Drivers = inventory.Drivers,
            Components = components.OrderBy(c => c.SortOrder).ThenBy(c => c.Name.Display, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Problems = problems,
            Modules = moduleResults,
            Changes = Array.Empty<DetectedChange>(),
            OverallStatus = status,
            OverallSummary = summary,
            Workload = workload,
            IsSimulation = _provider.IsSimulation,
            InventoryFailedReads = inventory.FailedReads,
            InventoryNotes = inventory.Notes,
            ApplicationVersion = _buildInfo?.Get().Version ?? "unknown",
        };

        _state.TryTransitionTo(SystemState.Diagnostic, "snapshot-built");
        _state.TryTransitionTo(StateFromHealth(status), "scan-result");
        _protocol.Info("SYS", summary, status switch
        {
            HealthStatus.Critical => Severity.Critical,
            HealthStatus.Warning => Severity.Error,
            HealthStatus.Attention => Severity.Warning,
            HealthStatus.Healthy => Severity.Success,
            _ => Severity.Info,
        }, $"problems={problems.Count} modules={moduleResults.Count} duration={duration.TotalSeconds:F1}s");

        return snapshot;
    }

    private async Task PersistAsync(SystemSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (_snapshotStore is not null)
        {
            try
            {
                await _snapshotStore.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Snapshot could not be stored.");
                _protocol.Warning("SYS", LocalizedText.Of("Protocol_SnapshotNotStored"), ex.Message);
            }
        }

        if (_historyStore is null)
        {
            return;
        }

        var counts = snapshot.ProblemCounts;
        try
        {
            await _historyStore.AppendAsync(new HistoryEntry
            {
                Id = $"HIST-{_clock.Now:yyyyMMddHHmmss}-{snapshot.Id}",
                Timestamp = snapshot.CapturedAt,
                Kind = "scan",
                OperationKey = "History_Operation_Scan",
                OverallStatus = snapshot.OverallStatus,
                ProblemCount = counts.Total,
                CriticalCount = counts.Critical,
                WarningCount = counts.Warnings,
                InformationCount = counts.Information,
                Duration = snapshot.Duration,
                ProblemIds = snapshot.Problems.Select(p => p.Id).ToList(),
                ApplicationVersion = snapshot.ApplicationVersion,
                IsSimulation = snapshot.IsSimulation,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "History entry could not be stored.");
        }
    }

    /// <summary>
    /// How a finished run ends. "Warnings" are not a state of the machine (chapter 40 lists the
    /// allowed states), they are part of the result. What matters here is only: did the run end with
    /// a finding, did it end clean, or did it produce no definitive answer? The last case is
    /// BLOCKED, never SUCCESS - a run that learned nothing must not look like a clean run
    /// (chapters 86, 101).
    /// </summary>
    private static SystemState StateFromHealth(HealthStatus status) => status switch
    {
        HealthStatus.Critical => SystemState.Error,
        HealthStatus.Warning => SystemState.Error,
        HealthStatus.Attention => SystemState.Error,
        HealthStatus.Healthy => SystemState.Success,
        _ => SystemState.Blocked,
    };

    private static IReadOnlyList<StorageDeviceInfo> ApplyReliability(
        IReadOnlyList<StorageDeviceInfo> devices,
        IReadOnlyList<StorageReliabilityCounter> counters)
    {
        if (devices.Count == 0 || counters.Count == 0)
        {
            return devices;
        }

        var result = new List<StorageDeviceInfo>(devices.Count);
        foreach (var device in devices)
        {
            var counter = counters.FirstOrDefault(c =>
                !string.IsNullOrEmpty(c.DeviceId) &&
                (string.Equals(c.DeviceId, device.SerialNumber.Value, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.DeviceId, device.Model.Value, StringComparison.OrdinalIgnoreCase)));

            if (counter is null)
            {
                result.Add(device);
                continue;
            }

            result.Add(device with
            {
                TemperatureCelsius = counter.TemperatureCelsius,
                PercentageUsed = counter.PercentageUsed,
                PowerOnHours = counter.PowerOnHours,
                ReadErrorsTotal = counter.ReadErrorsTotal,
                WriteErrorsTotal = counter.WriteErrorsTotal,
                MediaErrorsTotal = counter.MediaErrorsTotal,
                PowerCycleCount = counter.PowerCycleCount,
                BytesWrittenTbw = counter.NvmeBytesWritten,
                SmartAvailable = true,
                IsCritical = device.IsCritical
                    || (counter.MediaErrorsTotal.HasValue && counter.MediaErrorsTotal.Value > 0)
                    || (counter.PercentageUsed.HasValue && counter.PercentageUsed.Value >= 95),
            });
        }

        return result;
    }

    /// <summary>
    /// Builds the components that come straight from the inventory (system, board, CPU, RAM, GPU,
    /// storage, network, ...). Modules add their own analysis components on top.
    /// </summary>
    private IReadOnlyList<HardwareComponent> BuildInventoryComponents(InventoryResult inventory)
    {
        var origin = ValueOrigin.Wmi(_clock.Now, "inventory", SensorQuality.High);
        var components = new List<HardwareComponent>();

        components.Add(new HardwareComponent
        {
            Id = "system",
            Category = ComponentCategory.System,
            CategoryKey = "Component_System",
            Name = inventory.System.ComputerModel.IsKnown
                ? inventory.System.ComputerModel
                : TextInfo.Unknown(origin, "Win32_ComputerSystem.Model was not reported"),
            Manufacturer = inventory.System.Manufacturer,
            Model = inventory.System.ComputerModel,
            Status = HealthStatus.Unknown,
            DeviceInstanceId = TextInfo.Unknown(origin, "system identity has no device instance id"),
            SortOrder = 0,
        });

        foreach (var (processor, index) in inventory.Processors.Select((p, i) => (p, i)))
        {
            components.Add(new HardwareComponent
            {
                Id = $"cpu-{index}",
                Category = ComponentCategory.Cpu,
                CategoryKey = "Component_Cpu",
                Name = processor.Name,
                Manufacturer = processor.Manufacturer,
                Model = processor.Name,
                Status = HealthStatus.Unknown,
                DeviceInstanceId = TextInfo.Unknown(origin, "no device instance id on Win32_Processor"),
                SortOrder = 10 + index,
            });
        }

        components.Add(new HardwareComponent
        {
            Id = "mainboard",
            Category = ComponentCategory.Motherboard,
            CategoryKey = "Component_Mainboard",
            Name = inventory.Motherboard.Product,
            Manufacturer = inventory.Motherboard.Manufacturer,
            Model = inventory.Motherboard.Product,
            Status = HealthStatus.Unknown,
            DeviceInstanceId = TextInfo.Unknown(origin, "mainboard is enumerated through SMBIOS"),
            SortOrder = 20,
        });

        for (var i = 0; i < inventory.Graphics.Count; i++)
        {
            var gpu = inventory.Graphics[i];
            components.Add(new HardwareComponent
            {
                Id = $"gpu-{i}",
                Category = ComponentCategory.Graphics,
                CategoryKey = "Component_Graphics",
                Name = gpu.Name,
                Manufacturer = gpu.Manufacturer,
                Model = gpu.Name,
                Status = HealthStatus.Unknown,
                DeviceInstanceId = gpu.PnpDeviceId,
                SortOrder = 30 + i,
            });
        }

        for (var i = 0; i < inventory.Storage.Count; i++)
        {
            var disk = inventory.Storage[i];
            components.Add(new HardwareComponent
            {
                Id = $"storage-{i}",
                Category = ComponentCategory.Storage,
                CategoryKey = disk.IsNvme ? "Component_Nvme" : "Component_Storage",
                Name = disk.FriendlyName.IsKnown ? disk.FriendlyName : disk.Model,
                Manufacturer = disk.Manufacturer,
                Model = disk.Model,
                Status = HealthStatus.Unknown,
                HardwareIds = Array.Empty<string>(),
                DeviceInstanceId = TextInfo.Unknown(origin, "storage devices are not PnP nodes"),
                SortOrder = 40 + i,
            });
        }

        for (var i = 0; i < inventory.Network.Count; i++)
        {
            var adapter = inventory.Network[i];
            components.Add(new HardwareComponent
            {
                Id = $"net-{i}",
                Category = ComponentCategory.Network,
                CategoryKey = "Component_Network",
                Name = adapter.Name.IsKnown ? adapter.Name : adapter.Description,
                Manufacturer = adapter.Manufacturer,
                Model = adapter.Description,
                Status = HealthStatus.Unknown,
                DeviceInstanceId = adapter.PnpDeviceId,
                SortOrder = 60 + i,
            });
        }

        for (var i = 0; i < inventory.Audio.Count; i++)
        {
            var audio = inventory.Audio[i];
            components.Add(new HardwareComponent
            {
                Id = $"audio-{i}",
                Category = ComponentCategory.Audio,
                CategoryKey = "Component_Audio",
                Name = audio.Name,
                Manufacturer = audio.Manufacturer,
                Model = audio.Name,
                Status = HealthStatus.Unknown,
                DeviceInstanceId = audio.PnpDeviceId,
                SortOrder = 70 + i,
            });
        }

        for (var i = 0; i < inventory.Monitors.Count; i++)
        {
            var monitor = inventory.Monitors[i];
            components.Add(new HardwareComponent
            {
                Id = $"monitor-{i}",
                Category = ComponentCategory.Monitor,
                CategoryKey = "Component_Monitor",
                Name = monitor.Name,
                Manufacturer = monitor.Manufacturer,
                Model = monitor.ProductCode,
                Status = HealthStatus.Unknown,
                DeviceInstanceId = monitor.PnpDeviceId,
                SortOrder = 80 + i,
            });
        }

        if (inventory.Battery is not null)
        {
            components.Add(new HardwareComponent
            {
                Id = "battery",
                Category = ComponentCategory.Battery,
                CategoryKey = "Component_Battery",
                Name = inventory.Battery.Name,
                Manufacturer = inventory.Battery.Manufacturer,
                Model = inventory.Battery.Name,
                Status = HealthStatus.Unknown,
                DeviceInstanceId = TextInfo.Unknown(origin, "battery is enumerated through the power subsystem"),
                SortOrder = 90,
            });
        }

        return components;
    }

    private void Raise(SystemSnapshot snapshot)
    {
        var handler = SnapshotCompleted;
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler<SystemSnapshot> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, snapshot);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Snapshot subscriber failed.");
            }
        }
    }
}
