using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Abstractions;

/// <summary>Minimal, allocation friendly event bus (spec section 69).</summary>
public interface IEventBus
{
    void Publish<TEvent>(TEvent @event) where TEvent : notnull;

    IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : notnull;

    int SubscriberCount { get; }
}

/// <summary>
/// The always visible live protocol (spec section 20). Entries carry localisation keys,
/// therefore switching the language re-renders the whole history correctly.
/// </summary>
public interface ILiveProtocol
{
    event EventHandler<ProtocolEntry>? EntryAdded;

    IReadOnlyList<ProtocolEntry> Snapshot();

    ProtocolEntry Publish(string module, LocalizedText action, Severity severity, string? detail = null);

    void Clear();

    int MaxEntries { get; set; }

    long Sequence { get; }
}

/// <summary>Convenience helpers so that call sites stay short and severity consistent.</summary>
public static class LiveProtocolExtensions
{
    public static ProtocolEntry Info(this ILiveProtocol protocol, string module, LocalizedText action, string? detail = null) =>
        protocol.Publish(module, action, Severity.Info, detail);

    public static ProtocolEntry Success(this ILiveProtocol protocol, string module, LocalizedText action, string? detail = null) =>
        protocol.Publish(module, action, Severity.Success, detail);

    public static ProtocolEntry Warning(this ILiveProtocol protocol, string module, LocalizedText action, string? detail = null) =>
        protocol.Publish(module, action, Severity.Warning, detail);

    public static ProtocolEntry Error(this ILiveProtocol protocol, string module, LocalizedText action, string? detail = null) =>
        protocol.Publish(module, action, Severity.Error, detail);

    public static ProtocolEntry Critical(this ILiveProtocol protocol, string module, LocalizedText action, string? detail = null) =>
        protocol.Publish(module, action, Severity.Critical, detail);

    public static ProtocolEntry Blocked(this ILiveProtocol protocol, string module, LocalizedText action, string? detail = null) =>
        protocol.Publish(module, action, Severity.Blocked, detail);

    public static ProtocolEntry Report(this ILiveProtocol protocol, string module, LocalizedText action, Severity severity, string? detail = null) =>
        protocol.Publish(module, action, severity, detail);
}

/// <summary>
/// Progress reporting for long running operations (spec section 21). An estimated remaining
/// time is only published when the estimator can defend it (see ProgressReporter).
/// </summary>
public interface IProgressReporter
{
    event EventHandler<ProgressSnapshot>? ProgressChanged;

    ProgressSnapshot Current { get; }

    void Start(string operationKey, string module, int? totalSteps);

    void ReportStep(string stepKey, string? detail = null);

    void ReportFraction(double fraction, string stepKey, string? detail = null);

    void Complete(bool success);

    void Reset();

    /// <summary>Creates a scoped reporter that reports into this one with a module prefix.</summary>
    IProgressScope BeginScope(string operationKey, string module, int? totalSteps);
}

/// <summary>Scoped progress helper. Disposing completes the scope.</summary>
public interface IProgressScope : IDisposable
{
    void Step(string stepKey, string? detail = null);

    void Complete(bool success);
}
