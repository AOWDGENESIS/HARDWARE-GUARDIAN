using WindowsMaintenanceCenter.Core;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// One state change as it is written down (spec section 40, M34-F-001/M34-F-002).
///
/// The entry carries more than from/to: the operation it belongs to and - when there is one - the
/// registered action. That is what makes the difference between "the application was in EXECUTING"
/// and "the application was executing action X of operation Y", and only the second form answers
/// M35-F-001 (the last action has to be recognised) after a crash.
/// </summary>
public sealed record StateJournalEntry
{
    public SystemState From { get; init; }

    public SystemState To { get; init; }

    public DateTimeOffset At { get; init; }

    public string? Reason { get; init; }

    /// <summary>Operation this change belongs to, or null when no operation has begun.</summary>
    public string? OperationId { get; init; }

    /// <summary>Registered action that was running, when the operation names one.</summary>
    public string? ActionId { get; init; }

    /// <summary>True for the states a crash can interrupt in the middle (spec section 41).</summary>
    public static bool IsInterruptible(SystemState state) => state is
        SystemState.Backup or SystemState.Executing or SystemState.Validating or SystemState.Rollback or SystemState.Recovering;
}

/// <summary>
/// Where the state machine writes its changes down (M34-F-001: every state is stored).
///
/// The journal is append only and is read back after a restart: the last entry tells whether the
/// previous run ended in a finished state or was cut off in the middle. Without this file an
/// interrupted job is invisible, and an invisible job is one that nobody can recover - which is
/// exactly the defect M34-R-001 names.
/// </summary>
public interface IStateJournal
{
    /// <summary>Where the entries are kept, for the report and the protocol.</summary>
    string Location { get; }

    /// <summary>Appends one entry. A failure to write must not be swallowed silently by the caller.</summary>
    void Record(StateJournalEntry entry);

    /// <summary>All entries in the order they were written.</summary>
    IReadOnlyList<StateJournalEntry> Read();
}

/// <summary>
/// Journal that only lives in memory: the default for tests and for a run that deliberately keeps
/// nothing (simulation). It is never a substitute for the file journal in the application.
/// </summary>
public sealed class InMemoryStateJournal : IStateJournal
{
    private readonly List<StateJournalEntry> _entries = new();
    private readonly object _gate = new();

    public string Location => "in memory";

    public void Record(StateJournalEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    public IReadOnlyList<StateJournalEntry> Read()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }
}
