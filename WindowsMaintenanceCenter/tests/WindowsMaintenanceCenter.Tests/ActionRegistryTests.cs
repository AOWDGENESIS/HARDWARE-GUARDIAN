using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The action registry (WMC specification, chapters 38 and 32) and the inputs that have to be
/// handled safely (chapters 78 and 79).
///
/// These tests are the executable form of the security requirements M32-SEC-001 ... 004. They do not
/// need Windows and they do not start a process: what is tested is the decision whether something
/// may be started at all, and with which arguments.
/// </summary>
public sealed class ActionRegistryTests
{
    private static readonly RegisteredAction CleanCache = new()
    {
        Id = "Cleanup.WindowsUpdateCache",
        Description = LocalizedText.Of("Action_Unknown_Description"),
        Risk = RiskLevel.Medium,
        RequiresAdmin = true,
        SelfValidating = true,
        Executable = "dism.exe",
        Timeout = TimeSpan.FromMinutes(10),
        Arguments = new[]
        {
            new ActionArgumentSpec { Name = "Path", Kind = ActionArgumentKind.Path, Required = true },
        },
        ArgumentTemplate = new[] { "/Online", "/Cleanup-Image", "/StartComponentCleanup", "/Image:{Path}" },
    };

    private static ActionRegistry Registry(params RegisteredAction[] extra) =>
        new(new[] { CleanCache }.Concat(extra));

    [Fact]
    public void An_unregistered_action_is_refused()
    {
        var result = Registry().Validate(new ActionRequest { ActionId = "Cleanup.EverythingElse" });

        Assert.False(result.IsValid);
        Assert.Equal(BlockReasons.ActionNotRegistered, result.ReasonCode);
        Assert.Contains(result.Errors, error => error.Key == "Action_Blocked_NotRegistered");
    }

    [Fact]
    public void A_registered_but_not_allowed_action_is_refused()
    {
        // Chapter 96: a function that is described but not implemented must be visible as such, and
        // must not be performable by accident.
        var registry = Registry(new RegisteredAction
        {
            Id = "Firmware.Flash",
            Allowed = false,
            Executable = "fwupd.exe",
        });

        var result = registry.Validate(new ActionRequest { ActionId = "Firmware.Flash" });

        Assert.False(result.IsValid);
        Assert.Equal(BlockReasons.ActionNotAllowed, result.ReasonCode);
    }

    [Fact]
    public void An_action_without_a_registered_program_is_refused()
    {
        var registry = Registry(new RegisteredAction { Id = "System.Nothing" });

        var result = registry.Validate(new ActionRequest { ActionId = "System.Nothing" });

        Assert.False(result.IsValid);
        Assert.Equal(BlockReasons.InvalidRequest, result.ReasonCode);
    }

    [Fact]
    public void A_missing_required_argument_is_refused()
    {
        var result = Registry().Validate(new ActionRequest { ActionId = CleanCache.Id });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Key == "Action_Missing_Argument" && error.Argument == "Path");
    }

    [Fact]
    public void An_unknown_argument_is_refused()
    {
        var request = new ActionRequest
        {
            ActionId = CleanCache.Id,
            Arguments = new Dictionary<string, string> { ["Path"] = "C:\\Windows", ["Extra"] = "1" },
        };

        var result = Registry().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Key == "Action_Unknown_Argument" && error.Argument == "Extra");
    }

    [Fact]
    public void A_valid_request_resolves_the_command_line()
    {
        var request = new ActionRequest
        {
            ActionId = CleanCache.Id,
            Arguments = new Dictionary<string, string> { ["Path"] = "C:\\Windows\\Temp" },
        };

        var result = Registry().Validate(request);

        Assert.True(result.IsValid);
        Assert.Equal(CleanCache.Id, result.Action!.Id);
        Assert.Equal(new[] { "/Online", "/Cleanup-Image", "/StartComponentCleanup", "/Image:C:\\Windows\\Temp" }, result.ResolvedArguments);
    }

    // ---------------------------------------------------------------------------------------------
    // Chapter 78: these inputs must not lead to a second action
    // ---------------------------------------------------------------------------------------------
    [Theory]
    [InlineData("\"")]
    [InlineData("'")]
    [InlineData(";")]
    [InlineData("&")]
    [InlineData("|")]
    [InlineData(">")]
    [InlineData("<")]
    [InlineData("$")]
    [InlineData("`")]
    [InlineData("()")]
    [InlineData("{}")]
    [InlineData("%PATH%")]
    [InlineData("C:\\Temp; Remove-Item -Recurse -Force C:\\")]
    [InlineData("C:\\Temp | Stop-Service wuauserv")]
    [InlineData("C:\\Temp & del *.*")]
    [InlineData("$(Get-Process)")]
    public void A_value_with_shell_metacharacters_is_refused(string value)
    {
        var request = new ActionRequest
        {
            ActionId = CleanCache.Id,
            Arguments = new Dictionary<string, string> { ["Path"] = value },
        };

        var result = Registry().Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal(BlockReasons.ActionArgumentInvalid, result.ReasonCode);
    }

    [Fact]
    public void A_control_character_inside_a_value_is_refused()
    {
        var request = new ActionRequest
        {
            ActionId = CleanCache.Id,
            Arguments = new Dictionary<string, string> { ["Path"] = "C:\\Temp\nRemove-Item C:\\" },
        };

        Assert.False(Registry().Validate(request).IsValid);
    }

    // ---------------------------------------------------------------------------------------------
    // Chapter 79: path policy
    // ---------------------------------------------------------------------------------------------
    [Theory]
    [InlineData("..\\..\\Windows")]
    [InlineData("C:\\Temp\\..\\..\\Users")]
    [InlineData("C:/Temp/../Windows")]
    [InlineData("..")]
    public void A_path_that_leaves_its_area_is_refused(string value)
    {
        var request = new ActionRequest
        {
            ActionId = CleanCache.Id,
            Arguments = new Dictionary<string, string> { ["Path"] = value },
        };

        var result = Registry().Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal(BlockReasons.ActionPathNotAllowed, result.ReasonCode);
        Assert.Contains(result.Errors, error => error.Key == "Action_Path_Traversal");
    }

    [Theory]
    [InlineData("\\\\server\\share\\temp")]
    [InlineData("\\\\?\\C:\\Windows")]
    [InlineData("\\\\.\\PhysicalDrive0")]
    public void A_network_or_device_path_is_refused(string value)
    {
        var request = new ActionRequest
        {
            ActionId = CleanCache.Id,
            Arguments = new Dictionary<string, string> { ["Path"] = value },
        };

        var result = Registry().Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal(BlockReasons.ActionPathNotAllowed, result.ReasonCode);
        Assert.Contains(result.Errors, error => error.Key == "Action_Path_UncOrDevice");
    }

    [Fact]
    public void An_ordinary_windows_path_is_accepted()
    {
        var request = new ActionRequest
        {
            ActionId = CleanCache.Id,
            Arguments = new Dictionary<string, string> { ["Path"] = "C:\\Windows\\SoftwareDistribution\\Download" },
        };

        Assert.True(Registry().Validate(request).IsValid);
    }

    // ---------------------------------------------------------------------------------------------
    // Declared shapes
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void A_declared_number_only_accepts_numbers()
    {
        var registry = Registry(new RegisteredAction
        {
            Id = "System.Wait",
            Executable = "cmd.exe",
            Arguments = new[] { new ActionArgumentSpec { Name = "Seconds", Kind = ActionArgumentKind.Number, Required = true } },
            ArgumentTemplate = new[] { "/c", "timeout", "{Seconds}" },
        });

        Assert.False(registry.Validate(new ActionRequest
        {
            ActionId = "System.Wait",
            Arguments = new Dictionary<string, string> { ["Seconds"] = "60; shutdown /r" },
        }).IsValid);

        Assert.True(registry.Validate(new ActionRequest
        {
            ActionId = "System.Wait",
            Arguments = new Dictionary<string, string> { ["Seconds"] = "60" },
        }).IsValid);
    }

    [Fact]
    public void A_declared_choice_only_accepts_the_declared_values()
    {
        var registry = Registry(new RegisteredAction
        {
            Id = "System.SfcCheck",
            Executable = "sfc.exe",
            Arguments = new[] { new ActionArgumentSpec { Name = "Mode", Kind = ActionArgumentKind.Choice, AllowedValues = new[] { "/verifyonly" }, Required = true } },
            ArgumentTemplate = new[] { "{Mode}" },
        });

        Assert.True(registry.Validate(new ActionRequest
        {
            ActionId = "System.SfcCheck",
            Arguments = new Dictionary<string, string> { ["Mode"] = "/verifyonly" },
        }).IsValid);

        Assert.False(registry.Validate(new ActionRequest
        {
            ActionId = "System.SfcCheck",
            Arguments = new Dictionary<string, string> { ["Mode"] = "/scannow" },
        }).IsValid);
    }

    [Fact]
    public void A_value_that_is_longer_than_declared_is_refused()
    {
        var request = new ActionRequest
        {
            ActionId = CleanCache.Id,
            Arguments = new Dictionary<string, string> { ["Path"] = "C:\\" + new string('a', 300) },
        };

        var result = Registry().Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Key == "Action_Argument_TooLong");
    }

    [Fact]
    public void The_same_action_cannot_be_registered_twice()
    {
        var registry = Registry();

        Assert.Throws<InvalidOperationException>(() => registry.Register(CleanCache));
    }

    [Fact]
    public void Every_error_of_a_request_is_reported_at_once()
    {
        var request = new ActionRequest
        {
            ActionId = CleanCache.Id,
            Arguments = new Dictionary<string, string> { ["Unbekannt"] = "1" },
        };

        var result = Registry().Validate(request);

        // Fehlendes Pflichtargument und unbekanntes Argument: beides wird gemeldet, damit die
        // Oberfläche den ganzen Aufruf auf einmal korrigieren kann.
        Assert.Equal(2, result.Errors.Count);
    }
}
