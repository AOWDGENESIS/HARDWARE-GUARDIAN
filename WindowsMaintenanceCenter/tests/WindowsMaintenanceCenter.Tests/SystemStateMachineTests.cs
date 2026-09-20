using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Services;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The state machine (spec section 20). Illegal transitions must be refused instead of silently
/// producing a plausible looking state.
/// </summary>
public sealed class SystemStateMachineTests
{
    [Fact]
    public void Starts_idle()
    {
        var machine = new SystemStateMachine(new FakeClock());
        Assert.Equal(SystemState.Idle, machine.Current);
    }

    [Fact]
    public void Allows_the_documented_scan_flow()
    {
        var machine = new SystemStateMachine(new FakeClock());
        Assert.True(machine.TryTransitionTo(SystemState.Scanning, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.Analyzing, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.WaitingForApproval, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.Executing, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.Verifying, "test"));
        Assert.True(machine.TryTransitionTo(SystemState.Success, "test"));
    }

    [Fact]
    public void Refuses_a_transition_that_would_skip_the_verification()
    {
        var machine = new SystemStateMachine(new FakeClock());
        machine.TryTransitionTo(SystemState.Scanning, "test");

        // Scanning -> Executing is not in the transition table.
        Assert.False(machine.CanTransitionTo(SystemState.Executing));
        Assert.False(machine.TryTransitionTo(SystemState.Executing, "test"));
        Assert.Equal(SystemState.Scanning, machine.Current);
    }

    [Fact]
    public void Throws_when_a_transition_is_forced()
    {
        var machine = new SystemStateMachine(new FakeClock());
        machine.TryTransitionTo(SystemState.Scanning, "test");
        Assert.Throws<InvalidStateTransitionException>(() => machine.TransitionTo(SystemState.Executing, "forced"));
    }

    [Fact]
    public void Same_state_is_not_a_transition()
    {
        var machine = new SystemStateMachine(new FakeClock());
        Assert.False(machine.TryTransitionTo(SystemState.Idle, "noop"));
    }

    [Fact]
    public void History_records_the_reason()
    {
        var machine = new SystemStateMachine(new FakeClock());
        machine.TryTransitionTo(SystemState.Scanning, "user requested scan");

        var entry = Assert.Single(machine.History);
        Assert.Equal(SystemState.Idle, entry.Previous);
        Assert.Equal(SystemState.Scanning, entry.Current);
        Assert.Equal("user requested scan", entry.Reason);
    }
}
