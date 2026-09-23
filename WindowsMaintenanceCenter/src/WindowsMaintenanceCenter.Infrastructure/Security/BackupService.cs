using System.Globalization;
using System.Text.Json;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace WindowsMaintenanceCenter.Infrastructure.Security;

/// <summary>
/// Creates the backup material required before a change (spec section 25):
/// system restore point (only for high/critical risk), registry exports, file copies and a
/// driver/device state snapshot. Everything is written together with a manifest so a rollback
/// can be planned later. The service never claims a backup exists when it failed.
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly IPathProvider _paths;
    private readonly IProcessRunner _processRunner;
    private readonly IClock _clock;
    private readonly IEnvironmentProbe _environment;
    private readonly IBuildInfoProvider? _buildInfo;
    private readonly ILogger<BackupService>? _logger;

    public BackupService(
        IPathProvider paths,
        IProcessRunner processRunner,
        IClock clock,
        IEnvironmentProbe environment,
        IBuildInfoProvider? buildInfo = null,
        ILogger<BackupService>? logger = null)
    {
        _paths = paths;
        _processRunner = processRunner;
        _clock = clock;
        _environment = environment;
        _buildInfo = buildInfo;
        _logger = logger;
    }

    public string Location => _paths.BackupDirectory;

    public async Task<BackupAvailability> CheckAvailabilityAsync(RiskLevel risk, CancellationToken cancellationToken)
    {
        var details = new List<string>();
        var restorePointPossible = false;
        var restorePointAvailable = false;

        if (OperatingSystem.IsWindows())
        {
            var list = await _processRunner.RunAsync(
                ExecutablePath("WindowsPowerShell\\v1.0\\powershell.exe") ?? "powershell.exe",
                RestorePointCountArguments,
                new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(30) },
                cancellationToken).ConfigureAwait(false);

            if (list.Succeeded)
            {
                var line = list.StandardOutput.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("POINTS=", StringComparison.OrdinalIgnoreCase));
                if (line is not null && int.TryParse(line.Trim()["POINTS=".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                {
                    restorePointPossible = true;
                    restorePointAvailable = count > 0;
                    details.Add($"get-computerrestorepoint: {count} point(s) present");
                }
            }
            else
            {
                details.Add($"get-computerrestorepoint failed: {list.ErrorDetail ?? list.StandardError.Trim()}");
                restorePointPossible = InProcessIsWindows();
            }
        }
        else
        {
            details.Add("system restore points are a Windows feature; not available on this host");
        }

        var registryBackupAvailable = OperatingSystem.IsWindows();
        details.Add(registryBackupAvailable ? "reg.exe export available" : "registry export not available");

        var configurationBackupAvailable = true;
        details.Add($"application data root: {_paths.DataRoot}");

        var driverSnapshotAvailable = OperatingSystem.IsWindows();
        details.Add(driverSnapshotAvailable ? "driver/device snapshot via WMI available" : "driver snapshot not available");

        var summaryKey = !restorePointAvailable && risk >= RiskLevel.High
            ? "Backup_Availability_RestorePointMissing"
            : configurationBackupAvailable
                ? "Backup_Availability_Partial"
                : "Backup_Availability_Unknown";

        await Task.CompletedTask.ConfigureAwait(false);

        return new BackupAvailability
        {
            RequiredLevel = risk,
            RestorePointPossible = restorePointPossible,
            RestorePointAvailable = restorePointAvailable,
            ConfigurationBackupAvailable = configurationBackupAvailable,
            RegistryBackupAvailable = registryBackupAvailable,
            DriverSnapshotAvailable = driverSnapshotAvailable,
            Summary = LocalizedText.Of(summaryKey),
            Details = details,
        };
    }

    public async Task<BackupRecord> CreateAsync(BackupRequest request, IProgress<ProgressSnapshot>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = $"BKP-{_clock.Now:yyyyMMdd-HHmmss}-{Sanitise(request.OperationId)}";
        var directory = _paths.EnsureDirectory(Path.Combine(_paths.BackupDirectory, id));
        var evidence = new List<string>();
        var restoreSteps = new List<string>();
        string? restorePointSequence = null;
        var registryExports = new List<string>();
        var copiedPaths = new List<string>();
        string? driverSnapshotPath = null;

        progress?.Report(new ProgressSnapshot { OperationKey = "Progress_Backup", Module = "BACKUP", IsRunning = true, StepKey = "Backup_Step_Start", StartedAt = _clock.Now, UpdatedAt = _clock.Now });

        // 1. System restore point for high and critical risk only.
        if (request.Risk >= RiskLevel.High && OperatingSystem.IsWindows())
        {
            progress?.Report(new ProgressSnapshot { OperationKey = "Progress_Backup", Module = "BACKUP", IsRunning = true, StepKey = "Backup_Step_RestorePoint", StartedAt = _clock.Now, UpdatedAt = _clock.Now });
            var cleanOpId = System.Text.RegularExpressions.Regex.Replace(request.OperationId ?? "Op", @"[^a-zA-Z0-9_\-]", "");
            if (cleanOpId.Length > 20)
            {
                cleanOpId = cleanOpId[..20];
            }
            var rpDescription = $"WMC-{request.Kind}-{cleanOpId}";
            var script = $"try {{ Checkpoint-Computer -Description '{rpDescription}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop; " +
                $"$p = Get-ComputerRestorePoint -ErrorAction Stop | Sort-Object SequenceNumber -Descending | Select-Object -First 1; " +
                $"'RP=' + $p.SequenceNumber + '|' + $p.CreationTime; " +
                $"if ($p.Description -like '*{rpDescription}*') {{ 'RP_VALIDATED=true' }} else {{ 'RP_VALIDATED=false' }} }} " +
                $"catch {{ 'RP_ERROR=' + $_.Exception.Message }}";

            var result = await RunPowerShellAsync(script, TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);

            var line = result.StandardOutput.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("RP=", StringComparison.OrdinalIgnoreCase));
            var isValidated = result.StandardOutput.Contains("RP_VALIDATED=true", StringComparison.OrdinalIgnoreCase);

            if (line is not null)
            {
                restorePointSequence = line.Trim()["RP=".Length..];
                evidence.Add($"restore-point: {restorePointSequence}");
                evidence.Add($"restore-point-description: {rpDescription}");
                evidence.Add($"restore-point-validated: {isValidated}");
                restoreSteps.Add("restore-point: restore through Windows system restore (Systemwiederherstellung)");
            }
            else
            {
                var error = result.StandardOutput.Split('\n').FirstOrDefault(l => l.Contains("RP_ERROR=", StringComparison.OrdinalIgnoreCase))
                    ?? result.ErrorDetail
                    ?? "unknown error";
                evidence.Add($"restore-point-failed: {error.Trim()}");
                _logger?.LogWarning("System restore point could not be created: {Error}", error.Trim());
            }
        }
        else if (request.Risk >= RiskLevel.High)
        {
            evidence.Add("restore-point-skipped: not running on Windows");
        }
        else
        {
            evidence.Add($"restore-point-skipped: risk={request.Risk} does not require one");
        }

        // 2. Registry exports.
        if (request.RegistryKeys.Count > 0 && OperatingSystem.IsWindows())
        {
            progress?.Report(new ProgressSnapshot { OperationKey = "Progress_Backup", Module = "BACKUP", IsRunning = true, StepKey = "Backup_Step_Registry", StartedAt = _clock.Now, UpdatedAt = _clock.Now });
            foreach (var key in request.RegistryKeys)
            {
                var fileName = $"registry-{Sanitise(key)}.reg";
                var target = Path.Combine(directory, fileName);
                var result = await _processRunner.RunAsync(
                    "reg.exe",
                    new[] { "export", key, target, "/y" },
                    new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(60) },
                    cancellationToken).ConfigureAwait(false);

                if (result.Succeeded && File.Exists(target))
                {
                    registryExports.Add(target);
                    restoreSteps.Add($"registry: reg.exe import \"{target}\"");
                    evidence.Add($"registry-export-ok: {key}");
                }
                else
                {
                    evidence.Add($"registry-export-failed: {key} ({result.ErrorDetail ?? result.StandardError.Trim()})");
                }
            }
        }

        // 3. File copies (only inside the application data root or explicitly listed paths).
        foreach (var path in request.Paths)
        {
            if (!File.Exists(path))
            {
                evidence.Add($"file-backup-skipped: {path} does not exist");
                continue;
            }

            try
            {
                var target = Path.Combine(directory, "files", Path.GetFileName(path));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(path, target, overwrite: true);
                copiedPaths.Add(target);
                restoreSteps.Add($"file: copy \"{target}\" back to \"{path}\"");
                evidence.Add($"file-backup-ok: {path}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                evidence.Add($"file-backup-failed: {path} ({ex.GetType().Name}: {ex.Message})");
            }
        }

        // 4. Driver/device state snapshot (a managed snapshot is provided by the caller through
        //    IDriverInventoryService; here we only record that it was requested).
        if (request.CaptureDriverState)
        {
            driverSnapshotPath = Path.Combine(directory, "driver-state-requested.json");
            await JsonFileStore.WriteAsync(driverSnapshotPath, new
            {
                requestedAt = _clock.Now,
                note = "driver state snapshot is captured by IDriverInventoryService and stored next to this manifest",
            }, cancellationToken).ConfigureAwait(false);
            restoreSteps.Add("driver-state: compare against the captured snapshot (pnputil / delete-driver) - manual completion may be required");
            evidence.Add("driver-state-snapshot-requested");
        }

        var manifest = new BackupManifest
        {
            Id = id,
            OperationId = request.OperationId,
            Kind = request.Kind,
            Risk = request.Risk,
            Category = request.Category,
            ComponentId = request.ComponentId,
            CreatedAt = _clock.Now,
            ReasonKey = request.Reason.Key,
            RestorePointSequence = restorePointSequence,
            RegistryExports = registryExports,
            CopiedFiles = copiedPaths,
            DriverSnapshotPath = driverSnapshotPath,
            RestoreSteps = restoreSteps,
            Evidence = evidence,
            ApplicationVersion = _buildInfo?.Get().Version ?? "unknown",
            MachineName = _environment.MachineName,
            UserName = _environment.UserName,
        };

        var manifestPath = Path.Combine(directory, "manifest.json");
        await JsonFileStore.WriteAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);

        progress?.Report(new ProgressSnapshot
        {
            OperationKey = "Progress_Backup",
            Module = "BACKUP",
            IsRunning = false,
            Success = evidence.Any(e => e.StartsWith("registry-export-ok", StringComparison.OrdinalIgnoreCase) || e.StartsWith("file-backup-ok", StringComparison.OrdinalIgnoreCase) || e.StartsWith("restore-point:", StringComparison.OrdinalIgnoreCase)) || evidence.Count > 0 && !evidence.Any(e => e.Contains("failed", StringComparison.OrdinalIgnoreCase)),
            StepKey = "Backup_Step_Done",
            StartedAt = _clock.Now,
            UpdatedAt = _clock.Now,
            Fraction = 1d,
        });

        return new BackupRecord
        {
            Id = id,
            OperationId = request.OperationId,
            Kind = request.Kind,
            Risk = request.Risk,
            CreatedAt = _clock.Now,
            ArtifactPath = directory,
            ManifestPath = manifestPath,
            Summary = LocalizedText.Of("Backup_Record_Summary_Detailed", registryExports.Count, copiedPaths.Count),
            RestoreSteps = restoreSteps,
            Evidence = evidence,
        };
    }

    public async Task<BackupRecord?> FindAsync(string recordId, CancellationToken cancellationToken)
    {
        var all = await ListAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(r => string.Equals(r.Id, recordId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<BackupRecord>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.BackupDirectory))
        {
            return Array.Empty<BackupRecord>();
        }

        var records = new List<BackupRecord>();
        foreach (var directory in Directory.EnumerateDirectories(_paths.BackupDirectory).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var manifest = await JsonFileStore.ReadAsync<BackupManifest>(manifestPath, cancellationToken).ConfigureAwait(false);
                if (manifest is null)
                {
                    continue;
                }

                records.Add(new BackupRecord
                {
                    Id = manifest.Id,
                    OperationId = manifest.OperationId,
                    Kind = manifest.Kind,
                    Risk = manifest.Risk,
                    CreatedAt = manifest.CreatedAt,
                    ArtifactPath = directory,
                    ManifestPath = manifestPath,
                    Summary = LocalizedText.Of("Backup_Record_Summary_Detailed", manifest.RegistryExports.Count, manifest.CopiedFiles.Count),
                    RestoreSteps = manifest.RestoreSteps,
                    Evidence = manifest.Evidence,
                });
            }
            catch (JsonException)
            {
                // Ignore unreadable manifests; they are reported by the caller as missing backups.
            }
        }

        return records;
    }

    private static bool InProcessIsWindows() => OperatingSystem.IsWindows();

    private static string? ExecutablePath(string relativeUnderWindowsFolder)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return string.IsNullOrEmpty(windows) ? null : Path.Combine(windows, relativeUnderWindowsFolder);
    }

    private async Task<ProcessResult> RunPowerShellAsync(string script, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var executable = ExecutablePath(@"WindowsPowerShell\v1.0\powershell.exe") ?? "powershell.exe";
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        return await _processRunner.RunAsync(
            executable,
            new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded },
            new ProcessRunOptions { Timeout = timeout },
            cancellationToken).ConfigureAwait(false);
    }

        /// <summary>
    /// The argument vector of the restore point query. It is a constant, so it is written once and
    /// not rebuilt on every call.
    /// </summary>
    private static readonly string[] RestorePointCountArguments =
    {
        "-NoProfile", "-NonInteractive", "-Command",
        "$p = Get-ComputerRestorePoint -ErrorAction SilentlyContinue; if ($p) { 'POINTS=' + ($p | Measure-Object).Count } else { 'POINTS=0' }",
    };

    private static string Sanitise(string value) => new((value ?? "op").Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());

}
