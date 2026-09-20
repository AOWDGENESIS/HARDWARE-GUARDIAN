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

        // DISCOVERY -> EXECUTING is not in the table: a read must never run straight into an execution,
        // because that would skip plan, approval and validation (M34-S-001). The refused attempt must
        // not become a stored fact either.
        machine.TryTransitionTo(SystemState.Discovery, "start");
        var afterDiscovery = journal.Read().Count;

        Assert.False(machine.TryTransitionTo(SystemState.Executing, "not allowed from a read"));

        Assert.Equal(afterDiscovery, journal.Read().Count);
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
    public void The_memory_journal_chains_its_entries()
    {
        var journal = new InMemoryStateJournal();
        var machine = new SystemStateMachine(new FakeClock(), events: null, logger: null, journal: journal);

        machine.TryTransitionTo(SystemState.Discovery, "a");
        machine.TryTransitionTo(SystemState.Diagnostic, "b");

        var entries = journal.Read();
        var verification = journal.Verify();

        Assert.True(verification.IsIntact);
        Assert.Equal(2, verification.Checked);
        Assert.Equal(1, entries[0].Sequence);
        Assert.Null(entries[0].PreviousHash);
        Assert.False(string.IsNullOrWhiteSpace(entries[0].Hash));
        Assert.Equal(entries[0].Hash, entries[1].PreviousHash);
        Assert.Equal(2, entries[1].Sequence);
    }

    /// <summary>
    /// SEC-12: a line taken out of the journal has to be recognisable. Without the chain this line
    /// would simply be missing and the recovery would work with fewer entries - and nobody would
    /// know that the record was changed.
    /// </summary>
    [Fact]
    public void A_deleted_line_breaks_the_chain()
    {
        using var paths = new TempPathProvider();
        var journal = new WindowsMaintenanceCenter.Infrastructure.Persistence.FileStateJournal(paths);
        var machine = new SystemStateMachine(new FakeClock(), events: null, logger: null, journal: journal);

        machine.TryTransitionTo(SystemState.Discovery, "a");
        machine.TryTransitionTo(SystemState.Diagnostic, "b");
        machine.TryTransitionTo(SystemState.PlanGenerated, "c");

        Assert.True(journal.Verify().IsIntact);

        var lines = File.ReadAllLines(journal.Location);
        File.WriteAllLines(journal.Location, lines.Where((_, index) => index != 1));

        var reloaded = new WindowsMaintenanceCenter.Infrastructure.Persistence.FileStateJournal(paths);
        var verification = reloaded.Verify();

        Assert.False(verification.IsIntact);
        Assert.Equal("StateJournal_Chain_SequenceGap", verification.Summary.Key);
        Assert.Equal(1, verification.FirstBrokenIndex);

        // The run continues: the entries that are there stay readable, only the verdict changes.
        Assert.Equal(2, reloaded.Read().Count);
    }

    /// <summary>SEC-12: a changed entry has to be recognisable.</summary>
    [Fact]
    public void A_changed_entry_is_recognised()
    {
        using var paths = new TempPathProvider();
        var journal = new WindowsMaintenanceCenter.Infrastructure.Persistence.FileStateJournal(paths);
        var machine = new SystemStateMachine(new FakeClock(), events: null, logger: null, journal: journal);

        machine.TryTransitionTo(SystemState.Discovery, "a");
        machine.TryTransitionTo(SystemState.Diagnostic, "b");

        var lines = File.ReadAllLines(journal.Location);
        lines[0] = lines[0].Replace("\"Discovery\"", "\"Executing\"", StringComparison.Ordinal);
        File.WriteAllLines(journal.Location, lines);

        var verification = new WindowsMaintenanceCenter.Infrastructure.Persistence.FileStateJournal(paths).Verify();

        Assert.False(verification.IsIntact);
        Assert.Equal("StateJournal_Chain_HashMismatch", verification.Summary.Key);
        Assert.Equal(0, verification.FirstBrokenIndex);
    }

    /// <summary>SEC-13/REC-03: a half written last line is a crash, not manipulation - it cannot break the chain.</summary>
    [Fact]
    public void A_truncated_last_line_leaves_the_chain_intact()
    {
        using var paths = new TempPathProvider();
        var journal = new WindowsMaintenanceCenter.Infrastructure.Persistence.FileStateJournal(paths);
        var machine = new SystemStateMachine(new FakeClock(), events: null, logger: null, journal: journal);

        machine.TryTransitionTo(SystemState.Discovery, "a");
        File.AppendAllText(journal.Location, "{ \"from\": \"Discovery\", \"to\": \"Diagn");

        var verification = new WindowsMaintenanceCenter.Infrastructure.Persistence.FileStateJournal(paths).Verify();

        Assert.True(verification.IsIntact);
        Assert.Equal(1, verification.Checked);
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
