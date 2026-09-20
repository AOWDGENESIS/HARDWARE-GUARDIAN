using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Events;
using Microsoft.Extensions.Logging;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>Raised when a state change is requested that the machine does not allow.</summary>
public sealed class InvalidStateTransitionException : InvalidOperationException
{
    public InvalidStateTransitionException(SystemState from, SystemState to)
        : base($"Transition {from} -> {to} is not allowed.")
    {
        From = from;
        To = to;
    }

    public SystemState From { get; }

    public SystemState To { get; }
}

/// <summary>Central state machine (spec section 5). Every transition is logged.</summary>
public interface ISystemStateMachine
{
    event EventHandler<StateChangedEvent>? StateChanged;

    SystemState Current { get; }

    DateTimeOffset Since { get; }

    IReadOnlyList<StateChangedEvent> History { get; }

    bool CanTransitionTo(SystemState next);

    /// <summary>Performs the transition or throws <see cref="InvalidStateTransitionException"/>.</summary>
    StateChangedEvent TransitionTo(SystemState next, string? reason = null);

    /// <summary>Performs the transition only when it is allowed; returns false otherwise.</summary>
    bool TryTransitionTo(SystemState next, string? reason = null);
}

/// <summary>
/// Thread safe implementation. The allowed transitions are an explicit table, so that a bug in
/// a caller cannot silently produce an undefined state such as "executing while idle".
/// </summary>
public sealed class SystemStateMachine : ISystemStateMachine
{
    private readonly IClock _clock;
    private readonly IEventBus? _events;
    private readonly ILogger<SystemStateMachine>? _logger;
    private readonly object _gate = new();
    private readonly List<StateChangedEvent> _history = new();
    private SystemState _current = SystemState.Initializing;
    private DateTimeOffset _since;

    public SystemStateMachine(IClock clock, IEventBus? events = null, ILogger<SystemStateMachine>? logger = null)
    {
        _clock = clock;
        _events = events;
        _logger = logger;
        _since = clock.Now;
    }

    public event EventHandler<StateChangedEvent>? StateChanged;

    public SystemState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public DateTimeOffset Since
    {
        get
        {
            lock (_gate)
            {
                return _since;
            }
        }
    }

    public IReadOnlyList<StateChangedEvent> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToList();
            }
        }
    }

    /// <summary>Allowed transitions. Anything not listed here is refused.</summary>
    private static readonly IReadOnlyDictionary<SystemState, SystemState[]> Allowed = BuildTable();

    private static IReadOnlyDictionary<SystemState, SystemState[]> BuildTable()
    {
        // "terminal" = a finished run. From there a new run may start (DISCOVERY, DIAGNOSTIC) or
        // the machine may go back to INITIALIZING, for example when the application is restarted.
        var terminal = new[] { SystemState.Initializing, SystemState.Discovery, SystemState.Diagnostic };

        return new Dictionary<SystemState, SystemState[]>
        {
            [SystemState.Initializing] = new[]
            {
                SystemState.Discovery, SystemState.Diagnostic, SystemState.PlanGenerated, SystemState.AwaitingApproval,
                SystemState.Backup, SystemState.Executing, SystemState.Validating, SystemState.Blocked,
                SystemState.Error, SystemState.Success, SystemState.Cancelled, SystemState.Rollback, SystemState.Recovering,
            },

            // Reads are read only; the run may move on to the assessment, but never straight into an
            // execution: that would skip the plan, the approval and the validation (chapter 40, M34-S-001).
            [SystemState.Discovery] = new[]
            {
                SystemState.Diagnostic, SystemState.PlanGenerated, SystemState.AwaitingApproval, SystemState.Validating,
                SystemState.Success, SystemState.Blocked, SystemState.Error, SystemState.Cancelled,
                SystemState.Rollback, SystemState.Recovering, SystemState.Initializing,
            },
            [SystemState.Diagnostic] = new[]
            {
                SystemState.PlanGenerated, SystemState.AwaitingApproval, SystemState.Backup, SystemState.Executing,
                SystemState.Validating, SystemState.Success, SystemState.Blocked, SystemState.Error,
                SystemState.Cancelled, SystemState.Rollback, SystemState.Recovering, SystemState.Initializing,
            },
            [SystemState.PlanGenerated] = new[]
            {
                SystemState.AwaitingApproval, SystemState.Backup, SystemState.Executing, SystemState.Cancelled,
                SystemState.Blocked, SystemState.Error, SystemState.Diagnostic, SystemState.Initializing,
            },
            [SystemState.AwaitingApproval] = new[]
            {
                SystemState.Backup, SystemState.Executing, SystemState.PlanGenerated, SystemState.Cancelled,
                SystemState.Blocked, SystemState.Error, SystemState.Diagnostic, SystemState.Initializing,
            },
            [SystemState.Backup] = new[]
            {
                SystemState.Executing, SystemState.Cancelled, SystemState.Blocked, SystemState.Error,
                SystemState.Rollback, SystemState.Recovering, SystemState.Initializing,
            },
            [SystemState.Executing] = new[]
            {
                SystemState.Validating, SystemState.Blocked, SystemState.Error, SystemState.Cancelled,
                SystemState.Rollback, SystemState.Recovering, SystemState.Initializing,
            },
            [SystemState.Validating] = new[]
            {
                SystemState.Success, SystemState.Blocked, SystemState.Error, SystemState.Rollback,
                SystemState.Recovering, SystemState.Initializing,
            },
            [SystemState.Success] = terminal,
            [SystemState.Error] = terminal.Concat(new[] { SystemState.Rollback, SystemState.Recovering }).ToArray(),
            [SystemState.Blocked] = terminal.Concat(new[] { SystemState.AwaitingApproval, SystemState.Recovering }).ToArray(),
            [SystemState.Rollback] = new[]
            {
                SystemState.Validating, SystemState.Success, SystemState.Error, SystemState.Cancelled,
                SystemState.Recovering, SystemState.Initializing,
            },

            // RECOVERING is reachable from every state a crash can interrupt, and it leads back into
            // either a rollback, a validation or a controlled stop - never straight into an execution.
            [SystemState.Recovering] = new[]
            {
                SystemState.Rollback, SystemState.Validating, SystemState.Success, SystemState.Error,
                SystemState.Blocked, SystemState.Cancelled, SystemState.Diagnostic, SystemState.Initializing,
            },
            [SystemState.Cancelled] = terminal
                .Concat(new[] { SystemState.AwaitingApproval, SystemState.Backup, SystemState.Executing, SystemState.Recovering })
                .ToArray(),
        };
    }

    public bool CanTransitionTo(SystemState next)
    {
        lock (_gate)
        {
            if (_current == next)
            {
                return false;
            }

            return Allowed.TryGetValue(_current, out var targets) && targets.Contains(next);
        }
    }

    public StateChangedEvent TransitionTo(SystemState next, string? reason = null)
    {
        StateChangedEvent change;
        lock (_gate)
        {
            if (_current == next)
            {
                throw new InvalidStateTransitionException(_current, next);
            }

            if (!Allowed.TryGetValue(_current, out var targets) || !targets.Contains(next))
            {
                throw new InvalidStateTransitionException(_current, next);
            }

            var now = _clock.Now;
            change = new StateChangedEvent(_current, next, now, reason);
            _current = next;
            _since = now;
            _history.Add(change);
            if (_history.Count > 500)
            {
                _history.RemoveAt(0);
            }
        }

        _logger?.LogInformation("State changed {Previous} -> {Current} ({Reason})", change.Previous, change.Current, change.Reason ?? "-");
        _events?.Publish(change);
        Raise(change);
        return change;
    }

    public bool TryTransitionTo(SystemState next, string? reason = null)
    {
        if (!CanTransitionTo(next))
        {
            return false;
        }

        TransitionTo(next, reason);
        return true;
    }

    private void Raise(StateChangedEvent change)
    {
        var handler = StateChanged;
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler<StateChangedEvent> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, change);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "State change subscriber failed.");
            }
        }
    }
}
