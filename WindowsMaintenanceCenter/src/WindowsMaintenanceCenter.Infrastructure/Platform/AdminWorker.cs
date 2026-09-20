using System.ComponentModel;
using System.Diagnostics;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using Microsoft.Extensions.Logging;

namespace WindowsMaintenanceCenter.Infrastructure.Platform;

/// <summary>
/// Executes exactly one registered action (WMC specification, chapters 37 and 39).
///
/// What this type guarantees, and what it deliberately cannot do:
///
/// * It asks the registry first. An action that is not registered, is not allowed, or carries an
///   argument that does not match its declaration is refused before a process exists
///   (M31-S-002, M32-SEC-001 ... 004).
/// * It never builds a command line from user text: the program comes from the registration, the
///   arguments come from the registration, and a value handed in by the caller is only used after
///   the registry accepted it.
/// * It elevates per action, not per session (M31-S-001/S-003). When the action needs administrator
///   rights and the process does not have them, the action process is started with the
///   <c>runas</c> verb - one action, one prompt. A declined prompt (Win32 error 1223) is a normal
///   outcome: the result is BLOCKED with <c>UAC_CANCELLED</c> and nothing was changed
///   (M31-S-004).
/// * It reports success only from the exit code, and only as "executed". Chapter 86 allows SUCCESS
///   after EXECUTION **and** VALIDATION, so <see cref="ActionExecutionResult.Validated"/> stays
///   false unless a validator confirmed the result.
/// * It never hides a timeout: a process that hits the timeout is stopped and reported as unknown,
///   not as successful (M33-E-001).
/// </summary>
public interface IAdminWorker
{
    /// <summary>
    /// Runs one registered action. <paramref name="validate"/> is the verification of chapter 86; it
    /// receives the exit code and the output and answers whether the state really is as intended.
    /// </summary>
    Task<ActionExecutionResult> ExecuteAsync(
        ActionRequest request,
        Func<ActionExecutionResult, CancellationToken, Task<bool>>? validate = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Standard implementation. Uses the process runner unless the action needs elevation.</summary>
public sealed class AdminWorker : IAdminWorker
{
    private readonly IActionRegistry _registry;
    private readonly IProcessRunner _runner;
    private readonly IEnvironmentProbe _environment;
    private readonly IAuditLog _audit;
    private readonly ILiveProtocol _protocol;
    private readonly ILogger<AdminWorker>? _logger;

    public AdminWorker(
        IActionRegistry registry,
        IProcessRunner runner,
        IEnvironmentProbe environment,
        IAuditLog audit,
        ILiveProtocol protocol,
        ILogger<AdminWorker>? logger = null)
    {
        _registry = registry;
        _runner = runner;
        _environment = environment;
        _audit = audit;
        _protocol = protocol;
        _logger = logger;
    }

    public async Task<ActionExecutionResult> ExecuteAsync(
        ActionRequest request,
        Func<ActionExecutionResult, CancellationToken, Task<bool>>? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validation = _registry.Validate(request);
        if (!validation.IsValid || validation.Action is null)
        {
            return await BlockAsync(request, validation).ConfigureAwait(false);
        }

        var action = validation.Action;

        // M31-S-003: the prompt appears only when the action really needs it, and it appears for this
        // action alone. Without the rights nothing is started.
        if (action.RequiresAdmin && !_environment.IsElevated)
        {
            if (!_environment.IsWindows)
            {
                return await BlockAsync(request, ActionValidationResult.Invalid(
                    BlockReasons.UnsupportedPlatform,
                    new ActionValidationError { Key = "Action_Blocked_RequiresAdmin" })).ConfigureAwait(false);
            }

            return await RunElevatedAsync(action, request, validation, validate, cancellationToken).ConfigureAwait(false);
        }

        return await RunAsync(action, request, validation, validate, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionExecutionResult> RunAsync(
        RegisteredAction action,
        ActionRequest request,
        ActionValidationResult validation,
        Func<ActionExecutionResult, CancellationToken, Task<bool>>? validate,
        CancellationToken cancellationToken)
    {
        var options = new ProcessRunOptions { Timeout = action.Timeout };
        var result = await _runner.RunAsync(action.Executable, validation.ResolvedArguments, options, cancellationToken)
            .ConfigureAwait(false);

        return await FinishAsync(action, request, result.ExitCode, result.TimedOut, result.CombinedOutput,
            result.ErrorDetail, elevationCancelled: false, outputCaptured: true, validate, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Starts the single action with the <c>runas</c> verb. Windows does not allow capturing the
    /// output of an elevated process started this way, and this is not pretended: the result says
    /// <see cref="ActionExecutionResult.OutputCaptured"/> is false and the evidence names the reason.
    /// </summary>
    private async Task<ActionExecutionResult> RunElevatedAsync(
        RegisteredAction action,
        ActionRequest request,
        ActionValidationResult validation,
        Func<ActionExecutionResult, CancellationToken, Task<bool>>? validate,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = action.Executable,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
        };

        foreach (var argument in validation.ResolvedArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return await BlockAsync(
                    new ActionRequest { ActionId = action.Id },
                    ActionValidationResult.Invalid(BlockReasons.InvalidRequest, new ActionValidationError { Key = "Action_Blocked_NoExecutable" }))
                    .ConfigureAwait(false);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(action.Timeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Elevated process could not be stopped after the timeout.");
                }

                return await FinishAsync(action, request, exitCode: null, timedOut: true, output: string.Empty,
                    errorDetail: BlockReasons.ActionTimeout, elevationCancelled: false, outputCaptured: false,
                    validate, cancellationToken).ConfigureAwait(false);
            }

            return await FinishAsync(action, request, process.ExitCode, timedOut: false, output: string.Empty,
                errorDetail: null, elevationCancelled: false, outputCaptured: false, validate, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user closed the prompt. Nothing was started, nothing was changed.
            return await FinishAsync(action, request, exitCode: null, timedOut: false, output: string.Empty,
                errorDetail: BlockReasons.UacCancelled, elevationCancelled: true, outputCaptured: false,
                validate, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await FinishAsync(action, request, exitCode: null, timedOut: false, output: string.Empty,
                errorDetail: $"{ex.GetType().Name}: {ex.Message}", elevationCancelled: false, outputCaptured: false,
                validate, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ActionExecutionResult> FinishAsync(
        RegisteredAction action,
        ActionRequest request,
        int? exitCode,
        bool timedOut,
        string output,
        string? errorDetail,
        bool elevationCancelled,
        bool outputCaptured,
        Func<ActionExecutionResult, CancellationToken, Task<bool>>? validate,
        CancellationToken cancellationToken)
    {
        var blocked = timedOut || elevationCancelled || errorDetail is BlockReasons.UacCancelled
            || errorDetail is BlockReasons.ActionTimeout;

        // A tool that reports a finding with a code of its own did run and did answer. That is not a
        // success (the state is not as intended) and it is not a failed run either, so the result
        // carries both facts: the run is complete, and there is a finding (chapter 88). It is worked
        // out before the evidence list because the evidence states it.
        var reportedFindings = !blocked && exitCode is not null && exitCode != 0
            && action.FindingExitCodes.Contains(exitCode.Value);

        var evidence = new List<string>
        {
            $"action={action.Id}",
            $"program={action.Executable}",
            $"risk={action.Risk}; requiresAdmin={action.RequiresAdmin}",
            $"timeout={action.Timeout}",
            exitCode is null ? "exitCode=not reported" : $"exitCode={exitCode}",
            $"timedOut={timedOut}; elevationCancelled={elevationCancelled}; outputCaptured={outputCaptured}",
            reportedFindings
                ? "this is a documented finding code of the tool: the run is complete, the state is not repaired"
                : "the exit code is not a documented finding code of the tool",
            action.RollbackActionId is null
                ? "rollback: none registered for this action"
                : $"rollback: {action.RollbackActionId}",
        };

        if (!outputCaptured)
        {
            evidence.Add("this run was elevated per action; Windows does not let the caller read the output of such a process");
        }

        var succeeded = !blocked && (exitCode == 0 || reportedFindings);

        var draft = new ActionExecutionResult
        {
            ActionId = action.Id,
            Outcome = blocked ? StageOutcome.Blocked : succeeded ? StageOutcome.Succeeded : StageOutcome.Failed,
            Succeeded = succeeded,
            ExitCode = exitCode,
            ReportedFindings = reportedFindings,
            TimedOut = timedOut,
            ElevationCancelled = elevationCancelled,
            RequiresAdmin = action.RequiresAdmin,
            OutputCaptured = outputCaptured,
            Output = output,
            ErrorDetail = errorDetail,
            Summary = blocked
                ? LocalizedText.Of(elevationCancelled
                    ? "Action_Blocked_UacCancelled"
                    : timedOut
                        ? "Action_Blocked_Timeout"
                        : "Action_Summary_Blocked", errorDetail ?? "blocked")
                : succeeded
                    ? reportedFindings
                        ? LocalizedText.Of("Action_Summary_Findings", exitCode ?? 0)
                        : LocalizedText.Of("Action_Summary_Succeeded", exitCode ?? 0)
                    : LocalizedText.Of("Action_Summary_Failed", exitCode ?? -1),
            Evidence = evidence,
        };

        // Chapter 86: SUCCESS only after EXECUTION and VALIDATION. An action that declares itself
        // self-validating (its exit code is the verification) counts as validated; everything else
        // needs the caller's confirmation.
        var validated = false;
        if (succeeded)
        {
            if (validate is not null)
            {
                validated = await validate(draft, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                validated = action.SelfValidating;
            }
        }

        var verificationRan = succeeded && validate is not null;

        var result = draft with
        {
            Validated = validated,
            Evidence = evidence
                .Append(validated
                    ? "verification: confirmed"
                    : succeeded
                        ? verificationRan
                            ? "verification: ran and did not confirm the result"
                            : "verification: not performed - this run may not be reported as SUCCESS (chapter 86)"
                        : "verification: not applicable, the action did not succeed")
                .ToArray(),
        };

        if (succeeded && verificationRan && !validated)
        {
            // The action returned "ok" but the state is not as intended. That is a failed action, not
            // a successful one with a note (chapter 86).
            result = result with
            {
                Outcome = StageOutcome.Failed,
                Succeeded = false,
                ErrorDetail = "VERIFICATION_FAILED",
                Summary = LocalizedText.Of("Action_Summary_ValidationFailed"),
            };
        }
        else if (succeeded && !validated)
        {
            result = result with { Summary = LocalizedText.Of("Action_Blocked_ValidationMissing") };
        }

        _protocol.Publish(
            "ADMIN",
            result.Summary,
            result.Outcome switch
            {
                StageOutcome.Succeeded when result.ReportedFindings => Severity.Warning,
                StageOutcome.Succeeded when validated => Severity.Success,
                StageOutcome.Succeeded => Severity.Warning,
                StageOutcome.Blocked => Severity.Blocked,
                _ => Severity.Error,
            },
            string.Join("; ", result.Evidence));

        await _audit.RecordAsync(
            OperationKind.Execute,
            action.Id,
            CategoryOf(action),
            result.Outcome,
            componentId: action.Id,
            newState: result.Summary.Key,
            approval: request.Approval,
            backup: request.Backup,
            error: result.ErrorDetail,
            evidence: result.Evidence,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result;
    }

    private async Task<ActionExecutionResult> BlockAsync(ActionRequest request, ActionValidationResult validation)
    {
        var summaryKey = validation.ReasonCode switch
        {
            BlockReasons.ActionNotRegistered => "Action_Blocked_NotRegistered",
            BlockReasons.ActionNotAllowed => "Action_Blocked_NotAllowed",
            BlockReasons.ActionPathNotAllowed => "Action_Blocked_PathNotAllowed",
            BlockReasons.ActionArgumentInvalid => "Action_Blocked_ArgumentInvalid",
            BlockReasons.ActionRequiresAdmin => "Action_Blocked_RequiresAdmin",
            BlockReasons.UnsupportedPlatform => "Action_Blocked_RequiresAdmin",
            // Chapter 30/44: without a backup and without an approval for this very action nothing
            // runs, and the user is told which of the two is missing.
            BlockReasons.BackupRequired => "Action_Blocked_BackupRequired",
            BlockReasons.ApprovalMissing => "Action_Blocked_ApprovalMissing",
            _ => "Action_Summary_Blocked",
        };

        var details = validation.Errors.Count == 0
            ? summaryKey
            : string.Join("; ", validation.Errors.Select(error => $"{error.Key}:{error.Argument}"));

        var result = new ActionExecutionResult
        {
            ActionId = request.ActionId,
            Outcome = StageOutcome.Blocked,
            Succeeded = false,
            Validated = false,
            RequiresAdmin = validation.Action?.RequiresAdmin ?? false,
            ErrorDetail = validation.ReasonCode,
            Summary = LocalizedText.Of("Action_Summary_Blocked", details),
            Evidence = new[]
            {
                $"blocked={validation.ReasonCode}",
                $"action={request.ActionId}",
                details,
                "no process was started",
            },
        };

        _protocol.Publish("ADMIN", result.Summary, Severity.Blocked, details);
        _logger?.LogWarning("Action {ActionId} was blocked: {Reason}", request.ActionId, validation.ReasonCode);

        await _audit.RecordAsync(
            OperationKind.Execute,
            request.ActionId,
            validation.Action is null ? ComponentCategory.System : CategoryOf(validation.Action),
            StageOutcome.Blocked,
            componentId: request.ActionId,
            newState: summaryKey,
            approval: request.Approval,
            backup: request.Backup,
            error: validation.ReasonCode,
            evidence: result.Evidence,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        return result;
    }

    /// <summary>Category for the audit entry, derived from the identifier area of the action.</summary>
    private static ComponentCategory CategoryOf(RegisteredAction action)
    {
        var area = action.Id.Split('.')[0];
        return area switch
        {
            "Cleanup" => ComponentCategory.Maintenance,
            "System" => ComponentCategory.Windows,
            "Update" => ComponentCategory.Update,
            "Driver" => ComponentCategory.Driver,
            "Firmware" => ComponentCategory.Bios,
            "Service" => ComponentCategory.Windows,
            "Startup" => ComponentCategory.Windows,
            "Security" => ComponentCategory.Security,
            "Network" => ComponentCategory.Network,
            "Volume" => ComponentCategory.Storage,
            "Power" => ComponentCategory.Battery,
            _ => ComponentCategory.System,
        };
    }
}
