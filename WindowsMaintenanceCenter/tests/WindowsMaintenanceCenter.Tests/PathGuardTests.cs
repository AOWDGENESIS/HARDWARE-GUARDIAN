using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Services;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

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
    public void A_location_that_resolves_outside_the_allow_list_is_refused()
    {
        // The path as written is inside the allowed root, the location it points to is not. A
        // junction or symlink inside a cache folder is exactly how a delete would leave the
        // allowed root without the text form showing it.
        var inside = Path.Combine(Root, "cache", "escape", "file.tmp");
        var outside = Path.Combine(RootTwo, "windows", "file.tmp");
        var guard = new PathGuard(_ => outside);

        var decision = guard.Evaluate(inside, PathGuardIntent.Delete, new[] { Root });

        Assert.False(decision.Allowed);
        Assert.Equal("PathGuard_Reason_OutsideAllowedRoots", decision.ReasonCode);
        Assert.Contains(decision.Notes, note => note.Contains("resolves to", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_location_that_still_resolves_inside_the_root_is_allowed()
    {
        var inside = Path.Combine(Root, "cache", "link", "file.tmp");
        var guard = new PathGuard(_ => Path.Combine(Root, "cache", "real", "file.tmp"));

        var decision = guard.Evaluate(inside, PathGuardIntent.Delete, new[] { Root });

        Assert.True(decision.Allowed);
    }

    [Theory]
    [InlineData("file.tmp ")]
    [InlineData("cache.")]
    public void A_segment_that_ends_with_a_dot_or_a_space_is_refused(string segment)
    {
        // Windows strips trailing dots and spaces when the path is used, so the string that was
        // checked and the file that is deleted would not be the same one.
        var decision = _guard.Evaluate(Path.Combine(Root, segment, "file.tmp"), PathGuardIntent.Delete, new[] { Root });

        Assert.False(decision.Allowed);
        Assert.Equal("PathGuard_Reason_AmbiguousSegment", decision.ReasonCode);
    }

    [Fact]
    public void An_ordinary_path_has_no_ambiguous_segment()
    {
        Assert.False(PathGuard.HasAmbiguousSegment(Path.Combine(Root, "cache", "file.tmp")));
        Assert.False(PathGuard.HasAmbiguousSegment(Path.Combine(Root, "..", "cache")));
    }

    [Fact]
    public void Invalid_allow_list_entries_are_ignored_and_documented()
    {
        var decision = _guard.Evaluate(Path.Combine(Root, "file.tmp"), PathGuardIntent.Delete, new[] { "   ", Root });
        Assert.True(decision.Allowed);
        Assert.Contains(decision.Notes, note => note.Contains("ignored invalid allow list entry", StringComparison.OrdinalIgnoreCase));
    }
}
