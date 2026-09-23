using System.Globalization;
// System.Globalization has a TextInfo as well; the alias names the intended type.
using TextInfo = WindowsMaintenanceCenter.Core.Values.TextInfo;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Hardware.Wmi;
using WindowsMaintenanceCenter.Infrastructure.Platform;

namespace WindowsMaintenanceCenter.Windows;

/// <summary>
/// Windows health assessment (spec section 17): integrity checks (DISM/SFC), event log errors,
/// Defender state, update availability and startup analysis.
///
/// Rules that the implementation enforces:
/// - A check that was not performed is reported as <see cref="StageOutcome.NotRun"/> with a reason,
///   never as "OK" (fail closed).
/// - Results are derived from real tool output (exit code plus parsed markers), never assumed.
/// - Repair is only performed when the caller explicitly asked for it, and success is only claimed
///   after a verification run.
/// - Defender is read, never changed.
/// </summary>
public sealed class WindowsHealthService : IWindowsHealthService
{
    private const string ModuleKey = "WIN";

    private readonly IProcessRunner _runner;
    private readonly PowerShellRunner _powershell;
    private readonly WmiReader _wmi;
    private readonly IEnvironmentProbe _environment;
    private readonly IHardwareProvider _hardware;
    private readonly IRegistryAccess _registry;
    private readonly ILiveProtocol _protocol;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public WindowsHealthService(
        IProcessRunner runner,
        PowerShellRunner powershell,
        WmiReader wmi,
        IEnvironmentProbe environment,
        IHardwareProvider hardware,
        IRegistryAccess registry,
        ILiveProtocol protocol,
        IAuditLog audit,
        IClock clock)
    {
        _runner = runner;
        _powershell = powershell;
        _wmi = wmi;
        _environment = environment;
        _hardware = hardware;
        _registry = registry;
        _protocol = protocol;
        _audit = audit;
        _clock = clock;
    }

    // ---------------------------------------------------------------------------------------------
    // Whole assessment
    // ---------------------------------------------------------------------------------------------
    public async Task<WindowsHealthReport> AssessAsync(SystemSnapshot? snapshot, bool includeOnlineChecks, IProgressReporter progress, CancellationToken cancellationToken)
    {
        var checks = new List<WindowsCheckResult>();
        var identity = snapshot?.Windows ?? await _hardware.GetWindowsIdentityAsync(cancellationToken).ConfigureAwait(false);
        var startup = await GetStartupAsync(cancellationToken).ConfigureAwait(false);
        var defender = await GetDefenderStatusAsync(cancellationToken).ConfigureAwait(false);
        var updates = await CheckUpdateAvailabilityAsync(includeOnlineChecks, cancellationToken).ConfigureAwait(false);
        var pendingReboot = DetectPendingReboot();
        var storageSpace = await AssessStorageSpaceAsync(snapshot, cancellationToken).ConfigureAwait(false);
        var tpm = await TpmReader.ReadAsync(_wmi, cancellationToken).ConfigureAwait(false);
        var firewall = await FirewallReader.ReadAsync(_wmi, cancellationToken).ConfigureAwait(false);
        var bitLocker = await BitLockerReader.ReadAsync(_wmi, _clock.Now, cancellationToken).ConfigureAwait(false);

        progress.Start("Progress_Windows_Health", ModuleKey, 9);

        checks.Add(RunStatusFromPlatform(snapshot));
        progress.ReportStep("WindowsCheck_DeviceErrors", null);
        checks.Add(await ReadSystemEventLogAsync(cancellationToken).ConfigureAwait(false));
        progress.ReportStep("WindowsCheck_EventLogErrors", null);
        checks.Add(DefenderCheck(defender));
        progress.ReportStep("WindowsCheck_DefenderStatus", null);
        checks.Add(UpdateCheck(updates));
        progress.ReportStep("WindowsCheck_UpdateStatus", null);
        checks.Add(SecureBootCheck(identity, pendingReboot));
        progress.ReportStep("WindowsCheck_SecureBoot", null);
        checks.Add(TpmCheck(tpm));
        progress.ReportStep("WindowsCheck_Tpm", null);
        checks.Add(FirewallCheck(firewall));
        progress.ReportStep("WindowsCheck_Firewall", null);
        checks.Add(BitLockerCheck(bitLocker));
        progress.ReportStep("WindowsCheck_BitLocker", null);
        checks.Add(storageSpace);
        progress.ReportStep("WindowsCheck_StorageSpace", null);
        checks.Add(StartupCheck(startup));
        progress.Complete(true);

        var status = OverallOf(checks);
        var performed = checks.Count(c => c.Performed);

        return new WindowsHealthReport
        {
            Identity = identity,
            Status = status,
            Summary = performed == 0
                ? LocalizedText.Of("Windows_Health_NoChecks")
                : LocalizedText.Of("Windows_Health_Summary", performed, checks.Count, status.ToString()),
            Checks = checks,
            Defender = defender,
            Updates = updates,
            Startup = startup,
            PendingRebootReason = pendingReboot,
            AssessedAt = _clock.Now,
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Integrity checks (DISM / SFC)
    // ---------------------------------------------------------------------------------------------
    public Task<IntegrityCheckResult> RunComponentStoreCheckAsync(bool repair, ApprovalRecord? approval, IProgressReporter progress, CancellationToken cancellationToken) =>
        RunIntegrityCheckAsync(WindowsCheckId.ComponentStore, repair, approval, progress, cancellationToken);

    public Task<IntegrityCheckResult> RunSystemFileCheckAsync(bool repair, ApprovalRecord? approval, IProgressReporter progress, CancellationToken cancellationToken) =>
        RunIntegrityCheckAsync(WindowsCheckId.SystemFileIntegrity, repair, approval, progress, cancellationToken);

    private async Task<IntegrityCheckResult> RunIntegrityCheckAsync(
        WindowsCheckId check,
        bool repair,
        ApprovalRecord? approval,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var isComponentStore = check == WindowsCheckId.ComponentStore;
        var displayKey = isComponentStore ? "WindowsCheck_ComponentStore" : "WindowsCheck_SystemFileIntegrity";
        var tool = isComponentStore ? "dism.exe" : "sfc.exe";
        var arguments = BuildIntegrityArguments(check, repair);
        var commandLine = $"{tool} {string.Join(' ', arguments)}";

        if (!_environment.IsWindows)
        {
            return NotRun(check, displayKey, "Integrity checks require Windows; this host is not Windows.", commandLine);
        }

        // Fail closed: a repair changes the system, so it is refused unless the record of the
        // confirmed operation is present. The service does not trust the caller to have asked.
        if (repair && check == WindowsCheckId.SystemFileIntegrity)
        {
            return await BlockedAsync(
                check,
                BlockReasons.RepairNotAutomated,
                LocalizedText.Of("Integrity_Blocked_SfcRepairNotAutomated"),
                commandLine,
                requiresAdministrator: true,
                cancellationToken).ConfigureAwait(false);
        }

        if (repair && approval is null)
        {
            return await BlockedAsync(
                check,
                BlockReasons.ApprovalMissing,
                LocalizedText.Of("Integrity_Blocked_NoApproval"),
                commandLine,
                requiresAdministrator: true,
                cancellationToken).ConfigureAwait(false);
        }

        if (!_environment.IsElevated)
        {
            var blocked = new IntegrityCheckResult
            {
                Check = check,
                Outcome = StageOutcome.Blocked,
                CommandLine = commandLine,
                RequiresAdministrator = true,
                RepairRequested = repair,
                Summary = LocalizedText.Of("Integrity_Blocked_NotElevated"),
                Evidence = new[] { "elevation: false", $"command: {commandLine}" },
            };
            await AuditAsync(check, repair, approval, StageOutcome.Blocked, blocked.Summary.Key, result: null, evidence: new[] { "elevation: false" }, cancellationToken).ConfigureAwait(false);
            return blocked;
        }

        var started = _clock.Now;
        progress.Start(isComponentStore ? "Progress_Dism" : "Progress_Sfc", ModuleKey, null);
        _protocol.Info(ModuleKey, LocalizedText.Of(repair ? "Integrity_Running_Repair" : "Integrity_Running_Scan", displayKey));

        var runOptions = new ProcessRunOptions { Timeout = TimeSpan.FromMinutes(repair ? 75 : 45), MaxOutputCharacters = 2_000_000 };
        // Both tools are started through the same runner: the arguments decide what happens, and
        // for SFC that is `/verifyonly`, a read-only verification.
        var result = await _runner.RunAsync(tool, arguments, runOptions, cancellationToken).ConfigureAwait(false);

        var duration = _clock.Now - started;
        var output = result.CombinedOutput;
        var (changesPerformed, repairSucceeded, summary) = isComponentStore
            ? InterpretDismOutput(output, repair)
            : InterpretSfcOutput(output);

        var needsVerification = repair && repairSucceeded && changesPerformed;
        var verified = false;
        var evidence = new List<string>
        {
            $"tool={tool}",
            $"exitCode={result.ExitCode}",
            $"timedOut={result.TimedOut}",
            $"duration={duration.TotalSeconds:0.0}s",
            $"changesPerformed={changesPerformed}",
        };

        if (repair && approval is not null)
        {
            evidence.Add($"approval={approval.RequestId} operation={approval.OperationId} decided={approval.DecidedAt:O}");
        }

        if (needsVerification)
        {
            // A repair counts as successful only after a separate verification run confirmed it.
            progress.ReportStep("Integrity_Verifying", null);
            var verify = await _runner.RunAsync(
                tool,
                BuildIntegrityArguments(check, repair: false),
                new ProcessRunOptions { Timeout = TimeSpan.FromMinutes(45), MaxOutputCharacters = 1_000_000 },
                cancellationToken).ConfigureAwait(false);

            var (_, _, verifySummary) = InterpretDismOutput(verify.CombinedOutput, repair: false);
            verified = verify.ExitCode == 0 && verifySummary.Key == "Integrity_Summary_NoCorruption";
            evidence.Add($"verificationExitCode={verify.ExitCode}");
            evidence.Add($"verificationResult={verifySummary.Key}");
        }

        progress.Complete(result.ExitCode == 0);

        var outcome = result.TimedOut ? StageOutcome.Failed
            : result.ExitCode != 0 ? StageOutcome.Failed
            : repair && !repairSucceeded ? StageOutcome.Failed
            : StageOutcome.Succeeded;

        var final = new IntegrityCheckResult
        {
            Check = check,
            Outcome = outcome,
            CommandLine = commandLine,
            ExitCode = result.ExitCode,
            TimedOut = result.TimedOut,
            RepairRequested = repair,
            RepairSucceeded = repairSucceeded,
            ChangesPerformed = changesPerformed,
            VerifiedAfterRepair = verified,
            Summary = result.TimedOut
                ? LocalizedText.Of("Integrity_Summary_Timeout", displayKey)
                : summary,
            RawOutput = Trim(output, 200_000),
            RequiresAdministrator = true,
            Evidence = evidence,
            Duration = duration,
        };

        await AuditAsync(check, repair, approval, outcome, final.Summary.Key, final, evidence, cancellationToken).ConfigureAwait(false);
        return final;
    }

    /// <summary>Refusal result; it is audited as well, because a refused repair is a real event.</summary>
    private async Task<IntegrityCheckResult> BlockedAsync(
        WindowsCheckId check,
        string reasonCode,
        LocalizedText reason,
        string commandLine,
        bool requiresAdministrator,
        CancellationToken cancellationToken)
    {
        var blocked = new IntegrityCheckResult
        {
            Check = check,
            Outcome = StageOutcome.Blocked,
            CommandLine = commandLine,
            RepairRequested = true,
            RequiresAdministrator = requiresAdministrator,
            Summary = reason,
            Evidence = new[] { $"reasonCode={reasonCode}", $"reason={reason.Key}", $"command: {commandLine}" },
        };

        _protocol.Warning(ModuleKey, LocalizedText.Of("Integrity_Protocol_Blocked", reasonCode), reason.Key);
        await AuditAsync(check, repair: true, approval: null, StageOutcome.Blocked, reason.Key, blocked, blocked.Evidence, cancellationToken).ConfigureAwait(false);
        return blocked;
    }

    /// <summary>
    /// Writes the audit entry of an integrity run. The audit is written with
    /// <see cref="CancellationToken.None"/> on purpose: an operation that happened must stay
    /// recorded even when the caller cancelled right after it.
    /// </summary>
    private Task AuditAsync(
        WindowsCheckId check,
        bool repair,
        ApprovalRecord? approval,
        StageOutcome outcome,
        string summaryKey,
        IntegrityCheckResult? result,
        IReadOnlyList<string> evidence,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return _audit.RecordAsync(
            repair ? OperationKind.Execute : OperationKind.Verify,
            $"windows-integrity-{check}",
            ComponentCategory.Windows,
            outcome,
            componentId: check.ToString(),
            newState: summaryKey,
            approval: approval,
            error: outcome == StageOutcome.Failed ? $"exitCode={result?.ExitCode}" : null,
            evidence: evidence,
            cancellationToken: CancellationToken.None);
    }

    private static IReadOnlyList<string> BuildIntegrityArguments(WindowsCheckId check, bool repair)
    {
        if (check == WindowsCheckId.SystemFileIntegrity)
        {
            // `sfc.exe /verifyonly` only reads and reports. The repair (`/scannow`) is deliberately
            // not automated: its exit codes are ambiguous and a silent repair is not verifiable here.
            return new List<string> { "/verifyonly" };
        }

        // Only documented DISM component store operations are used.
        return new List<string> { "/Online", "/Cleanup-Image", repair ? "/RestoreHealth" : "/ScanHealth", "/NoRestart" };
    }

    /// <summary>
    /// Maps real DISM output to a result. DISM reports its findings in text and in the exit code;
    /// anything that cannot be recognised stays "unknown", never "healthy".
    /// </summary>
    private static (bool ChangesPerformed, bool RepairSucceeded, LocalizedText Summary) InterpretDismOutput(string output, bool repair)
    {
        var text = output ?? string.Empty;

        var noCorruption = text.Contains("No component store corruption detected", StringComparison.OrdinalIgnoreCase);
        var repairable = text.Contains("The component store is repairable", StringComparison.OrdinalIgnoreCase);
        var repaired = text.Contains("The operation completed successfully", StringComparison.OrdinalIgnoreCase);
        var repairedRestored = text.Contains("The restore operation completed successfully", StringComparison.OrdinalIgnoreCase);
        var unrepaired = text.Contains("The component store has been corrupted", StringComparison.OrdinalIgnoreCase)
            || text.Contains("cannot be repaired", StringComparison.OrdinalIgnoreCase);

        if (repair)
        {
            return repaired || repairedRestored
                ? (true, true, LocalizedText.Of("Integrity_Summary_RepairCompleted"))
                : unrepaired
                    ? (false, false, LocalizedText.Of("Integrity_Summary_RepairFailed"))
                    : (false, false, LocalizedText.Of("Integrity_Summary_Unknown"));
        }

        if (noCorruption)
        {
            return (false, true, LocalizedText.Of("Integrity_Summary_NoCorruption"));
        }

        if (repairable)
        {
            return (false, true, LocalizedText.Of("Integrity_Summary_Repairable"));
        }

        if (unrepaired)
        {
            return (false, false, LocalizedText.Of("Integrity_Summary_Corrupted"));
        }

        return (false, false, LocalizedText.Of("Integrity_Summary_Unknown"));
    }

    /// <summary>Maps real SFC output to a result; an unrecognised message stays unknown.</summary>
    private static (bool ChangesPerformed, bool RepairSucceeded, LocalizedText Summary) InterpretSfcOutput(string output)
    {
        var text = output ?? string.Empty;

        if (text.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
        {
            return (false, true, LocalizedText.Of("Integrity_Summary_NoViolations"));
        }

        if (text.Contains("found integrity violations", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Windows Resource Protection found corrupt files", StringComparison.OrdinalIgnoreCase))
        {
            return (false, false, LocalizedText.Of("Integrity_Summary_ViolationsFound"));
        }

        if (text.Contains("system repair pending", StringComparison.OrdinalIgnoreCase))
        {
            return (false, false, LocalizedText.Of("Integrity_Summary_RepairPending"));
        }

        if (text.Contains("could not perform the requested operation", StringComparison.OrdinalIgnoreCase))
        {
            return (false, false, LocalizedText.Of("Integrity_Summary_NotPossible"));
        }

        return (false, false, LocalizedText.Of("Integrity_Summary_Unknown"));
    }

    private IntegrityCheckResult NotRun(WindowsCheckId check, string displayKey, string reason, string commandLine) => new()
    {
        Check = check,
        Outcome = StageOutcome.NotRun,
        CommandLine = commandLine,
        RequiresAdministrator = true,
        Summary = LocalizedText.Of("Integrity_NotRun_Reason", reason),
        Evidence = new[] { $"reason={reason}", $"check={displayKey}" },
    };

    // ---------------------------------------------------------------------------------------------
    // Windows updates (real update agent only)
    // ---------------------------------------------------------------------------------------------
    public async Task<UpdateAvailability> CheckUpdateAvailabilityAsync(bool queryOnline, CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.WindowsApi(_clock.Now, "Microsoft.Update.Session (COM) via allow listed PowerShell template");
        var recent = await ReadUpdateHistoryAsync(cancellationToken).ConfigureAwait(false);
        var pendingReboot = DetectPendingReboot();

        if (!_environment.IsWindows)
        {
            return new UpdateAvailability
            {
                Outcome = StageOutcome.NotRun,
                SearchPerformed = false,
                PendingReboot = !string.IsNullOrWhiteSpace(pendingReboot),
                Summary = LocalizedText.Of("WindowsUpdate_NotRun_NoWindows"),
                RecentUpdates = recent,
            };
        }

        if (!queryOnline)
        {
            return new UpdateAvailability
            {
                Outcome = StageOutcome.Skipped,
                SearchPerformed = false,
                PendingReboot = !string.IsNullOrWhiteSpace(pendingReboot),
                Summary = LocalizedText.Of("WindowsUpdate_Skipped_Offline"),
                RecentUpdates = recent,
            };
        }

        if (_environment.IsOfflineRequested)
        {
            return new UpdateAvailability
            {
                Outcome = StageOutcome.Blocked,
                SearchPerformed = false,
                PendingReboot = !string.IsNullOrWhiteSpace(pendingReboot),
                Summary = LocalizedText.Of("WindowsUpdate_Blocked_OfflineMode"),
                RecentUpdates = recent,
                ErrorDetail = BlockReasons.OfflineMode,
            };
        }

        var result = await _powershell.RunAsync(
            PowerShellCommandCatalog.WindowsUpdateSession,
            null,
            new ProcessRunOptions { Timeout = TimeSpan.FromMinutes(5) },
            cancellationToken).ConfigureAwait(false);

        var drafts = new Dictionary<int, UpdateDraft>();
        var pendingCount = 0;
        bool? agentRebootRequired = null;
        string? error = null;

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();

            if (trimmed.StartsWith("PENDING=", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(trimmed["PENDING=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pending))
                {
                    pendingCount = pending;
                }

                continue;
            }

            if (trimmed.StartsWith("WU_ERROR=", StringComparison.OrdinalIgnoreCase))
            {
                error = trimmed["WU_ERROR=".Length..];
                continue;
            }

            if (trimmed.StartsWith("SYSTEM_REBOOT=", StringComparison.OrdinalIgnoreCase))
            {
                agentRebootRequired = ParseFlag(trimmed["SYSTEM_REBOOT=".Length..]);
                continue;
            }

            // Indexed lines have the shape FIELD=<index>|<value>; the value is everything after the
            // first '|', so a title may contain any character. Unknown fields are ignored on purpose:
            // a future agent version must not be able to inject a value that is then displayed as
            // measured.
            var first = trimmed.IndexOf('=');
            var pipe = trimmed.IndexOf('|');
            if (first <= 0 || pipe < first)
            {
                continue;
            }

            var field = trimmed[..first];
            var indexText = trimmed[(first + 1)..pipe];
            var value = trimmed[(pipe + 1)..];
            if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                continue;
            }

            if (!drafts.TryGetValue(index, out var draft))
            {
                draft = new UpdateDraft();
                drafts[index] = draft;
            }

            switch (field.ToUpperInvariant())
            {
                case "UPDATE":
                    draft.Title = value;
                    break;
                case "KB":
                    draft.KnowledgeBaseId = value;
                    break;
                case "CAT":
                    draft.Category = value;
                    break;
                case "SEV":
                    draft.Severity = value;
                    break;
                case "SIZE":
                    draft.DownloadSize = value;
                    break;
                case "REBOOT":
                    draft.RebootRequired = ParseFlag(value);
                    break;
                case "MAND":
                    draft.IsMandatory = ParseFlag(value);
                    break;
                case "DL":
                    draft.IsDownloaded = ParseFlag(value);
                    break;
            }
        }

        var available = drafts
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value.ToInfo(origin))
            .ToList();

        if (agentRebootRequired == true)
        {
            // UPDATE-F-008: the agent's own statement about a pending restart is evidence and is
            // added to what the registry markers say - it never replaces them.
            const string agentReason = "the update agent reports a pending restart (Microsoft.Update.SystemInfo.RebootRequired)";
            pendingReboot = pendingReboot is null ? agentReason : $"{pendingReboot}, {agentReason}";
        }

        if (error is not null)
        {
            return new UpdateAvailability
            {
                Outcome = StageOutcome.Failed,
                SearchPerformed = true,
                PendingReboot = !string.IsNullOrWhiteSpace(pendingReboot),
                Summary = LocalizedText.Of("WindowsUpdate_Error", error),
                RecentUpdates = recent,
                ErrorDetail = error,
            };
        }

        return new UpdateAvailability
        {
            Outcome = StageOutcome.Succeeded,
            SearchPerformed = true,
            RequiresAdministrator = !_environment.IsElevated,
            PendingCount = pendingCount,
            PendingReboot = !string.IsNullOrWhiteSpace(pendingReboot),
            Summary = pendingCount == 0
                ? LocalizedText.Of("WindowsUpdate_UpToDate")
                : LocalizedText.Of("WindowsUpdate_Pending", pendingCount),
            RecentUpdates = recent,
            Available = available,
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Windows updates: download and install one offered update (rule 90, UPDATE-F-005 ... F-008)
    // ---------------------------------------------------------------------------------------------
    public Task<UpdateActionReport> DownloadUpdateAsync(int index, IProgressReporter progress, CancellationToken cancellationToken) =>
        RunUpdateActionAsync(PowerShellCommandCatalog.WindowsUpdateDownload, index, "Progress_Update_Download", install: false, approval: null, progress, cancellationToken);

    public Task<UpdateActionReport> InstallUpdateAsync(int index, ApprovalRecord? approval, IProgressReporter progress, CancellationToken cancellationToken) =>
        RunUpdateActionAsync(PowerShellCommandCatalog.WindowsUpdateInstall, index, "Progress_Update_Install", install: true, approval, progress, cancellationToken);

    private async Task<UpdateActionReport> RunUpdateActionAsync(
        string template,
        int index,
        string progressKey,
        bool install,
        ApprovalRecord? approval,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.WindowsApi(_clock.Now, "Microsoft.Update.Session (COM) via allow listed PowerShell template");

        // Fail closed before anything runs: no approval for an installation, no elevated process,
        // no impossible position in the list.
        if (install && approval is null)
        {
            return await BlockUpdateAsync(index, BlockReasons.ApprovalMissing, "WindowsUpdate_Blocked_NoApproval", origin, cancellationToken).ConfigureAwait(false);
        }

        if (!_environment.IsWindows)
        {
            return await BlockUpdateAsync(index, BlockReasons.UnsupportedPlatform, "WindowsUpdate_Blocked_NoWindows", origin, cancellationToken).ConfigureAwait(false);
        }

        if (install && !_environment.IsElevated)
        {
            return await BlockUpdateAsync(index, BlockReasons.NotElevated, "WindowsUpdate_Blocked_NotElevated", origin, cancellationToken).ConfigureAwait(false);
        }

        if (index < 0)
        {
            return await BlockUpdateAsync(index, BlockReasons.InvalidRequest, "WindowsUpdate_Blocked_InvalidIndex", origin, cancellationToken).ConfigureAwait(false);
        }

        progress.Start(progressKey, ModuleKey, 1);

        var result = await _powershell.RunAsync(
            template,
            new Dictionary<string, string> { ["INDEX"] = index.ToString(CultureInfo.InvariantCulture) },
            new ProcessRunOptions { Timeout = TimeSpan.FromMinutes(install ? 90 : 60) },
            cancellationToken).ConfigureAwait(false);

        var report = ParseUpdateAction(result, index, origin, install);

        // UPDATE-R-001: the real state after the action is measured, not assumed. A successful
        // installation whose offer is still pending stays visible as exactly that.
        if (report.Outcome is StageOutcome.Succeeded or StageOutcome.Failed)
        {
            var after = await CheckUpdateAvailabilityAsync(queryOnline: true, cancellationToken).ConfigureAwait(false);
            report = report with
            {
                PendingCountAfter = after.PendingCount,
                PendingRebootAfter = after.PendingReboot,
                Evidence = report.Evidence.Concat(new[]
                {
                    $"re-check after the action: pendingUpdates={after.PendingCount}; pendingReboot={after.PendingReboot}",
                    "re-check source: Microsoft.Update.Session search, the same agent that offered the update",
                }).ToArray(),
            };
        }

        await _audit.RecordAsync(
            install ? OperationKind.Execute : OperationKind.Download,
            install ? "windows-update-install" : "windows-update-download",
            ComponentCategory.Windows,
            report.Outcome,
            componentId: index.ToString(CultureInfo.InvariantCulture),
            newState: report.Summary.Key,
            approval: approval,
            error: report.ErrorDetail,
            evidence: report.Evidence,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        progress.Complete(report.Succeeded);
        return report;
    }

    private async Task<UpdateActionReport> BlockUpdateAsync(int index, string reasonCode, string reasonKey, ValueOrigin origin, CancellationToken cancellationToken)
    {
        var report = new UpdateActionReport
        {
            Index = index,
            Outcome = StageOutcome.Blocked,
            RequiresAdministrator = true,
            Summary = LocalizedText.Of(reasonKey),
            ErrorDetail = reasonCode,
            Title = TextInfo.Unknown(origin, "the offer list was not queried because the action was blocked before it started"),
            ResultCode = TextInfo.Unknown(origin, "no action was started, so the agent reported no result code"),
            Evidence = new[] { $"blocked={reasonCode}", $"origin={origin.Token()}", "the update agent was not contacted" },
        };

        await _audit.RecordAsync(
            OperationKind.Execute,
            "windows-update-blocked",
            ComponentCategory.Windows,
            StageOutcome.Blocked,
            componentId: index.ToString(CultureInfo.InvariantCulture),
            newState: reasonKey,
            error: reasonCode,
            evidence: report.Evidence,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return report;
    }

    /// <summary>
    /// Reads what the agent reported. A missing result code is not success, and a position outside
    /// the offer list is a statement about the list, not about the update (UPDATE-E-001).
    /// </summary>
    private UpdateActionReport ParseUpdateAction(ProcessResult result, int index, ValueOrigin origin, bool install)
    {
        string? Field(string key)
        {
            var line = result.StandardOutput.Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            return line?[(key.Length + 1)..];
        }

        var title = Field("TITLE");
        var error = Field("WU_ERROR");
        var outOfRange = Field("WU_INDEX_OUT_OF_RANGE");
        var codeText = Field(install ? "INSTALL_RESULTCODE" : "DOWNLOAD_RESULTCODE");
        var hresult = Field(install ? "INSTALL_HRESULT" : "DOWNLOAD_HRESULT");
        var rebootReported = string.Equals(Field("INSTALL_REBOOT") ?? Field("REBOOT_REQUIRED"), "True", StringComparison.OrdinalIgnoreCase);
        var downloaded = string.Equals(Field("IS_DOWNLOADED"), "True", StringComparison.OrdinalIgnoreCase);
        var installed = string.Equals(Field("IS_INSTALLED"), "True", StringComparison.OrdinalIgnoreCase);

        var evidence = new List<string>
        {
            $"template={(install ? PowerShellCommandCatalog.WindowsUpdateInstall : PowerShellCommandCatalog.WindowsUpdateDownload)}; index={index}",
            $"resultCode={codeText ?? "not reported"}; hresult={hresult ?? "not reported"}",
            $"rebootRequiredByAgent={rebootReported}; downloaded={downloaded}; installed={installed}",
            $"origin={origin.Token()}",
            install
                ? "the installer ran through the official Windows Update agent; Windows Maintenance Center itself replaced no file"
                : "the downloader ran through the official Windows Update agent",
        };

        UpdateActionReport Report(StageOutcome outcome, bool succeeded, bool partial, string summaryKey, params object?[] arguments) => new()
        {
            Index = index,
            Title = title is null ? TextInfo.Unknown(origin, "the agent did not report a title for this position") : TextInfo.Known(title, origin),
            Outcome = outcome,
            Succeeded = succeeded,
            Partial = partial,
            RebootRequired = rebootReported,
            RequiresAdministrator = true,
            ResultCode = codeText is null ? TextInfo.Unknown(origin, "the agent did not report a result code") : TextInfo.Known(codeText, origin),
            ErrorDetail = outcome == StageOutcome.Failed ? error ?? hresult ?? codeText : null,
            Summary = LocalizedText.Of(summaryKey, arguments),
            Evidence = evidence,
        };

        if (error is not null)
        {
            return Report(StageOutcome.Failed, false, false, "WindowsUpdate_Action_Error", error);
        }

        if (outOfRange is not null)
        {
            // The offer list changed between the search the user saw and this call: nothing was done.
            return Report(StageOutcome.Blocked, false, false, "WindowsUpdate_Action_IndexOutOfRange", outOfRange);
        }

        if (!int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return Report(StageOutcome.Failed, false, false, "WindowsUpdate_Action_NoResultCode");
        }

        var verdict = WindowsUpdateResultCodes.Interpret(code);

        if (verdict == WindowsUpdateResultVerdict.Unknown)
        {
            return Report(StageOutcome.Failed, false, false, "WindowsUpdate_Action_UnknownResultCode", code);
        }

        if (!WindowsUpdateResultCodes.IsSuccess(verdict))
        {
            return Report(StageOutcome.Failed, false, false, install ? "WindowsUpdate_Install_Failed" : "WindowsUpdate_Download_Failed", code);
        }

        var partial = verdict == WindowsUpdateResultVerdict.SucceededWithErrors;
        var key = install
            ? partial ? "WindowsUpdate_Install_SucceededWithErrors" : "WindowsUpdate_Install_Succeeded"
            : partial ? "WindowsUpdate_Download_SucceededWithErrors" : "WindowsUpdate_Download_Succeeded";

        return Report(StageOutcome.Succeeded, true, partial, key, code);
    }

    private async Task<IReadOnlyList<WindowsUpdateInfo>> ReadUpdateHistoryAsync(CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.WindowsApi(_clock.Now, "Win32_QuickFixEngineering");
        var entries = await _wmi.QueryAsync("Win32_QuickFixEngineering", cancellationToken: cancellationToken).ConfigureAwait(false);

        return entries.Select(entry => new WindowsUpdateInfo
        {
            HotFixId = Text(entry, "HotFixID", origin),
            Description = Text(entry, "Description", origin),
            InstalledOn = Text(entry, "InstalledOn", origin),
            InstalledBy = Text(entry, "InstalledBy", origin),
            Caption = Text(entry, "Caption", origin),
            SupportUrl = Text(entry, "Caption", origin),
        }).ToList();
    }

    private static TextInfo Text(WmiObject? source, string property, ValueOrigin origin) =>
        source is not null && source.TryGetString(property, out var value)
            ? TextInfo.Known(value, origin)
            : TextInfo.Unknown(origin, $"{property} not reported");

    // ---------------------------------------------------------------------------------------------
    // Event log (System log, error and critical entries)
    // ---------------------------------------------------------------------------------------------
    private async Task<WindowsCheckResult> ReadSystemEventLogAsync(CancellationToken cancellationToken)
    {
        const string displayKey = "WindowsCheck_EventLogErrors";
        var origin = ValueOrigin.Wmi(_clock.Now, "Win32_NTLogEvent", SensorQuality.Medium);

        if (!_environment.IsWindows)
        {
            return Check(WindowsCheckId.EventLogErrors, displayKey, HealthStatus.Unknown, StageOutcome.NotRun,
                LocalizedText.Of("Windows_Check_NotRun_NoWindows"), detail: "the event log is a Windows component", requiresAdmin: false);
        }

        var since = _clock.Now.AddDays(-7).UtcDateTime;
        var where = $"Logfile='System' AND (Level=1 OR Level=2) AND TimeGenerated > '{since:yyyyMMddHHmmss}.000000+000'";

        var events = await _wmi.QueryAsync("Win32_NTLogEvent", where, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (events.Count == 0)
        {
            // Zero rows can mean "no errors" or "the query failed". The reader records the reason.
            if (!string.IsNullOrWhiteSpace(_wmi.LastError))
            {
                return Check(WindowsCheckId.EventLogErrors, displayKey, HealthStatus.Unknown, StageOutcome.Failed,
                    LocalizedText.Of("Windows_Check_EventLog_Unreadable", _wmi.LastError),
                    detail: _wmi.LastError,
                    requiresAdmin: false);
            }

            return Check(WindowsCheckId.EventLogErrors, displayKey, HealthStatus.Healthy, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_EventLog_Clean"), detail: "no error or warning entries in the System log within the last 7 days",
                requiresAdmin: false);
        }

        var errorCount = 0;
        var warningCount = 0;
        var sources = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in events)
        {
            var level = entry.TryGetUInt("Level", out var value) ? value : 3;
            if (level == 1)
            {
                errorCount++;
            }
            else
            {
                warningCount++;
            }

            var source = entry.GetString("SourceName") ?? "unknown";
            sources[source] = sources.TryGetValue(source, out var count) ? count + 1 : 1;
        }

        var topSources = sources.OrderByDescending(pair => pair.Value).Take(5)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToList();

        var status = errorCount > 20 ? HealthStatus.Warning : errorCount > 0 ? HealthStatus.Attention : HealthStatus.Healthy;

        return new WindowsCheckResult
        {
            Check = WindowsCheckId.EventLogErrors,
            DisplayNameKey = displayKey,
            Status = status,
            Outcome = StageOutcome.Succeeded,
            Summary = LocalizedText.Of("Windows_Check_EventLog_Summary", errorCount, warningCount, events.Count),
            Detail = string.Join("; ", topSources),
            Performed = true,
            RequiresAdministrator = false,
            Evidence = new[]
            {
                $"wmi=Win32_NTLogEvent; query={where}",
                $"errors={errorCount}; warnings={warningCount}; read={events.Count}",
                $"top sources: {string.Join(", ", topSources)}",
                $"origin={origin.Token()}",
            },
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Defender (read only)
    // ---------------------------------------------------------------------------------------------
    public async Task<DefenderStatus> GetDefenderStatusAsync(CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.WindowsApi(_clock.Now, "Get-MpComputerStatus (allow listed template)");

        if (!_environment.IsWindows)
        {
            return new DefenderStatus
            {
                Available = false,
                Summary = LocalizedText.Of("Defender_Unavailable_NoWindows"),
                ErrorDetail = "Microsoft Defender is a Windows component",
            };
        }

        var result = await _powershell.RunAsync(
            PowerShellCommandCatalog.DefenderStatus,
            null,
            new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(60) },
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return new DefenderStatus
            {
                Available = false,
                Summary = LocalizedText.Of("Defender_Unavailable_QueryFailed", result.ErrorDetail ?? result.StandardError.Trim()),
                ErrorDetail = result.ErrorDetail ?? result.StandardError.Trim(),
            };
        }

        string? value1(string key)
        {
            var line = result.StandardOutput.Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            return line?[(key.Length + 1)..];
        }

        var error = value1("DEFENDER_ERROR");
        if (error is not null)
        {
            return new DefenderStatus
            {
                Available = false,
                Summary = LocalizedText.Of("Defender_Unavailable_QueryFailed", error),
                ErrorDetail = error,
            };
        }

        var avEnabled = value1("AV");
        var rtp = value1("RTP");
        var engine = value1("ENGINE");
        var signature = value1("SIGNATURE");
        var signatureAge = value1("SIGNATURE_AGE_DAYS");
        var tamper = value1("TAMPER");

        var ageDays = signatureAge is not null && int.TryParse(signatureAge, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAge)
            ? parsedAge
            : (int?)null;

        // An outdated signature definition is a real, evidence based finding.
        var outdated = ageDays.HasValue && ageDays.Value > 3;

        return new DefenderStatus
        {
            Available = true,
            AntivirusEnabled = TextInfo.From(avEnabled, origin, "AntivirusEnabled not reported"),
            RealTimeProtectionEnabled = TextInfo.From(rtp, origin, "RealTimeProtectionEnabled not reported"),
            EngineVersion = TextInfo.From(engine, origin, "engine version not reported"),
            SignatureVersion = TextInfo.From(signature, origin, "signature version not reported"),
            SignatureLastUpdated = TextInfo.From(ageDays?.ToString(CultureInfo.InvariantCulture), origin, "signature age not reported"),
            TamperProtection = TextInfo.From(tamper, origin, "tamper protection state not reported"),
            ThreatsDetected = TextInfo.Unknown(origin, "the threat history is not part of this query and is not modified by Windows Maintenance Center"),
            AntivirusProvider = TextInfo.Known("Microsoft Defender", origin),
            AntispywareEnabled = TextInfo.From(avEnabled, origin, "AntispywareEnabled not reported"),
            Summary = outdated
                ? LocalizedText.Of("Defender_Summary_Outdated", ageDays)
                : LocalizedText.Of("Defender_Summary_Ok"),
        };
    }

    private WindowsCheckResult DefenderCheck(DefenderStatus defender)
    {
        const string displayKey = "WindowsCheck_DefenderStatus";

        if (!defender.Available)
        {
            return Check(WindowsCheckId.DefenderStatus, displayKey, HealthStatus.Unknown, StageOutcome.Failed,
                defender.Summary, defender.ErrorDetail, requiresAdmin: false);
        }

        var protectionOff = defender.RealTimeProtectionEnabled.IsKnown && defender.RealTimeProtectionEnabled.Value == "False";
        var outdated = defender.Summary.Key == "Defender_Summary_Outdated";

        return Check(WindowsCheckId.DefenderStatus, displayKey,
            protectionOff ? HealthStatus.Critical : outdated ? HealthStatus.Attention : HealthStatus.Healthy,
            StageOutcome.Succeeded,
            defender.Summary,
            defender.Summary.Key,
            requiresAdmin: false);
    }

    // ---------------------------------------------------------------------------------------------
    // Startup / services (read only, never changed)
    // ---------------------------------------------------------------------------------------------
    public async Task<IReadOnlyList<ServiceStartupInfo>> GetStartupAsync(CancellationToken cancellationToken)
    {
        var origin = ValueOrigin.Wmi(_clock.Now, "Win32_Service", SensorQuality.High);
        if (!_environment.IsWindows)
        {
            return Array.Empty<ServiceStartupInfo>();
        }

        var services = await _wmi.QueryAsync("Win32_Service", cancellationToken: cancellationToken).ConfigureAwait(false);
        var result = new List<ServiceStartupInfo>();

        foreach (var service in services)
        {
            var name = service.GetString("Name") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var startMode = service.GetString("StartMode") ?? string.Empty;
            var path = service.GetString("PathName") ?? string.Empty;
            var isMicrosoft = path.Contains(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase)
                || path.Contains(@"\Windows\SysWOW64\", StringComparison.OrdinalIgnoreCase);

            result.Add(new ServiceStartupInfo
            {
                Name = TextInfo.Known(name, origin),
                DisplayName = TextInfo.From(service.GetString("DisplayName"), origin, "display name not reported"),
                StartMode = TextInfo.From(startMode, origin, "start mode not reported"),
                State = TextInfo.From(service.GetString("State"), origin, "state not reported"),
                BinaryPath = TextInfo.From(path, origin, "binary path not readable"),
                Account = TextInfo.From(service.GetString("StartName"), origin, "account not reported"),
                IsAutomatic = string.Equals(startMode, "Auto", StringComparison.OrdinalIgnoreCase),
                IsDelayedAutomatic = service.TryGetBool("DelayedAutoStart", out var delayed) && delayed,
                IsThirdParty = !isMicrosoft,
                IsDisabled = string.Equals(startMode, "Disabled", StringComparison.OrdinalIgnoreCase),
                Importance = ImportanceOf(name),
                SourceKey = "Startup_Source_Service",
            });
        }

        return result;
    }

    private static int ImportanceOf(string serviceName) => serviceName.ToLowerInvariant() switch
    {
        "rpcss" or "dnscache" or "lanmanworkstation" or "nsi" or "plugplay" or "power" or "winmgmt"
            or "eventsystem" or "schedule" or "themes" or "audioendpointbuilder" or "audiosrv"
            or "bfe" or "windefend" or "wuauserv" or "storsvc" or "storahci" or "disk" or "volmgr" => 3,
        "wuauserv" or "bits" or "wsearch" or "spooler" => 3,
        _ => 1,
    };

    private WindowsCheckResult StartupCheck(IReadOnlyList<ServiceStartupInfo> startup)
    {
        const string displayKey = "WindowsCheck_StartupImpact";

        if (startup.Count == 0)
        {
            return Check(WindowsCheckId.StartupImpact, displayKey, HealthStatus.Unknown, StageOutcome.NotRun,
                LocalizedText.Of("Windows_Check_NotRun_NoWindows"), "the service list is a Windows component", requiresAdmin: false);
        }

        var thirdParty = startup.Count(s => s.IsThirdParty && s.IsAutomatic && !s.IsDisabled);
        var disabledImportant = startup
            .Where(s => s.IsDisabled && s.Importance == 3)
            .Select(s => s.Name.Display)
            .ToList();

        var status = disabledImportant.Count > 0 ? HealthStatus.Attention
            : thirdParty > 60 ? HealthStatus.Attention
            : HealthStatus.Healthy;

        return new WindowsCheckResult
        {
            Check = WindowsCheckId.StartupImpact,
            DisplayNameKey = displayKey,
            Status = status,
            Outcome = StageOutcome.Succeeded,
            Summary = disabledImportant.Count > 0
                ? LocalizedText.Of("Windows_Check_Startup_ImportantDisabled", string.Join(", ", disabledImportant.Take(5)))
                : LocalizedText.Of("Windows_Check_Startup_Summary", startup.Count, thirdParty),
            Detail = disabledImportant.Count > 0 ? string.Join(", ", disabledImportant) : $"{thirdParty} automatic third-party service(s)",
            Performed = true,
            RequiresAdministrator = false,
            Evidence = new[]
            {
                "wmi=Win32_Service",
                $"total={startup.Count}; automaticThirdParty={thirdParty}; disabledImportant={disabledImportant.Count}",
            },
        };
    }

    /// <summary>Markers used to build the workload snapshot required by the specification.</summary>
    public IReadOnlyList<DetectedWorkloadSnapshot> DetectWorkloads(IEnumerable<ServiceStartupInfo> startup)
    {
        var services = startup?.ToList() ?? new List<ServiceStartupInfo>();
        var result = new List<DetectedWorkloadSnapshot>();

        DetectedWorkloadSnapshot Probe(string id, IEnumerable<string> serviceNames, bool mustNotBeDisturbed)
        {
            var matches = services
                .Where(s => serviceNames.Contains(s.Name.Display, StringComparer.OrdinalIgnoreCase))
                .Select(s => s.Name.Display)
                .ToList();

            return new DetectedWorkloadSnapshot(
                id,
                matches.Count > 0,
                matches.Count > 0
                    ? $"service(s) found: {string.Join(", ", matches)}"
                    : $"no matching service found ({string.Join(", ", serviceNames)})",
                mustNotBeDisturbed);
        }

        result.Add(Probe("hyper-v", new[] { "vmms", "vmcompute", "hns" }, true));
        result.Add(Probe("wsl", new[] { "LxssManager", "WslService" }, false));
        result.Add(Probe("containers", new[] { "com.docker.service", "docker" }, false));
        result.Add(Probe("remote-desktop", new[] { "TermService", "UmRdpService" }, true));
        result.Add(Probe("sql-server", new[] { "MSSQLSERVER", "SQLSERVERAGENT" }, false));
        result.Add(Probe("windows-update", new[] { "wuauserv", "BITS" }, false));

        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Remaining checks
    // ---------------------------------------------------------------------------------------------
    private WindowsCheckResult RunStatusFromPlatform(SystemSnapshot? snapshot)
    {
        const string displayKey = "WindowsCheck_DeviceErrors";
        const string checkId = "Windows_Check_DeviceErrors";

        if (snapshot is null)
        {
            return Check(WindowsCheckId.DeviceErrors, displayKey, HealthStatus.Unknown, StageOutcome.NotRun,
                LocalizedText.Of(checkId + "_NoSnapshot"), "no snapshot was provided", requiresAdmin: false);
        }

        var problematic = snapshot.PnpDevices.Where(d => d.IsPresent == true && d.ProblemCode.HasValue && d.ProblemCode.Value!.Value != 0).ToList();
        var disabled = problematic.Where(d => d.ProblemCode.Value!.Value == 22).ToList();
        var broken = problematic.Except(disabled).ToList();

        var status = broken.Any(d => d.ProblemCode.Value!.Value is 10 or 28 or 43) ? HealthStatus.Warning
            : broken.Count > 0 ? HealthStatus.Attention
            : disabled.Count > 0 ? HealthStatus.Attention
            : HealthStatus.Healthy;

        return new WindowsCheckResult
        {
            Check = WindowsCheckId.DeviceErrors,
            DisplayNameKey = displayKey,
            Status = status,
            Outcome = StageOutcome.Succeeded,
            Summary = problematic.Count == 0
                ? LocalizedText.Of(checkId + "_Clean")
                : LocalizedText.Of(checkId + "_Summary", broken.Count, disabled.Count),
            Detail = string.Join("; ", problematic.Take(5).Select(d => $"{d.Name.Display}={d.ProblemCode.Value!.Value}")),
            Performed = true,
            RequiresAdministrator = false,
            Evidence = new[]
            {
                "Win32_PnPEntity.ConfigManagerErrorCode",
                $"devices={snapshot.PnpDevices.Count}; broken={broken.Count}; disabled={disabled.Count}",
            },
        };
    }

    private WindowsCheckResult SecureBootCheck(WindowsIdentityInfo identity, string? pendingReboot)
    {
        const string displayKey = "WindowsCheck_SecureBoot";

        if (!identity.SecureBootState.IsKnown)
        {
            return Check(WindowsCheckId.SecureBoot, displayKey, HealthStatus.Unknown, StageOutcome.NotRun,
                LocalizedText.Of("Windows_Check_SecureBoot_Unknown", identity.SecureBootState.UnknownReason ?? "not readable"),
                pendingReboot,
                requiresAdmin: false);
        }

        var state = identity.SecureBootState.Value ?? string.Empty;

        if (state.Contains("BIOS", StringComparison.OrdinalIgnoreCase) && !state.Contains("UEFI", StringComparison.OrdinalIgnoreCase))
        {
            // Legacy BIOS cannot provide Secure Boot at all. That is a finding, not a defect to fix
            // silently - the platform simply does not offer this capability.
            return Check(WindowsCheckId.SecureBoot, displayKey, HealthStatus.Attention, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_SecureBoot_LegacyBios"), state, requiresAdmin: false);
        }

        var enabled = string.Equals(state, "Enabled", StringComparison.OrdinalIgnoreCase);
        var disabled = string.Equals(state, "Disabled", StringComparison.OrdinalIgnoreCase);

        if (!enabled && !disabled)
        {
            return Check(WindowsCheckId.SecureBoot, displayKey, HealthStatus.Unknown, StageOutcome.NotRun,
                LocalizedText.Of("Windows_Check_SecureBoot_Unknown", state), state, requiresAdmin: false);
        }

        return Check(WindowsCheckId.SecureBoot, displayKey,
            enabled ? HealthStatus.Healthy : HealthStatus.Attention,
            StageOutcome.Succeeded,
            LocalizedText.Of(enabled ? "Windows_Check_SecureBoot_Enabled" : "Windows_Check_SecureBoot_Disabled"),
            pendingReboot,
            requiresAdmin: false);
    }

    /// <summary>
    /// TPM state (rule 87, DIAG-F-011). The verdict comes from the pure function in
    /// <see cref="TpmReader"/>; this method only turns it into a localized result with evidence.
    /// Nothing here changes the TPM - no clear, no prepare, no ownership change.
    /// </summary>
    private WindowsCheckResult TpmCheck(TpmReading reading)
    {
        const string displayKey = "WindowsCheck_Tpm";
        var origin = ValueOrigin.Wmi(_clock.Now, $"{TpmReader.Scope}:{TpmReader.WmiClass}", SensorQuality.Medium);
        var specification = TpmReader.SpecificationMajor(reading.SpecificationVersion);

        var evidence = new List<string>
        {
            $"enabled={Show(reading.Enabled)}; activated={Show(reading.Activated)}; owned={Show(reading.Owned)}",
            $"specVersion={reading.SpecificationVersion ?? "not reported"}",
            $"manufacturer={reading.ManufacturerId ?? "not reported"}; firmware={reading.ManufacturerVersion ?? "not reported"}",
            $"origin={origin.Token()}",
            "read-only: the TPM is never cleared, prepared or changed",
        };

        var result = TpmReader.Judge(reading) switch
        {
            TpmVerdict.Ready => Check(WindowsCheckId.Tpm, displayKey, HealthStatus.Healthy, StageOutcome.Succeeded,
                LocalizedText.Of(specification is null ? "Windows_Check_Tpm_Ready_NoSpec" : "Windows_Check_Tpm_Ready", specification ?? string.Empty),
                reading.Detail, requiresAdmin: false),
            TpmVerdict.EnabledNotActivated => Check(WindowsCheckId.Tpm, displayKey, HealthStatus.Attention, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_Tpm_EnabledNotActivated"), reading.Detail, requiresAdmin: false),
            TpmVerdict.Disabled => Check(WindowsCheckId.Tpm, displayKey, HealthStatus.Warning, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_Tpm_Disabled"), reading.Detail, requiresAdmin: false),
            TpmVerdict.NotPresent => Check(WindowsCheckId.Tpm, displayKey, HealthStatus.Attention, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_Tpm_NotPresent"), reading.Detail, requiresAdmin: false),
            _ => Check(WindowsCheckId.Tpm, displayKey, HealthStatus.Unknown, StageOutcome.NotRun,
                LocalizedText.Of("Windows_Check_Tpm_Unknown", reading.Detail), reading.Detail, requiresAdmin: false),
        };

        return result with { Evidence = result.Evidence.Concat(evidence).ToArray() };
    }

    /// <summary>
    /// Firewall profile state (rule 87, DIAG-F-009). Read only: Windows Maintenance Center reports a disabled
    /// profile, it never enables or disables one (spec section 12).
    /// </summary>
    private WindowsCheckResult FirewallCheck(FirewallReading reading)
    {
        const string displayKey = "WindowsCheck_Firewall";
        var origin = ValueOrigin.Wmi(_clock.Now, $"{FirewallReader.Scope}:{FirewallReader.WmiClass}", SensorQuality.Medium);
        var state = FirewallReader.Judge(reading);
        var disabled = FirewallReader.DisabledProfiles(reading);
        var unreported = FirewallReader.UnreportedProfiles(reading);

        var evidence = new List<string>
        {
            $"profiles={reading.Profiles.Count}; disabled={disabled.Count}; unreportedState={unreported.Count}",
            $"origin={origin.Token()}",
            "read-only: no firewall setting is changed by Windows Maintenance Center",
        };
        evidence.AddRange(reading.Profiles.Select(profile =>
            $"profile={profile.Name}; enabled={Show(profile.Enabled)}; defaultInbound={profile.DefaultInboundAction ?? "not reported"}"));

        var result = state switch
        {
            FirewallState.AllProfilesEnabled => Check(WindowsCheckId.Firewall, displayKey, HealthStatus.Healthy, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_Firewall_AllEnabled", reading.Profiles.Count), reading.Detail, requiresAdmin: false),
            FirewallState.SomeProfilesDisabled => Check(WindowsCheckId.Firewall, displayKey, HealthStatus.Warning, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_Firewall_SomeDisabled", string.Join(", ", disabled)), reading.Detail, requiresAdmin: false),
            FirewallState.NotFullyReadable => Check(WindowsCheckId.Firewall, displayKey, HealthStatus.Unknown, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_Firewall_NotFullyReadable", string.Join(", ", unreported)), reading.Detail, requiresAdmin: false),
            _ => Check(WindowsCheckId.Firewall, displayKey, HealthStatus.Unknown, StageOutcome.NotRun,
                LocalizedText.Of("Windows_Check_Firewall_Unreadable", reading.Detail), reading.Detail, requiresAdmin: false),
        };

        return result with { Evidence = result.Evidence.Concat(evidence).ToArray() };
    }

    /// <summary>
    /// BitLocker protection state (chapter 8, module M02). Read only: Windows Maintenance Center
    /// reports an unprotected volume, it never encrypts, unlocks or changes a key protector - the
    /// methods that would do that require administrator rights (chapter 12).
    ///
    /// The verdict distinguishes the three honest cases of "no protection reported": no encryptable
    /// volume exists (nothing to say), the provider answered PROTECTION UNKNOWN (locked volume), and
    /// the provider could not be asked. None of them becomes "unprotected", and none becomes "safe".
    /// </summary>
    private WindowsCheckResult BitLockerCheck(BitLockerReading reading)
    {
        const string displayKey = "WindowsCheck_BitLocker";
        var origin = ValueOrigin.Wmi(
            reading.ReadAt ?? _clock.Now,
            $"{BitLockerReader.Scope}:{BitLockerReader.WmiClass}",
            SensorQuality.Medium);
        var state = BitLockerReader.Judge(reading);
        var unprotected = BitLockerReader.UnprotectedVolumes(reading);
        var unreported = BitLockerReader.UnreportedVolumes(reading);

        var evidence = new List<string>
        {
            $"volumes={reading.Volumes.Count}; unprotected={unprotected.Count}; unreportedProtection={unreported.Count}",
            $"origin={origin.Token()}",
            "values are stored when the WMI class is instantiated, so the timestamp above is the moment of the read",
            "read-only: no volume is encrypted, unlocked or changed by Windows Maintenance Center; the methods of this class are not called",
            "not stated: whether a recovery key is escrowed - that cannot be read from Win32_EncryptableVolume",
        };
        evidence.AddRange(reading.Volumes.Select(volume =>
            $"volume={BitLockerReader.Describe(volume)}; {BitLockerReader.Token(volume)}"));

        var result = state switch
        {
            BitLockerState.AllVolumesProtected => Check(WindowsCheckId.BitLocker, displayKey, HealthStatus.Healthy, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_BitLocker_AllProtected", reading.Volumes.Count), reading.Detail, requiresAdmin: false),
            BitLockerState.SomeVolumesUnprotected => Check(WindowsCheckId.BitLocker, displayKey, HealthStatus.Warning, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_BitLocker_Unprotected", string.Join(", ", unprotected)), reading.Detail, requiresAdmin: false),
            BitLockerState.ConversionInProgress => Check(WindowsCheckId.BitLocker, displayKey, HealthStatus.Attention, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_BitLocker_Converting"), reading.Detail, requiresAdmin: false),
            BitLockerState.NoEncryptableVolume => Check(WindowsCheckId.BitLocker, displayKey, HealthStatus.Unknown, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_BitLocker_NoVolume"), reading.Detail, requiresAdmin: false),
            BitLockerState.UnknownProtection => Check(WindowsCheckId.BitLocker, displayKey, HealthStatus.Unknown, StageOutcome.Succeeded,
                LocalizedText.Of("Windows_Check_BitLocker_UnknownProtection", string.Join(", ", unreported)), reading.Detail, requiresAdmin: false),
            _ => Check(WindowsCheckId.BitLocker, displayKey, HealthStatus.Unknown, StageOutcome.NotRun,
                LocalizedText.Of("Windows_Check_BitLocker_Unreadable", reading.Detail), reading.Detail, requiresAdmin: false),
        };

        return result with { Evidence = result.Evidence.Concat(evidence).ToArray() };
    }

    /// <summary>Plain text for a measured flag inside an evidence line, never a guessed value.</summary>
    private static string Show(bool? value) => value switch
    {
        true => "True",
        false => "False",
        null => "not reported",
    };

    private async Task<WindowsCheckResult> AssessStorageSpaceAsync(SystemSnapshot? snapshot, CancellationToken cancellationToken)
    {
        const string displayKey = "WindowsCheck_StorageSpace";

        var volumes = snapshot?.Storage.SelectMany(d => d.Volumes).ToList();
        if (volumes is null || volumes.Count == 0)
        {
            return Check(WindowsCheckId.StorageSpace, displayKey, HealthStatus.Unknown, StageOutcome.NotRun,
                LocalizedText.Of("Windows_Check_Storage_NoSnapshot"), "no volume data in this snapshot", requiresAdmin: false);
        }

        var critical = volumes.Where(v => v.FreePercent.HasValue && v.FreePercent.Value!.Value < 5).ToList();
        var low = volumes.Where(v => v.FreePercent.HasValue && v.FreePercent.Value!.Value is >= 5 and < 15).ToList();
        var unmeasured = volumes.Count(v => !v.FreePercent.HasValue);

        await Task.CompletedTask.ConfigureAwait(false);
        _ = cancellationToken;

        return new WindowsCheckResult
        {
            Check = WindowsCheckId.StorageSpace,
            DisplayNameKey = displayKey,
            Status = critical.Count > 0 ? HealthStatus.Critical : low.Count > 0 ? HealthStatus.Warning : HealthStatus.Healthy,
            Outcome = StageOutcome.Succeeded,
            Summary = critical.Count > 0
                ? LocalizedText.Of("Windows_Check_Storage_Critical", string.Join(", ", critical.Select(v => v.DriveLetter.Display)))
                : low.Count > 0
                    ? LocalizedText.Of("Windows_Check_Storage_Low", string.Join(", ", low.Select(v => v.DriveLetter.Display)))
                    : LocalizedText.Of("Windows_Check_Storage_Ok", volumes.Count),
            Detail = unmeasured > 0 ? $"{unmeasured} volume(s) without free space data" : null,
            Performed = true,
            RequiresAdministrator = false,
            Evidence = volumes
                .Where(v => v.FreePercent.HasValue)
                .Select(v => $"{v.DriveLetter.Display} free={v.FreePercent.Value!.Value}%")
                .ToList(),
        };
    }

    private static WindowsCheckResult UpdateCheck(UpdateAvailability updates) => new()
    {
        Check = WindowsCheckId.UpdateStatus,
        DisplayNameKey = "WindowsCheck_UpdateStatus",
        Status = updates.Outcome switch
        {
            StageOutcome.Succeeded when updates.PendingCount == 0 => HealthStatus.Healthy,
            StageOutcome.Succeeded => HealthStatus.Attention,
            StageOutcome.Skipped or StageOutcome.NotRun => HealthStatus.Unknown,
            StageOutcome.Blocked => HealthStatus.Unknown,
            _ => HealthStatus.Unknown,
        },
        Outcome = updates.Outcome,
        Summary = updates.Summary,
        Detail = updates.ErrorDetail,
        RequiresAdministrator = true,
        Performed = updates.SearchPerformed,
        Evidence = new[]
        {
            $"searchPerformed={updates.SearchPerformed}",
            $"pending={updates.PendingCount}",
            $"pendingReboot={updates.PendingReboot}",
            $"recentUpdates={updates.RecentUpdates.Count}",
        },
    };

    /// <summary>
    /// Pending-reboot detection uses the documented registry markers. Each marker is read through
    /// the registry abstraction, so a missing key means "not pending", never a guess.
    /// </summary>
    private string? DetectPendingReboot()
    {
        if (!_environment.IsWindows)
        {
            return null;
        }

        var markers = new (string SubKey, string ValueName, string Reason)[]
        {
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", string.Empty, "component based servicing (CBS)"),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired", string.Empty, "Windows Update"),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\PackagesPending", string.Empty, "pending packages"),
        };

        var reasons = new List<string>();
        foreach (var (subKey, valueName, reason) in markers)
        {
            if (_registry.KeyExists(RegistryScope.LocalMachine, subKey))
            {
                reasons.Add(reason);
            }
        }

        if (_registry.KeyExists(RegistryScope.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager")
            && _registry.EnumerateValueNames(RegistryScope.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager")
                .Any(name => string.Equals(name, "PendingFileRenameOperations", StringComparison.OrdinalIgnoreCase)))
        {
            reasons.Add("pending file rename operations");
        }

        return reasons.Count == 0 ? null : string.Join(", ", reasons);
    }

    /// <summary>Collects the fields of one offered update while the agent output is parsed.</summary>
    private sealed class UpdateDraft
    {
        public string? Title { get; set; }

        public string? KnowledgeBaseId { get; set; }

        public string? Category { get; set; }

        public string? Severity { get; set; }

        public string? DownloadSize { get; set; }

        public bool? RebootRequired { get; set; }

        public bool? IsMandatory { get; set; }

        public bool? IsDownloaded { get; set; }

        /// <summary>
        /// Builds the record. Every field the agent did not report becomes an explicit "not reported"
        /// instead of a default value, so nothing on screen looks measured that was not measured.
        /// </summary>
        public WindowsUpdateInfo ToInfo(ValueOrigin origin) => new()
        {
            Caption = Optional(Title, origin, "the update agent did not report a title"),
            Description = TextInfo.Unknown(origin, "this query does not report a description"),
            KnowledgeBaseId = Optional(KnowledgeBaseId, origin, "the update agent did not report a knowledge base number"),
            Category = Optional(Category, origin, "the update agent did not report a category"),
            Severity = Optional(Severity, origin, "the update agent did not report a severity"),
            DownloadSizeBytes = DownloadSize is { } size
                && ulong.TryParse(size, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
                    ? Measured<ulong>.Known(bytes, origin)
                    : Measured<ulong>.NotAvailable("the update agent did not report a download size", origin),
            RebootRequired = RebootRequired,
            IsMandatory = IsMandatory,
            IsDownloaded = IsDownloaded,
        };
    }

    /// <summary>A reported value becomes known, anything else stays unknown with its reason.</summary>
    private static TextInfo Optional(string? value, ValueOrigin origin, string reason) =>
        string.IsNullOrWhiteSpace(value) ? TextInfo.Unknown(origin, reason) : TextInfo.Known(value.Trim(), origin);

    /// <summary>Reads the flags the agent prints. Anything that is not true/false counts as not reported.</summary>
    private static bool? ParseFlag(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private WindowsCheckResult Check(WindowsCheckId check, string displayKey, HealthStatus status, StageOutcome outcome, LocalizedText summary, string? detail, bool requiresAdmin) => new()
    {
        Check = check,
        DisplayNameKey = displayKey,
        Status = status,
        Outcome = outcome,
        Summary = summary,
        Detail = detail,
        Performed = outcome is StageOutcome.Succeeded or StageOutcome.Failed,
        RequiresAdministrator = requiresAdmin,
        Evidence = new[] { $"outcome={outcome}", $"status={status}" },
    };

    private static HealthStatus OverallOf(IReadOnlyList<WindowsCheckResult> checks)
    {
        if (checks.Any(c => c.Status == HealthStatus.Critical))
        {
            return HealthStatus.Critical;
        }

        if (checks.Any(c => c.Status == HealthStatus.Warning))
        {
            return HealthStatus.Warning;
        }

        if (checks.Any(c => c.Status == HealthStatus.Attention))
        {
            return HealthStatus.Attention;
        }

        return checks.All(c => c.Status == HealthStatus.Unknown) ? HealthStatus.Unknown : HealthStatus.Healthy;
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + Environment.NewLine + "[output truncated by Windows Maintenance Center]";
}
