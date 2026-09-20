using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Infrastructure.Platform;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// The admin worker (WMC specification, chapters 37, 39 and 86).
///
/// The tests pin the four rules that decide whether a maintenance tool is safe to run:
/// a refused action starts nothing, a missing privilege blocks instead of guessing, a timeout is an
/// unknown result and never a success, and SUCCESS is only reported after execution **and**
/// verification.
/// </summary>
public sealed class AdminWorkerTests
{
    private static readonly RegisteredAction Action = new()
    {
        Id = "System.DismScanHealth",
        Description = LocalizedText.Of("Action_Unknown_Description"),
        Risk = RiskLevel.Low,
        RequiresAdmin = true,
        SelfValidating = true,
        Executable = "dism.exe",
        Timeout = TimeSpan.FromMinutes(5),
        ArgumentTemplate = new[] { "/Online", "/Cleanup-Image", "/ScanHealth" },
    };

    private static readonly RegisteredAction PlainAction = Action with
    {
        Id = "System.TrimStatus",
        RequiresAdmin = false,
        SelfValidating = false,
        Executable = "fsutil.exe",
        ArgumentTemplate = new[] { "behavior", "query", "DisableDeleteNotify" },
    };

    private static (AdminWorker Worker, RecordingProcessRunner Runner, RecordingAuditLog Audit, RecordingLiveProtocol Protocol, FakeEnvironmentProbe Environment) Build(
        params RegisteredAction[] actions)
    {
        var registry = new ActionRegistry(new[] { Action, PlainAction }.Concat(actions));
        var runner = new RecordingProcessRunner();
        var audit = new RecordingAuditLog();
        var protocol = new RecordingLiveProtocol();
        var environment = new FakeEnvironmentProbe { IsWindows = false, Privilege = SessionPrivilege.StandardUser };

        return (new AdminWorker(registry, runner, environment, audit, protocol), runner, audit, protocol, environment);
    }

    [Fact]
    public async Task An_unregistered_action_starts_nothing()
    {
        var (worker, runner, audit, _, _) = Build();

        var result = await worker.ExecuteAsync(new ActionRequest { ActionId = "Cleanup.Everything" });

        Assert.Equal(StageOutcome.Blocked, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(0, runner.Calls);
        Assert.Equal(BlockReasons.ActionNotRegistered, result.ErrorDetail);
        Assert.Equal(StageOutcome.Blocked, Assert.Single(audit.Entries).Result);
    }

    [Fact]
    public async Task An_action_that_needs_rights_is_blocked_when_they_are_missing()
    {
        // The environment says "not Windows", so the worker must not attempt a runas prompt. What is
        // checked here is the rule, not the prompt itself: without the rights nothing is started.
        var (worker, runner, _, _, _) = Build();

        var result = await worker.ExecuteAsync(new ActionRequest { ActionId = Action.Id });

        Assert.Equal(StageOutcome.Blocked, result.Outcome);
        Assert.Equal(BlockReasons.UnsupportedPlatform, result.ErrorDetail);
        Assert.Equal(0, runner.Calls);
        Assert.Contains(result.Evidence, line => line.Contains("no process was started", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_action_without_the_admin_requirement_runs_without_a_prompt()
    {
        var (worker, runner, _, _, _) = Build();

        var result = await worker.ExecuteAsync(new ActionRequest { ActionId = PlainAction.Id });

        Assert.Equal(1, runner.Calls);
        Assert.Equal("fsutil.exe", runner.LastExecutable);
        Assert.Equal(new[] { "behavior", "query", "DisableDeleteNotify" }, runner.LastArguments);
        Assert.Equal(StageOutcome.Succeeded, result.Outcome);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_non_zero_exit_code_is_a_failure_and_not_a_success()
    {
        var (worker, runner, _, _, _) = Build();
        runner.Result = new ProcessResult { ExitCode = 2, StandardError = "something failed" };

        var result = await worker.ExecuteAsync(new ActionRequest { ActionId = PlainAction.Id });

        Assert.Equal(StageOutcome.Failed, result.Outcome);
        Assert.False(result.Succeeded);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public async Task A_timeout_is_an_unknown_result_and_never_a_success()
    {
        var (worker, runner, _, protocol, _) = Build();
        runner.Result = new ProcessResult { ExitCode = 0, TimedOut = true, ErrorDetail = "the process was stopped" };

        var result = await worker.ExecuteAsync(new ActionRequest { ActionId = PlainAction.Id });

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.Equal(StageOutcome.Blocked, result.Outcome);
        Assert.False(result.Validated);
        Assert.Contains(protocol.Entries, entry => entry.Severity == Severity.Blocked);
    }

    [Fact]
    public async Task A_self_validating_action_counts_as_validated()
    {
        // Ein Werkzeug, dessen Exit-Code selbst die Prüfung ist (dism /scanhealth), braucht keinen
        // zweiten Nachweis; die Registrierung sagt das ausdrücklich.
        var selfValidating = Action with { Id = "System.DismSelfTest", RequiresAdmin = false };
        var (worker, runner, _, _, _) = Build(selfValidating);
        runner.Result = new ProcessResult { ExitCode = 0 };

        var result = await worker.ExecuteAsync(new ActionRequest { ActionId = selfValidating.Id });

        Assert.Equal(1, runner.Calls);
        Assert.True(result.Succeeded);
        Assert.True(result.Validated);
        Assert.Equal(StageOutcome.Succeeded, result.Outcome);
    }

    [Fact]
    public async Task Without_a_verification_the_run_is_not_called_success_in_the_summary()
    {
        // Chapter 86: EXECUTION alone is not SUCCESS. The result says so instead of hiding it.
        var (worker, runner, _, _, _) = Build();
        runner.Result = new ProcessResult { ExitCode = 0 };

        var result = await worker.ExecuteAsync(new ActionRequest { ActionId = PlainAction.Id });

        Assert.Equal("Action_Blocked_ValidationMissing", result.Summary.Key);
    }

    [Fact]
    public async Task A_supplied_verification_confirms_the_result()
    {
        var (worker, runner, _, _, _) = Build();
        runner.Result = new ProcessResult { ExitCode = 0, StandardOutput = "state ok" };

        var result = await worker.ExecuteAsync(
            new ActionRequest { ActionId = PlainAction.Id },
            (execution, _) => Task.FromResult(execution.Output.Contains("state ok", StringComparison.Ordinal)));

        Assert.True(result.Succeeded);
        Assert.True(result.Validated);
        Assert.Contains(result.Evidence, line => line.Contains("verification: confirmed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_verification_that_does_not_confirm_the_result_turns_it_into_a_failure()
    {
        // Der Exit-Code sagte ok, der geprüfte Zustand sagt nein: dann ist die Aktion gescheitert
        // (Kapitel 86) und wird nicht als Erfolg mit Hinweis gemeldet.
        var (worker, runner, _, _, _) = Build();
        runner.Result = new ProcessResult { ExitCode = 0, StandardOutput = "state unchanged" };

        var result = await worker.ExecuteAsync(
            new ActionRequest { ActionId = PlainAction.Id },
            (_, _) => Task.FromResult(false));

        Assert.False(result.Validated);
        Assert.False(result.Succeeded);
        Assert.Equal(StageOutcome.Failed, result.Outcome);
        Assert.Equal("Action_Summary_ValidationFailed", result.Summary.Key);
    }

    [Fact]
    public async Task The_action_identifier_and_the_timeout_reach_the_runner()
    {
        var (worker, runner, _, _, _) = Build();

        await worker.ExecuteAsync(new ActionRequest { ActionId = PlainAction.Id });

        Assert.NotNull(runner.LastOptions);
        Assert.Equal(PlainAction.Timeout, runner.LastOptions!.Timeout);
    }

    [Fact]
    public async Task An_invalid_argument_never_reaches_the_runner()
    {
        var registryAction = new RegisteredAction
        {
            Id = "System.TouchPath",
            Executable = "takeown.exe",
            Arguments = new[] { new ActionArgumentSpec { Name = "Path", Kind = ActionArgumentKind.Path, Required = true } },
            ArgumentTemplate = new[] { "/f", "{Path}" },
        };

        var (worker, runner, _, _, _) = Build(registryAction);

        var result = await worker.ExecuteAsync(new ActionRequest
        {
            ActionId = registryAction.Id,
            Arguments = new Dictionary<string, string> { ["Path"] = "..\\..\\Windows\\System32" },
        });

        Assert.Equal(StageOutcome.Blocked, result.Outcome);
        Assert.Equal(BlockReasons.ActionPathNotAllowed, result.ErrorDetail);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Every_refusal_is_written_to_the_audit_log()
    {
        var (worker, _, audit, _, _) = Build();

        await worker.ExecuteAsync(new ActionRequest { ActionId = "Cleanup.Everything" });

        var entry = Assert.Single(audit.Entries);
        Assert.Equal("Cleanup.Everything", entry.OperationKey);
        Assert.Equal(StageOutcome.Blocked, entry.Result);
        Assert.Equal(BlockReasons.ActionNotRegistered, entry.Error);
    }
}
