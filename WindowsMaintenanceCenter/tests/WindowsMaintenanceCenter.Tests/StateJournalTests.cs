using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Services;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The state journal (specification chapters 40 and 41).
///
/// Two requirements are pinned here:
///
/// 1. M34-F-001/M34-F-002 - every state change is stored and the transitions are logged, including
///    the operation they belong to. A transition that was refused must not appear: the journal has to
///    show what happened, not what somebody tried.
/// 2. M34-R-001 - the journal survives the process, so an interrupted job can be recognised after a
///    restart. The file test damages the last line on purpose: a crash truncates the line it was
///    writing, and everything before it has to stay readable.
/// </summary>
public sealed class StateJournalTests
{
    [Fact]
    public void Every_state_change_is_recorded()
    {
        var journal = new InMemoryStateJournal();
        var machine = new SystemStateMachine(new FakeClock(), events: null, logger: null, journal: journal);

        machine.TryTransitionTo(SystemState.Discovery, "start");
        machine.TryTransitionTo(SystemState.Diagnostic, "assess");

        var entries = journal.Read();

        Assert.Equal(2, entries.Count);
        Assert.Equal(SystemState.Initializing, entries[0].From);
        Assert.Equal(SystemState.Discovery, entries[0].To);
        Assert.Equal("start", entries[0].Reason);
        Assert.Equal(SystemState.Discovery, entries[1].From);
        Assert.Equal(SystemState.Diagnostic, entries[1].To);
    }

    [Fact]
    public void A_refused_transition_leaves_no_entry()
    {
        var journal = new InMemoryStateJournal();
        var machine = new SystemStateMachine(new FakeClock(), events: null, logger: null, journal: journal);

        // INITIALIZING -> EXECUTING is not in the table: an execution without plan, approval and
        // validation must not become a stored fact (M34-S-001).
        Assert.False(machine.TryTransitionTo(SystemState.Validating, "not allowed from initializing"));

        Assert.Empty(journal.Read());
    }

    [Fact]
    public void The_entries_name_the_operation_and_the_action()
    {
        var journal = new InMemoryStateJournal();
        var machine = new SystemStateMachine(new FakeClock(), events: null, logger: null, journal: journal);

        machine.BeginOperation("op-4711", "System.IpConfigAll");
        machine.TryTransitionTo(SystemState.Discovery, "start");
        machine.TryTransitionTo(SystemState.Diagnostic, "assess");
        machine.EndOperation();
        machine.TryTransitionTo(SystemState.PlanGenerated, "plan");

        var entries = journal.Read();

        Assert.All(entries.Take(2), entry =>
        {
            Assert.Equal("op-4711", entry.OperationId);
            Assert.Equal("System.IpConfigAll", entry.ActionId);
        });
        Assert.Null(entries[2].OperationId);
    }

    [Fact]
    public void Only_the_states_a_crash_can_interrupt_count_as_interrupted()
    {
        Assert.True(StateJournalEntry.IsInterruptible(SystemState.Executing));
        Assert.True(StateJournalEntry.IsInterruptible(SystemState.Backup));
        Assert.True(StateJournalEntry.IsInterruptible(SystemState.Validating));
        Assert.True(StateJournalEntry.IsInterruptible(SystemState.Rollback));
        Assert.True(StateJournalEntry.IsInterruptible(SystemState.Recovering));

        Assert.False(StateJournalEntry.IsInterruptible(SystemState.Success));
        Assert.False(StateJournalEntry.IsInterruptible(SystemState.Cancelled));
        Assert.False(StateJournalEntry.IsInterruptible(SystemState.Blocked));
        Assert.False(StateJournalEntry.IsInterruptible(SystemState.Initializing));
        Assert.False(StateJournalEntry.IsInterruptible(SystemState.AwaitingApproval));
    }

    [Fact]
    public void The_file_journal_survives_a_new_instance_and_tolerates_a_truncated_last_line()
    {
        using var paths = new TempPathProvider();
        var first = new WindowsMaintenanceCenter.Infrastructure.Persistence.FileStateJournal(paths);
        var machine = new SystemStateMachine(new FakeClock(), events: null, logger: null, journal: first);

        machine.BeginOperation("op-1", "Volume.ChkdskScan");
        machine.TryTransitionTo(SystemState.Discovery, "start");
        machine.TryTransitionTo(SystemState.Diagnostic, "assess");
        machine.TryTransitionTo(SystemState.PlanGenerated, "plan");

        Assert.True(File.Exists(first.Location));

        // A crash while the fourth entry was being written leaves half a line behind. The journal must
        // stay usable - that is the whole point of a line based format.
        File.AppendAllText(first.Location, "{ \"from\": \"PlanGenerated\", \"to\": \"Execut");

        var second = new WindowsMaintenanceCenter.Infrastructure.Persistence.FileStateJournal(paths);
        var entries = second.Read();

        Assert.Equal(3, entries.Count);
        Assert.Equal(SystemState.PlanGenerated, entries[^1].To);
        Assert.Equal("op-1", entries[^1].OperationId);
    }
}
