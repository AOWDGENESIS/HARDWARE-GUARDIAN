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

    /// <summary>
    /// Names the operation whose changes follow, so that every journal entry says which job was
    /// running. Without it a crash leaves "<c>EXECUTING</c> at 14:02" behind, and nobody can tell
    /// what was being executed (spec section 41, M35-F-001).
    /// </summary>
    void BeginOperation(string operationId, string? actionId = null);

    /// <summary>Ends the current operation; further changes are recorded without one.</summary>
    void EndOperation();
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
    private readonly IStateJournal? _journal;
    private readonly object _gate = new();
    private readonly List<StateChangedEvent> _history = new();
    private SystemState _current = SystemState.Initializing;
    private DateTimeOffset _since;
    private string? _operationId;
    private string? _actionId;

    /// <param name="journal">
    /// Where every change is written down (M34-F-001). Optional so that a test or a simulated run can
    /// work without a file; when it is missing, nothing claims that a state was stored.
    /// </param>
    public SystemStateMachine(
        IClock clock,
        IEventBus? events = null,
        ILogger<SystemStateMachine>? logger = null,
        IStateJournal? journal = null)
    {
        _clock = clock;
        _events = events;
        _logger = logger;
        _journal = journal;
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

            // Written under the same lock as the change itself: when the process dies, the journal
            // must not be behind the state or ahead of it (spec section 40, M34-F-001/M34-F-002).
            if (_journal is not null)
            {
                try
                {
                    _journal.Record(new StateJournalEntry
                    {
                        From = change.Previous,
                        To = change.Current,
                        At = now,
                        Reason = reason,
                        OperationId = _operationId,
                        ActionId = _actionId,
                    });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A state that could not be stored is a finding, not a reason to kill the running
                    // operation. It is logged loudly, because an unstored change is exactly the defect
                    // M34-F-001 names.
                    _logger?.LogError(ex, "The state change {Previous} -> {Current} could not be written to the journal.",
                        change.Previous, change.Current);
                }
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

    public void BeginOperation(string operationId, string? actionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        lock (_gate)
        {
            _operationId = operationId;
            _actionId = actionId;
        }
    }

    public void EndOperation()
    {
        lock (_gate)
        {
            _operationId = null;
            _actionId = null;
        }
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
