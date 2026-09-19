using HardwareGuardian.Core.Models;

namespace HardwareGuardian.Core.Abstractions;

/// <summary>
/// One diagnostic module. Modules are pure with respect to the machine: they read and analyse,
/// they never change anything. All state changing work goes through an action with approval.
/// </summary>
public interface IDiagnosticModule
{
    string Id { get; }

    /// <summary>Localisation key of the module name, e.g. <c>Module_Cpu</c>.</summary>
    string DisplayNameKey { get; }

    ComponentCategory Category { get; }

    /// <summary>True when the module can only read its full evidence with administrator rights.</summary>
    bool RequiresAdministrator { get; }

    /// <summary>True when the module needs network access (manufacturer sources, update checks).</summary>
    bool RequiresNetwork { get; }

    Task<ModuleResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken);
}

/// <summary>Everything a module is allowed to use. No module resolves its own dependencies.</summary>
public sealed class DiagnosticContext
{
    public required IHardwareProvider Hardware { get; init; }

    public required ILiveProtocol Protocol { get; init; }

    public required IEventBus Events { get; init; }

    public required IProgressReporter Progress { get; init; }

    public required IProblemRegistry Problems { get; init; }

    public required IClock Clock { get; init; }

    public required IEnvironmentProbe Environment { get; init; }

    public required ISettingsService Settings { get; init; }

    public required IServiceProvider Services { get; init; }

    public bool Offline { get; init; }

    public bool Simulation { get; init; }

    public CancellationToken CancellationToken { get; init; }

    public T? GetService<T>() where T : class => Services.GetService(typeof(T)) as T;

    public T GetRequiredService<T>() where T : class =>
        GetService<T>() ?? throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
}

/// <summary>Runs the individual modules and aggregates the result (spec section 86, phases 2 and 5).</summary>
public interface IScanOrchestrator
{
    IReadOnlyList<IDiagnosticModule> Modules { get; }

    SystemSnapshot? LastSnapshot { get; }

    event EventHandler<SystemSnapshot>? SnapshotCompleted;

    Task<SystemSnapshot> RunFullScanAsync(CancellationToken cancellationToken);

    Task<SystemSnapshot> RunModuleAsync(string moduleId, CancellationToken cancellationToken);

    /// <summary>Reads every provider without running the analysis modules (used at startup).</summary>
    Task<SystemSnapshot> ReadInventoryAsync(CancellationToken cancellationToken);
}
