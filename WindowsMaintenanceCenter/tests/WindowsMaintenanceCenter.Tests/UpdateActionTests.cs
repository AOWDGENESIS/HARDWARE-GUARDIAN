using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Infrastructure.Platform;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The download/install path of rule 90 (UPDATE-F-005 … F-008).
///
/// Two properties are pinned here, and both of them are safety properties rather than features:
///
/// 1. An update is identified by its <em>position</em> in the offer list. No title, no URL and no
///    knowledge base number ever becomes part of a command, so there is no text that could be turned
///    into another command (rule 115, EXEC-S-002). A value with separators is refused by the
///    parameter whitelist before the script exists.
/// 2. A result the agent did not document is never a success. Rule 90 forbids reporting a failed
///    update as installed, and an unknown code is exactly the case where a guess would be invisible.
///
/// The script itself talks to the Windows Update agent and can therefore only run on Windows with
/// real updates available; what is tested here is the part that decides.
/// </summary>
public sealed class UpdateActionTests
{
    [Theory]
    [InlineData(0, WindowsUpdateResultVerdict.NotStarted)]
    [InlineData(1, WindowsUpdateResultVerdict.InProgress)]
    [InlineData(2, WindowsUpdateResultVerdict.Succeeded)]
    [InlineData(3, WindowsUpdateResultVerdict.SucceededWithErrors)]
    [InlineData(4, WindowsUpdateResultVerdict.Failed)]
    [InlineData(5, WindowsUpdateResultVerdict.Aborted)]
    public void The_documented_result_codes_keep_their_documented_meaning(int code, WindowsUpdateResultVerdict expected)
    {
        Assert.Equal(expected, WindowsUpdateResultCodes.Interpret(code));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(99)]
    public void A_code_that_nobody_documented_is_unknown_and_never_a_success(int code)
    {
        var verdict = WindowsUpdateResultCodes.Interpret(code);

        Assert.Equal(WindowsUpdateResultVerdict.Unknown, verdict);
        Assert.False(WindowsUpdateResultCodes.IsSuccess(verdict));
    }

    [Fact]
    public void A_run_that_never_finished_does_not_count_as_success_either()
    {
        Assert.False(WindowsUpdateResultCodes.IsSuccess(WindowsUpdateResultVerdict.NotStarted));
        Assert.False(WindowsUpdateResultCodes.IsSuccess(WindowsUpdateResultVerdict.InProgress));
        Assert.True(WindowsUpdateResultCodes.IsIncomplete(WindowsUpdateResultVerdict.NotStarted));
        Assert.True(WindowsUpdateResultCodes.IsIncomplete(WindowsUpdateResultVerdict.InProgress));
    }

    [Fact]
    public void A_partial_success_stays_visible_as_a_partial_success()
    {
        var verdict = WindowsUpdateResultCodes.Interpret(3);

        Assert.Equal(WindowsUpdateResultVerdict.SucceededWithErrors, verdict);
        Assert.True(WindowsUpdateResultCodes.IsSuccess(verdict));
        Assert.NotEqual(WindowsUpdateResultVerdict.Succeeded, verdict);
    }

    // ---------------------------------------------------------------------------------------------
    // Only a position reaches the command line, and only in the allow listed template
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void The_install_script_contains_the_position_and_nothing_else_from_the_caller()
    {
        var script = PowerShellCommandCatalog.ScriptFor(
            PowerShellCommandCatalog.WindowsUpdateInstall,
            new Dictionary<string, string> { ["INDEX"] = "3" });

        Assert.Contains("[int]3", script, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Update.Session", script, StringComparison.Ordinal);
        Assert.Contains("CreateUpdateInstaller", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_download_script_contains_the_position_and_nothing_else_from_the_caller()
    {
        var script = PowerShellCommandCatalog.ScriptFor(
            PowerShellCommandCatalog.WindowsUpdateDownload,
            new Dictionary<string, string> { ["INDEX"] = "0" });

        Assert.Contains("[int]0", script, StringComparison.Ordinal);
        Assert.Contains("CreateUpdateDownloader", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("3; Remove-Item -Recurse -Force C:\\Temp")]
    [InlineData("3 | Stop-Service wuauserv")]
    [InlineData("$(Get-Process)")]
    [InlineData("3' ; whoami")]
    [InlineData("3\"")]
    public void A_position_that_carries_anything_but_a_number_is_refused_before_a_script_exists(string value)
    {
        var parameters = new Dictionary<string, string> { ["INDEX"] = value };

        Assert.Throws<ArgumentException>(() =>
            PowerShellCommandCatalog.ScriptFor(PowerShellCommandCatalog.WindowsUpdateInstall, parameters));
    }

    [Fact]
    public void A_template_that_is_not_on_the_allow_list_is_refused()
    {
        // The catalogue is the only source of scripts; an unknown id must not fall back to anything.
        Assert.Throws<ArgumentException>(() =>
            PowerShellCommandCatalog.ScriptFor("windowsupdate.install.anything", new Dictionary<string, string> { ["INDEX"] = "1" }));
    }

    [Fact]
    public void Both_update_templates_are_part_of_the_allow_list()
    {
        Assert.Contains(PowerShellCommandCatalog.WindowsUpdateDownload, PowerShellCommandCatalog.TemplateIds);
        Assert.Contains(PowerShellCommandCatalog.WindowsUpdateInstall, PowerShellCommandCatalog.TemplateIds);
    }
}
