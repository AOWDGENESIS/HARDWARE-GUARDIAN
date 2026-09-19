using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Services;
using Xunit;

namespace HardwareGuardian.Tests;

/// <summary>
/// The path guard is the last gate before a delete (spec sections 77 and 80). These tests pin down
/// the fail-closed behaviour: no allow list, no deletion; a root itself is never deleted.
/// </summary>
public sealed class PathGuardTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "hg-pathguard-tests");
    private static readonly string RootTwo = Path.Combine(Path.GetTempPath(), "hg-pathguard-other");

    private readonly PathGuard _guard = new();

    [Fact]
    public void Empty_path_is_refused()
    {
        var decision = _guard.Evaluate("   ", PathGuardIntent.Delete, new[] { Root });
        Assert.False(decision.Allowed);
        Assert.Equal("PathGuard_Reason_Empty", decision.ReasonCode);
    }

    [Fact]
    public void Missing_allow_list_refuses_everything()
    {
        var decision = _guard.Evaluate(Path.Combine(Root, "file.tmp"), PathGuardIntent.Delete, Array.Empty<string>());
        Assert.False(decision.Allowed);
        Assert.Equal("PathGuard_Reason_NoAllowedRoot", decision.ReasonCode);
    }

    [Fact]
    public void Path_outside_the_allow_list_is_refused()
    {
        var decision = _guard.Evaluate(Path.Combine(RootTwo, "file.tmp"), PathGuardIntent.Delete, new[] { Root });
        Assert.False(decision.Allowed);
        Assert.Equal("PathGuard_Reason_OutsideAllowedRoots", decision.ReasonCode);
    }

    [Fact]
    public void Path_inside_the_allow_list_is_allowed()
    {
        var decision = _guard.Evaluate(Path.Combine(Root, "sub", "file.tmp"), PathGuardIntent.Delete, new[] { Root });
        Assert.True(decision.Allowed);
        Assert.Null(decision.ReasonCode);
    }

    [Fact]
    public void The_allow_list_root_itself_is_never_deleted()
    {
        var decision = _guard.Evaluate(Root, PathGuardIntent.Delete, new[] { Root });
        Assert.False(decision.Allowed);
        Assert.Equal("PathGuard_Reason_IsAllowedRoot", decision.ReasonCode);
    }

    [Fact]
    public void Drive_root_is_refused()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var decision = _guard.Evaluate(root, PathGuardIntent.Delete, new[] { Root });
        Assert.False(decision.Allowed);
        Assert.Equal("PathGuard_Reason_Invalid", decision.ReasonCode);
    }

    [Theory]
    [InlineData("/tmp/root", "/tmp/root", false)]
    [InlineData("/tmp/root", "/tmp/root/sub", true)]
    [InlineData("/tmp/root", "/tmp/rootsub", false)]
    public void IsStrictlyInside_never_treats_a_prefix_as_containment(string root, string candidate, bool expected) =>
        Assert.Equal(expected, PathGuard.IsStrictlyInside(candidate, root));

    [Fact]
    public void Invalid_allow_list_entries_are_ignored_and_documented()
    {
        var decision = _guard.Evaluate(Path.Combine(Root, "file.tmp"), PathGuardIntent.Delete, new[] { "   ", Root });
        Assert.True(decision.Allowed);
        Assert.Contains(decision.Notes, note => note.Contains("ignored invalid allow list entry", StringComparison.OrdinalIgnoreCase));
    }
}
