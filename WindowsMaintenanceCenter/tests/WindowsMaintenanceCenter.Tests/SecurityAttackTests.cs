using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The attack cases of the security matrix (spec chapters 78 to 82, module M44) - here as far as they
/// are decided in code and therefore provable without a target machine.
///
/// The matrix names fourteen attacks. Ten of them are answered by the registry, the path guard, the
/// source verifier, the admin worker and the rollback service; the journal (SEC-11, SEC-12, SEC-13)
/// is covered by <see cref="StateJournalTests"/> and <see cref="RecoveryEngineTests"/>, the report
/// (SEC-14) by <see cref="ReportGeneratorTests"/>. This file adds the three injection attacks that
/// had no case of their own: a command, a path and an argument that try to make the application run
/// something it was not asked to run.
///
/// Every test here hands the attack to the real component, never to a stub, and checks that nothing
/// was accepted - "it did not run" is worth nothing if the validation said yes.
/// </summary>
public sealed class SecurityAttackTests
{
    private static readonly RegisteredAction VolumeAction = new()
    {
        Id = "Test.VolumeScan",
        Executable = "chkdsk.exe",
        SelfValidating = true,
        ArgumentTemplate = new[] { "{Volume}", "/scan" },
        Arguments = new[]
        {
            new ActionArgumentSpec
            {
                Name = "Volume",
                Kind = ActionArgumentKind.Choice,
                Required = true,
                AllowedValues = new[] { "C", "D" },
            },
        },
    };

    private static readonly RegisteredAction PathAction = new()
    {
        Id = "Test.PathCleanup",
        Executable = "cleanmgr.exe",
        ArgumentTemplate = new[] { "/d", "{Path}" },
        Arguments = new[]
        {
            new ActionArgumentSpec
            {
                Name = "Path",
                Kind = ActionArgumentKind.Path,
                Required = true,
                MaxLength = 260,
            },
        },
    };

    /// <summary>SEC-01: a command smuggled into an argument is refused, and no command line is built.</summary>
    [Theory]
    [InlineData("C & del /f /q C:\\Windows\\System32")]
    [InlineData("C | powershell -enc SQBFAFgA")]
    [InlineData("C; cmd /c whoami")]
    [InlineData("C > C:\\Windows\\Temp\\x.txt")]
    [InlineData("$(Get-Process)")]
    [InlineData("`whoami`")]
    public void A_command_smuggled_into_an_argument_is_refused(string attack)
    {
        var registry = new ActionRegistry(new[] { VolumeAction, PathAction });

        var result = registry.Validate(new ActionRequest
        {
            ActionId = VolumeAction.Id,
            Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["Volume"] = attack },
        });

        Assert.False(result.IsValid);
        Assert.Empty(result.ResolvedArguments);
        Assert.NotEqual(string.Empty, result.ReasonCode);
    }

    /// <summary>SEC-02: a path that leaves its area is refused, however it is written.</summary>
    [Theory]
    [InlineData("..\\..\\Windows\\System32\\config\\SAM")]
    [InlineData("C:\\Temp\\..\\..\\Windows")]
    [InlineData("\\\\server\\share\\payload")]
    [InlineData("\\\\?\\C:\\Windows")]
    [InlineData("C:\\Temp\\file.exe & del /f /q C:\\")]
    public void A_path_that_leaves_its_area_is_refused(string attack)
    {
        var registry = new ActionRegistry(new[] { VolumeAction, PathAction });

        var result = registry.Validate(new ActionRequest
        {
            ActionId = PathAction.Id,
            Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["Path"] = attack },
        });

        Assert.False(result.IsValid);
        Assert.Empty(result.ResolvedArguments);

        // A path that is refused because of the path policy says so; a metacharacter is a different
        // refusal but equally final.
        Assert.Contains(
            result.Errors,
            error => error.Key is "Action_Path_Traversal" or "Action_Path_UncOrDevice" or "Action_Path_Metacharacter");
    }

    /// <summary>SEC-03: an argument nobody declared is not passed through.</summary>
    [Fact]
    public void An_undeclared_argument_is_refused()
    {
        var registry = new ActionRegistry(new[] { VolumeAction });

        var result = registry.Validate(new ActionRequest
        {
            ActionId = VolumeAction.Id,
            Arguments = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Volume"] = "C",
                ["/f"] = "",
                ["Extra"] = "/r",
            },
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Key == "Action_Unknown_Argument");
        Assert.Empty(result.ResolvedArguments);
    }

    /// <summary>
    /// SEC-01 to SEC-03: an ordinary call still passes. A guard that refuses everything is not a guard,
    /// it is a wall - and the delivery would be unusable.
    /// </summary>
    [Fact]
    public void The_declared_call_still_passes()
    {
        var registry = new ActionRegistry(new[] { VolumeAction, PathAction });

        var volume = registry.Validate(new ActionRequest
        {
            ActionId = VolumeAction.Id,
            Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["Volume"] = "C" },
        });

        Assert.True(volume.IsValid);
        Assert.Equal(new[] { "C", "/scan" }, volume.ResolvedArguments.ToArray());

        var path = registry.Validate(new ActionRequest
        {
            ActionId = PathAction.Id,
            Arguments = new Dictionary<string, string>(StringComparer.Ordinal) { ["Path"] = "C:\\Temp\\WMC" },
        });

        Assert.True(path.IsValid);
        Assert.Equal(new[] { "/d", "C:\\Temp\\WMC" }, path.ResolvedArguments.ToArray());
    }
}
