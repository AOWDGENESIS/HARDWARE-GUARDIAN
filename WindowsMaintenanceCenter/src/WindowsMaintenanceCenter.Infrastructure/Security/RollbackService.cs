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
/// Rolls a change back using the artefacts of a backup (spec section 26).
///
/// What is automated: registry exports (<c>reg.exe import</c>) and backed up files (copy back).
/// What is NOT automated, and therefore reported as a manual step instead of being faked:
/// a system restore point (interactive, requires a reboot) and a driver/device state
/// (needs <c>pnputil</c> with a device specific decision).
///
/// Success is only reported after verification of every single step.
/// </summary>
public sealed class RollbackService : IRollbackService
{
    private readonly IPathProvider _paths;
    private readonly IProcessRunner _runner;
    private readonly IHashService _hashes;
    private readonly IAuditLog _audit;
    private readonly ILiveProtocol _protocol;
    private readonly IClock _clock;
    private readonly ILogger<RollbackService>? _logger;

    public RollbackService(
        IPathProvider paths,
        IProcessRunner runner,
        IHashService hashes,
        IAuditLog audit,
        ILiveProtocol protocol,
        IClock clock,
        ILogger<RollbackService>? logger = null)
    {
        _paths = paths;
        _runner = runner;
        _hashes = hashes;
        _audit = audit;
        _protocol = protocol;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// All backups that can be found on disk. Read directly from the manifest files, so the list is
    /// always what is really there - not what a previous session believed.
    /// </summary>
    public IReadOnlyList<BackupRecord> Available => LoadManifests().Select(entry => ManifestToRecord(entry.Manifest, entry.Path)).ToList();

    public async Task<bool> CanRollbackAsync(string backupRecordId, CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var entry = FindManifest(backupRecordId);
        return entry is not null && HasRestorableArtefacts(entry.Value.Manifest);
    }

    public async Task<RollbackResult> RollbackAsync(RollbackRequest request, IProgress<ProgressSnapshot>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!OperatingSystem.IsWindows())
        {
            return RollbackResult.NotAvailable(request.BackupRecordId, LocalizedText.Of("Rollback_NotAvailable_NoWindows"));
        }

        var found = FindManifest(request.BackupRecordId);
        if (found is null)
        {
            return RollbackResult.NotAvailable(request.BackupRecordId, LocalizedText.Of("Rollback_NotAvailable_NoManifest"));
        }

        var manifest = found.Value.Manifest;

        // A rollback changes the system, therefore it needs an approval of its own.
        if (request.Approval is null || request.Approval.Decision != ApprovalDecision.Approved)
        {
            return new RollbackResult
            {
                BackupRecordId = request.BackupRecordId,
                Attempted = false,
                Verified = false,
                Outcome = StageOutcome.Blocked,
                Summary = LocalizedText.Of("Rollback_Blocked_NoApproval"),
                Steps = new[] { "approval: missing or not approved" },
                ErrorDetail = BlockReasons.UserRejected,
            };
        }

        if (!HasRestorableArtefacts(manifest))
        {
            return RollbackResult.NotAvailable(request.BackupRecordId, LocalizedText.Of("Rollback_NotAvailable_NoArtefacts"));
        }

        var steps = new List<string>();
        var verification = new List<string>();
        var failed = false;
        var manualRequired = false;
        var index = 0;

        progress?.Report(new ProgressSnapshot
        {
            OperationKey = "Progress_Rollback",
            Module = "ROLLBACK",
            IsRunning = true,
            StartedAt = _clock.Now,
            UpdatedAt = _clock.Now,
            StepKey = "Rollback_Step_Start",
        });

        // 1. Files first: restoring a file cannot depend on a registry state.
        foreach (var copied in manifest.CopiedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report(new ProgressSnapshot
            {
                OperationKey = "Progress_Rollback",
                Module = "ROLLBACK",
                IsRunning = true,
                StartedAt = _clock.Now,
                UpdatedAt = _clock.Now,
                StepKey = "Rollback_Step_File",
                Fraction = Fraction(index, manifest),
            });

            if (!File.Exists(copied))
            {
                steps.Add($"file-restore-skipped: artefact missing ({copied})");
                failed = true;
                continue;
            }

            var target = TargetOf(copied, manifest);
            if (target is null)
            {
                steps.Add($"file-restore-skipped: target not unique or not recorded for {Path.GetFileName(copied)} - manual restore required");
                manualRequired = true;
                continue;
            }

            try
            {
                var targetDirectory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                File.Copy(copied, target, overwrite: true);

                // Verification: the restored file must be byte identical to the backup copy.
                var expected = await _hashes.ComputeFileHashAsync(copied, "SHA256", null, cancellationToken).ConfigureAwait(false);
                var actual = await _hashes.ComputeFileHashAsync(target, "SHA256", null, cancellationToken).ConfigureAwait(false);
                var identical = expected.Succeeded && actual.Succeeded && string.Equals(expected.Hash, actual.Hash, StringComparison.OrdinalIgnoreCase);

                steps.Add(identical ? $"file-restored: {target}" : $"file-restored-unverified: {target}");
                verification.Add($"sha256-match={identical}: {target}");
                failed |= !identical;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                steps.Add($"file-restore-failed: {target} ({ex.GetType().Name}: {ex.Message})");
                failed = true;
            }
        }

        // 2. Registry exports, applied with the documented reg.exe import.
        foreach (var export in manifest.RegistryExports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress?.Report(new ProgressSnapshot
            {
                OperationKey = "Progress_Rollback",
                Module = "ROLLBACK",
                IsRunning = true,
                StartedAt = _clock.Now,
                UpdatedAt = _clock.Now,
                StepKey = "Rollback_Step_Registry",
                Fraction = Fraction(index, manifest),
            });

            if (!File.Exists(export))
            {
                steps.Add($"registry-restore-skipped: artefact missing ({export})");
                failed = true;
                continue;
            }

            var result = await _runner.RunAsync(
                "reg.exe",
                new[] { "import", export },
                new ProcessRunOptions { Timeout = TimeSpan.FromSeconds(120) },
                cancellationToken).ConfigureAwait(false);

            var ok = result.Succeeded;
            steps.Add(ok ? $"registry-imported: {Path.GetFileName(export)}" : $"registry-import-failed: {Path.GetFileName(export)} ({result.ErrorDetail ?? result.StandardError.Trim()})");
            verification.Add($"reg-import-exit={result.ExitCode}: {Path.GetFileName(export)}");
            failed |= !ok;
        }

        // 3. Restore point: documented manual step, never automated here.
        if (!string.IsNullOrWhiteSpace(manifest.RestorePointSequence))
        {
            manualRequired = true;
            steps.Add($"restore-point-manual: sequence={manifest.RestorePointSequence} must be applied through Windows system restore (requires interaction and a restart)");
        }

        // 4. Driver/device state: documented manual step.
        if (!string.IsNullOrWhiteSpace(manifest.DriverSnapshotPath))
        {
            manualRequired = true;
            steps.Add("driver-state-manual: compare with the captured snapshot and roll back the specific device with pnputil after checking the device status");
        }

        progress?.Report(new ProgressSnapshot
        {
            OperationKey = "Progress_Rollback",
            Module = "ROLLBACK",
            IsRunning = false,
            StartedAt = _clock.Now,
            UpdatedAt = _clock.Now,
            StepKey = "Rollback_Step_Done",
            Fraction = 1d,
            Success = !failed,
        });

        var outcome = failed ? StageOutcome.Failed : StageOutcome.Succeeded;
        var summary = failed
            ? LocalizedText.Of("Rollback_Summary_Partial", steps.Count, verification.Count)
            : manualRequired
                ? LocalizedText.Of("Rollback_Summary_AutomatedDoneManualOpen")
                : LocalizedText.Of("Rollback_Summary_Completed", steps.Count);

        await _audit.RecordAsync(
            OperationKind.Rollback,
            request.OperationKey,
            manifest.Category,
            outcome,
            componentId: manifest.ComponentId,
            oldState: $"backup={manifest.Id}",
            newState: failed ? "rollback incomplete" : "rollback applied",
            approval: request.Approval,
            error: failed ? "at least one restore step failed" : null,
            evidence: steps,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _protocol.Report("ROLLBACK", LocalizedText.Of("Rollback_Protocol_Line", manifest.Id), failed ? Severity.Warning : Severity.Success, summary.Key);

        return new RollbackResult
        {
            BackupRecordId = manifest.Id,
            Attempted = true,
            Verified = !failed && verification.Count > 0,
            Outcome = outcome,
            Summary = summary,
            Steps = steps,
            ErrorDetail = failed ? string.Join("; ", steps.Where(s => s.Contains("failed", StringComparison.OrdinalIgnoreCase))) : null,
        };
    }

    private static double? Fraction(int index, BackupManifest manifest)
    {
        var total = manifest.CopiedFiles.Count + manifest.RegistryExports.Count;
        return total == 0 ? null : Math.Min(1d, (double)index / total);
    }

    private static bool HasRestorableArtefacts(BackupManifest manifest) =>
        manifest.RegistryExports.Any(File.Exists) || manifest.CopiedFiles.Any(File.Exists);

    private IEnumerable<(BackupManifest Manifest, string Path)> LoadManifests()
    {
        if (!Directory.Exists(_paths.BackupDirectory))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(_paths.BackupDirectory).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            BackupManifest? manifest = null;
            try
            {
                manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(manifestPath), Serialization.JsonOptions.Default);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Backup manifest {Path} could not be read", manifestPath);
            }

            if (manifest is not null)
            {
                yield return (manifest, manifestPath);
            }
        }
    }

    private (BackupManifest Manifest, string Path)? FindManifest(string backupRecordId)
    {
        foreach (var entry in LoadManifests())
        {
            if (string.Equals(entry.Manifest.Id, backupRecordId, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    private static BackupRecord ManifestToRecord(BackupManifest manifest, string manifestPath) => new()
    {
        Id = manifest.Id,
        OperationId = manifest.OperationId,
        Kind = manifest.Kind,
        Risk = manifest.Risk,
        CreatedAt = manifest.CreatedAt,
        ArtifactPath = Path.GetDirectoryName(manifestPath),
        ManifestPath = manifestPath,
        Summary = LocalizedText.Of(
            "Backup_Record_Summary_Detailed",
            manifest.RegistryExports.Count.ToString(CultureInfo.InvariantCulture),
            manifest.CopiedFiles.Count.ToString(CultureInfo.InvariantCulture)),
        RestoreSteps = manifest.RestoreSteps,
        Evidence = manifest.Evidence,
    };

    /// <summary>
    /// The original location of a backed up file. It is taken from the evidence line
    /// <c>file-backup-ok: &lt;original path&gt;</c> that the backup service wrote. If the match is not
    /// unique, no automatic restore is attempted - guessing a path is not acceptable here.
    /// </summary>
    private static string? TargetOf(string copiedFile, BackupManifest manifest)
    {
        var fileName = Path.GetFileName(copiedFile);
        var candidates = manifest.Evidence
            .Where(e => e.StartsWith("file-backup-ok:", StringComparison.OrdinalIgnoreCase))
            .Select(e => e["file-backup-ok:".Length..].Trim())
            .Where(target => string.Equals(Path.GetFileName(target), fileName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }
}
