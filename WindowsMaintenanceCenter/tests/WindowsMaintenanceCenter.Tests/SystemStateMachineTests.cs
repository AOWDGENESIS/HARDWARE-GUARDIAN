using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Services;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The state machine (WMC specification, chapters 40 and 101). Two properties are pinned here:
///
/// 1. The allowed states are exactly the fourteen the specification names. A job that produced no
///    definitive answer ends in BLOCKED, and "warnings" is not a state at all - it is part of the
///    result. A run must therefore never move from reading straight into an execution: that would
///    skip plan, approval and validation (M34-S-001).
/// 2. RECOVERING exists, is reachable from the states a crash can interrupt, and leads back into a
///    controlled path - never directly into an execution (M34-R-001).
/// </summary>
public sealed class SystemStateMachineTests
{
    [Fact]
    public void Starts_initializing()
    {
        var machine = new SystemStateMachine(new FakeClock());

        Assert.Equal(SystemState.Initializing, machine.Current);
    }

    [Fact]
    public void Allows_the_documented_maintenance_flow()
    {
        // INITIALIZING -> DISCOVERY -> DIAGNOSTIC -> PLAN_GENERATED -> AWAITING_APPROVAL -> BACKUP
        // -> EXECUTING -> VALIDATING -> SUCCESS
        var machine = new SystemStateMachine(new FakeClock());

        Assert.True(machine.TryTransitionTo(SystemState.Discovery, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.Diagnostic, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.PlanGenerated, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.AwaitingApproval, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.Backup, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.Executing, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.Validating, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.Success, "test"));
    }

    [Fact]
    public void Refuses_a_transition_that_would_skip_the_verification()
    {
        var machine = new SystemStateMachine(new FakeClock());
        machine.TryTransitionTo(SystemState.Discovery, "test");

        // Discovery -> Executing is not in the transition table.
        Assert.False(machine.CanTransitionTo(SystemState.Executing));
        Assert.False(machine.TryTransitionTo(SystemState.Executing, "test"));
        Assert.Equal(SystemState.Discovery, machine.Current);
    }

    [Fact]
    public void Refuses_a_transition_from_a_valued_state_straight_into_success()
    {
        var machine = new SystemStateMachine(new FakeClock());
        machine.TryTransitionTo(SystemState.Discovery, "test");
        machine.TryTransitionTo(SystemState.Diagnostic, "test");
        machine.TryTransitionTo(SystemState.AwaitingApproval, "test");
        machine.TryTransitionTo(SystemState.Executing, "test");

        // Executing -> Success would claim a result that was never validated (chapter 86).
        Assert.False(machine.CanTransitionTo(SystemState.Success));
        Assert.True(machine.TryTransitionTo(SystemState.Validating, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.Success, "test"));
    }

    [Fact]
    public void Throws_when_a_transition_is_forced()
    {
        var machine = new SystemStateMachine(new FakeClock());
        machine.TryTransitionTo(SystemState.Discovery, "test");

        Assert.Throws<InvalidStateTransitionException>(() => machine.TransitionTo(SystemState.Executing, "forced"));
    }

    [Fact]
    public void Same_state_is_not_a_transition()
    {
        var machine = new SystemStateMachine(new FakeClock());

        Assert.False(machine.TryTransitionTo(SystemState.Initializing, "noop"));
    }

    [Fact]
    public void An_interrupted_job_can_be_recovered_and_never_jumps_into_an_execution()
    {
        var machine = new SystemStateMachine(new FakeClock());
        machine.TryTransitionTo(SystemState.Discovery, "test");
        machine.TryTransitionTo(SystemState.Diagnostic, "test");
        machine.TryTransitionTo(SystemState.Executing, "test");

        // The process died here; after the restart the machine recognises the interruption.
        Assert.True(machine.TryTransitionTo(SystemState.Recovering, "interrupted job detected"));

        Assert.False(machine.CanTransitionTo(SystemState.Executing));
        Assert.True(machine.TryTransitionTo(SystemState.Rollback, "user approved the rollback"));
        Assert.True(machine.TryTransitionTo(SystemState.Validating, "rollback finished"));
        Assert.True(machine.TryTransitionTo(SystemState.Success, "state verified"));
    }

    [Fact]
    public void The_state_list_is_exactly_the_list_of_chapter_40()
    {
        // The list of chapter 40 is the contract: an extra state that the specification does not
        // name, or a missing one, is a deviation.
        var documented = new[]
        {
            SystemState.Initializing, SystemState.Discovery, SystemState.Diagnostic, SystemState.PlanGenerated,
            SystemState.AwaitingApproval, SystemState.Backup, SystemState.Executing, SystemState.Validating,
            SystemState.Success, SystemState.Error, SystemState.Rollback, SystemState.Recovering,
            SystemState.Blocked, SystemState.Cancelled,
        };

        Assert.Equal(documented.OrderBy(state => state).ToList(), Enum.GetValues<SystemState>().OrderBy(state => state).ToList());
    }

    [Fact]
    public void History_records_the_reason()
    {
        var machine = new SystemStateMachine(new FakeClock());
        machine.TryTransitionTo(SystemState.Discovery, "user requested scan");

        var entry = Assert.Single(machine.History);
        Assert.Equal(SystemState.Initializing, entry.Previous);
        Assert.Equal(SystemState.Discovery, entry.Current);
        Assert.Equal("user requested scan", entry.Reason);
    }
}
