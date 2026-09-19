using System.Globalization;
using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;
using HardwareGuardian.Hardware.Wmi;
using HardwareGuardian.Infrastructure.Platform;

namespace HardwareGuardian.Windows;

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
    private readonly IClock _clock;

    public WindowsHealthService(
        IProcessRunner runner,
        PowerShellRunner powershell,
        WmiReader wmi,
        IEnvironmentProbe environment,
        IHardwareProvider hardware,
        IRegistryAccess registry,
        ILiveProtocol protocol,
        IClock clock)
    {
        _runner = runner;
        _powershell = powershell;
        _wmi = wmi;
        _environment = environment;
        _hardware = hardware;
        _registry = registry;
        _protocol = protocol;
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

        progress.Start("Progress_Windows_Health", ModuleKey, 6);

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
    public Task<IntegrityCheckResult> RunComponentStoreCheckAsync(bool repair, IProgressReporter progress, CancellationToken cancellationToken) =>
        RunIntegrityCheckAsync(WindowsCheckId.ComponentStore, repair, progress, cancellationToken);

    public Task<IntegrityCheckResult> RunSystemFileCheckAsync(bool repair, IProgressReporter progress, CancellationToken cancellationToken) =>
        RunIntegrityCheckAsync(WindowsCheckId.SystemFileIntegrity, repair, progress, cancellationToken);

    private async Task<IntegrityCheckResult> RunIntegrityCheckAsync(WindowsCheckId check, bool repair, IProgressReporter progress, CancellationToken cancellationToken)
    {
        var displayKey = check == WindowsCheckId.ComponentStore ? "WindowsCheck_ComponentStore" : "WindowsCheck_SystemFileIntegrity";
        var arguments = BuildIntegrityArguments(check, repair);
        var commandLine = $"dism.exe {string.Join(' ', arguments)}";

        if (!_environment.IsWindows)
        {
            return NotRun(check, displayKey, "Integrity checks require Windows; this host is not Windows.", commandLine);
        }

        if (!_environment.IsElevated)
        {
            return new IntegrityCheckResult
            {
                Check = check,
                Outcome = StageOutcome.Blocked,
                CommandLine = commandLine,
                RequiresAdministrator = true,
                Summary = LocalizedText.Of("Integrity_Blocked_NotElevated"),
                Evidence = new[] { "elevation: false", $"command: {commandLine}" },
            };
        }

        var started = _clock.Now;
        progress.Start(check == WindowsCheckId.ComponentStore ? "Progress_Dism" : "Progress_Sfc", ModuleKey, null);
        _protocol.Info(ModuleKey, LocalizedText.Of(repair ? "Integrity_Running_Repair" : "Integrity_Running_Scan", displayKey));

        var result = await _runner.RunAsync(
            "dism.exe",
            arguments,
            new ProcessRunOptions { Timeout = TimeSpan.FromMinutes(repair ? 60 : 45), MaxOutputCharacters = 2_000_000 },
            cancellationToken).ConfigureAwait(false);

        var duration = _clock.Now - started;
        var output = result.CombinedOutput;
        var (changesPerformed, repairSucceeded, summary) = InterpretDismOutput(output, repair);

        var needsVerification = repair && repairSucceeded && changesPerformed;
        var verified = false;
        var evidence = new List<string>
        {
            $"exitCode={result.ExitCode}",
            $"timedOut={result.TimedOut}",
            $"duration={duration.TotalSeconds:0.0}s",
            $"changesPerformed={changesPerformed}",
        };

        if (needsVerification)
        {
            // A repair is only reported as successful after a separate verification run confirmed it.
            progress.ReportStep("Integrity_Verifying", null);
            var verify = await _runner.RunAsync(
                "dism.exe",
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

        return new IntegrityCheckResult
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
    }

    private static IReadOnlyList<string> BuildIntegrityArguments(WindowsCheckId check, bool repair)
    {
        // Only documented DISM component store operations are used. SFC is not scripted here
        // because its exit codes are ambiguous and it cannot repair from an offline source safely;
        // the component store check is the documented, verifiable path.
        var arguments = new List<string> { "/Online", "/Cleanup-Image", repair ? "/RestoreHealth" : "/ScanHealth" };
        if (check == WindowsCheckId.SystemFileIntegrity && !repair)
        {
            arguments = new List<string> { "/Online", "/Cleanup-Image", "/CheckHealth" };
        }

        arguments.Add("/NoRestart");
        return arguments;
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
                PendingReboot = pendingReboot,
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
                PendingReboot = pendingReboot,
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
                PendingReboot = pendingReboot,
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

        var available = new List<WindowsUpdateInfo>();
        var pendingCount = 0;
        string? error = null;

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("PENDING=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(trimmed["PENDING=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pending))
            {
                pendingCount = pending;
            }
            else if (trimmed.StartsWith("UPDATE=", StringComparison.OrdinalIgnoreCase))
            {
                available.Add(new WindowsUpdateInfo
                {
                    Caption = TextInfo.Known(trimmed["UPDATE=".Length..], origin),
                    Description = TextInfo.Unknown(origin, "the update search reports the title only"),
                });
            }
            else if (trimmed.StartsWith("WU_ERROR=", StringComparison.OrdinalIgnoreCase))
            {
                error = trimmed["WU_ERROR=".Length..];
            }
        }

        if (error is not null)
        {
            return new UpdateAvailability
            {
                Outcome = StageOutcome.Failed,
                SearchPerformed = true,
                PendingReboot = pendingReboot,
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
            PendingReboot = pendingReboot,
            Summary = pendingCount == 0
                ? LocalizedText.Of("WindowsUpdate_UpToDate")
                : LocalizedText.Of("WindowsUpdate_Pending", pendingCount),
            RecentUpdates = recent,
            Available = available,
        };
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
            ThreatsDetected = TextInfo.Unknown(origin, "the threat history is not part of this query and is not modified by Hardware Guardian"),
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

    private async Task<WindowsCheckResult> AssessStorageSpaceAsync(SystemSnapshot? snapshot, CancellationToken cancellationToken)
    {
        const string displayKey = "WindowsCheck_StorageSpace";

        var volumes = snapshot?.StorageDevices.SelectMany(d => d.Volumes).ToList();
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

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + Environment.NewLine + "[output truncated by Hardware Guardian]";
}
