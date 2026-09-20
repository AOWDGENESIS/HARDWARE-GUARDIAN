using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Maintenance;

/// <summary>
/// Maintenance engine (spec sections 18, 20, 77 and 78). It implements the mandatory pipeline
/// of the specification literally:
/// SCAN (measure only) -> PLAN (what exactly would change) -> DRY RUN -> APPROVAL ->
/// EXECUTE -> VERIFY. Nothing is deleted without an approved plan, protected categories are never
/// cleaned, and every deletion passes the path guard for the individual file.
/// </summary>
public sealed class MaintenanceService : IMaintenanceService
{
    private const string ModuleKey = "MNT";

    private readonly IPathGuard _pathGuard;
    private readonly IAuditLog _audit;
    private readonly ILiveProtocol _protocol;
    private readonly IProgressReporter _progress;
    private readonly IEnvironmentProbe _environment;
    private readonly ISettingsService _settings;
    private readonly IClock _clock;

    /// <summary>
    /// Dry runs that were really performed, keyed by the locations they covered. It is the evidence
    /// that the mandatory dry run happened for exactly this set of locations: a plan is only
    /// executable when it names a dry run that is still on record. Without this, calling the service
    /// directly (or a future caller) could delete files without ever having shown the user what would
    /// happen - which the specification forbids (sections 44 to 46, 78).
    /// </summary>
    private readonly Dictionary<string, string> _dryRuns = new(StringComparer.Ordinal);
    private readonly object _dryRunGate = new();

    /// <summary>
    /// Backups that were really created, keyed by the locations they cover. The specification puts
    /// BACKUP before USER APPROVAL and EXECUTE (section 44); keeping the record here means a caller
    /// cannot execute a plan that needs securing without having secured it.
    /// </summary>
    private readonly Dictionary<string, string> _backups = new(StringComparer.Ordinal);
    private readonly object _backupGate = new();
    private readonly IBackupService? _backupService;

    public MaintenanceService(
        IPathGuard pathGuard,
        IAuditLog audit,
        ILiveProtocol protocol,
        IProgressReporter progress,
        IEnvironmentProbe environment,
        ISettingsService settings,
        IClock clock,
        IBackupService? backupService = null)
    {
        _pathGuard = pathGuard;
        _audit = audit;
        _protocol = protocol;
        _progress = progress;
        _environment = environment;
        _settings = settings;
        _clock = clock;
        _backupService = backupService;
    }

    public IReadOnlyList<MaintenanceCategoryDescriptor> DescribeCategories() =>
        CleanupTargetCatalog.DescribeCategories()
            .Where(entry => entry.IsOffered)
            .Select(entry => new MaintenanceCategoryDescriptor
            {
                Category = entry.Category,
                DisplayNameKey = entry.NameKey,
                Description = entry.Description,
                SafetyClass = entry.Safety,
                RequiresAdministrator = entry.RequiresAdmin,
                IsOptIn = entry.IsOptIn,
                Restriction = entry.Restriction,
            })
            .ToList();

    // -----------------------------------------------------------------------------------------
    // 1. Scan: measure only, never delete, never modify.
    // -----------------------------------------------------------------------------------------
    public Task<MaintenanceScanResult> ScanAsync(MaintenanceScanOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var started = _clock.Now;
        var settings = _settings.Current;
        var maxDepth = options.MaxDepth <= 0 ? 6 : options.MaxDepth;

        var targets = CleanupTargetCatalog.ForCurrentMachine(
            includeBrowserCache: options.IncludeBrowserCache && settings.MaintenanceIncludeBrowserCache,
            includePrefetch: options.IncludePrefetch && settings.IncludePrefetchedData,
            includeWindowsUpdateCache: options.IncludeWindowsUpdateCache && settings.MaintenanceIncludeWindowsUpdateCache);

        _protocol.Info(ModuleKey, LocalizedText.Of("Maintenance_Scan_Started", targets.Count));

        var items = new List<MaintenanceItem>();
        var problems = new List<Problem>();
        long totalEligible = 0;
        var measuredAnything = false;
        var counter = 0;

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var roots = ResolveRoots(target);
            if (roots.Count == 0)
            {
                items.Add(BuildItem(target, null, "the location could not be resolved on this system (environment folder not available)"));
                continue;
            }

            var notes = new List<string>();
            var totalBytes = 0L;
            var totalFiles = 0;
            long eligibleBytes = 0;
            var eligibleFiles = 0;
            var skipped = 0;
            var exists = false;

            foreach (var root in roots)
            {
                var decision = EvaluateAccess(root, PathGuardIntent.Measure);
                if (!decision.Allowed && decision.IsProtected)
                {
                    notes.Add($"measurement refused by path guard: {decision.ReasonCode} ({decision.Path})");
                }

                // Measuring is read-only; a protected location is still measured so that its size
                // can be reported, but it can never be cleaned (see the protection reason below).
                var measurement = DirectoryMeasurer.Measure(root, target.FilePattern, target.MinimumAge, maxDepth, _clock.Now, cancellationToken);
                exists |= measurement.RootExists;
                totalBytes += measurement.TotalBytes.Value ?? 0;
                totalFiles += measurement.TotalFiles.Value ?? 0;
                eligibleBytes += measurement.EligibleBytes.Value ?? 0;
                eligibleFiles += measurement.EligibleFiles.Value ?? 0;
                skipped += measurement.SkippedCount;
                notes.AddRange(measurement.Notes);
            }

            if (!exists)
            {
                items.Add(BuildItem(target, null, "the location does not exist on this machine"));
                continue;
            }

            measuredAnything = true;
            totalEligible += eligibleBytes;

            if (skipped > 0)
            {
                notes.Add($"{skipped} entry/entries could not be read (in use or access denied)");
            }

            items.Add(BuildItem(target, new DirectoryMeasurement
            {
                RootExists = true,
                TotalBytes = Measured<long>.Known(totalBytes, ValueOrigin.LocalFile(_clock.Now, roots[0])),
                TotalFiles = Measured<int>.Known(totalFiles, ValueOrigin.LocalFile(_clock.Now, roots[0])),
                EligibleBytes = Measured<long>.Known(eligibleBytes, ValueOrigin.LocalFile(_clock.Now, roots[0])),
                EligibleFiles = Measured<int>.Known(eligibleFiles, ValueOrigin.LocalFile(_clock.Now, roots[0])),
                SkippedCount = skipped,
                Notes = notes,
            }, null));

            if (target.SafetyClass == SafetyClass.Protected && eligibleBytes > 0)
            {
                problems.Add(BuildProtectedProblem(target, eligibleBytes, ref counter));
            }
        }

        var duration = _clock.Now - started;
        _protocol.Success(ModuleKey, LocalizedText.Of("Maintenance_Scan_Completed", items.Count), $"{duration.TotalSeconds:0.0} s");

        return Task.FromResult(new MaintenanceScanResult
        {
            Items = items,
            TotalSizeBytes = measuredAnything
                ? Measured<long>.Known(totalEligible, ValueOrigin.LocalFile(_clock.Now, _environment.MachineName))
                : Measured<long>.NotAvailable("no cleanup location could be measured on this machine"),
            ScannedAt = started,
            Duration = duration,
            Problems = problems,
        });
    }

    // -----------------------------------------------------------------------------------------
    // 2. Plan: exactly what would happen, per item, with risk and protection state.
    // -----------------------------------------------------------------------------------------
    public Task<MaintenancePlan> BuildPlanAsync(MaintenanceScanResult scan, IReadOnlyList<MaintenanceCategory> selection, ExecutionMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(selection);

        var selected = new HashSet<MaintenanceCategory>(selection);
        var items = new List<MaintenancePlanItem>();
        long planned = 0;

        foreach (var item in scan.Items.Where(i => selected.Contains(i.Category)))
        {
            var isProtected = item.SafetyClass == SafetyClass.Protected;
            var size = item.SizeBytes.Value ?? 0;
            if (!isProtected)
            {
                planned += size;
            }

            items.Add(new MaintenancePlanItem
            {
                ItemId = item.Id,
                Category = item.Category,
                DisplayNameKey = item.DisplayNameKey,
                SafetyClass = item.SafetyClass,
                RootPath = item.RootPath,
                SizeBytes = item.SizeBytes,
                FileCount = item.FileCount,
                Change = isProtected
                    ? LocalizedText.Of("Maintenance_Change_None", item.DisplayNameKey)
                    : LocalizedText.Of("Maintenance_Change_Delete", item.FileCount.Display, item.SizeBytes.Display),
                Reason = item.ProtectionReason ?? LocalizedText.Of("Maintenance_Reason_Evidence", item.Notes.FirstOrDefault() ?? "measured"),
                Risk = isProtected ? RiskLevel.High : item.SafetyClass == SafetyClass.Optional ? RiskLevel.Medium : RiskLevel.Low,
                IsProtected = isProtected,
                IsSelected = !isProtected,
            });
        }

        var risk = items.Any(i => i.Risk == RiskLevel.High) ? RiskLevel.High
            : items.Any(i => i.Risk == RiskLevel.Medium) ? RiskLevel.Medium
            : items.Count > 0 ? RiskLevel.Low : RiskLevel.Low;

        var plan = new MaintenancePlan
        {
            PlanId = $"MNT-{_clock.Now:yyyyMMdd-HHmmss}",
            Mode = mode,
            // The dry run is looked up by the locations, not by the plan id: the execute plan is a
            // separate object with a separate id, and without this link the execution would carry no
            // evidence that a dry run ever covered these folders.
            DryRunPlanId = mode == ExecutionMode.Execute ? RecordedDryRun(Fingerprint(items)) : null,
            BackupRequired = RequiresBackup(items),
            // Same idea as the dry run: an execution plan carries the record of the backup that
            // covers these locations, so the audit trail shows what was secured before the change.
            BackupRecordId = mode == ExecutionMode.Execute && RequiresBackup(items) ? RecordedBackup(Fingerprint(items)) : null,
            Items = items,
            TotalBytesToFree = planned > 0
                ? Measured<long>.Known(planned, ValueOrigin.LocalFile(_clock.Now, _environment.MachineName))
                : Measured<long>.NotAvailable("nothing selected that may be removed"),
            Risk = risk,
            RequiresAdministrator = items.Any(i => i.IsSelected && scan.Items.FirstOrDefault(s => s.Id == i.ItemId)?.RequiresAdministrator == true),
            CreatedAt = _clock.Now,
            Summary = items.Count == 0
                ? LocalizedText.Of("Maintenance_Plan_Empty")
                : LocalizedText.Of("Maintenance_Plan_Summary", items.Count(i => i.IsSelected), planned / (1024d * 1024d)),
        };

        _protocol.Info(ModuleKey, LocalizedText.Of("Maintenance_Plan_Created", plan.PlanId), $"{items.Count(i => i.IsSelected)} item(s)");
        return Task.FromResult(plan);
    }

    // -----------------------------------------------------------------------------------------
    // 3. Dry run: the same plan, evaluated completely, without a single write.
    // -----------------------------------------------------------------------------------------
    public async Task<MaintenanceResult> ExecuteDryRunAsync(MaintenancePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var started = _clock.Now;
        var results = new List<MaintenanceItemResult>();

        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = item.IsProtected ? StageOutcome.Blocked
                : item.SafetyClass == SafetyClass.Unknown ? StageOutcome.Blocked
                : StageOutcome.Succeeded;

            var message = item.IsProtected
                ? LocalizedText.Of("Maintenance_DryRun_Protected", item.DisplayNameKey)
                : item.SafetyClass == SafetyClass.Unknown
                    ? LocalizedText.Of("Maintenance_DryRun_UnknownCategory", item.DisplayNameKey)
                    : LocalizedText.Of("Maintenance_DryRun_WouldFree", item.SizeBytes.Display, item.FileCount.Display);

            results.Add(new MaintenanceItemResult
            {
                ItemId = item.ItemId,
                Category = item.Category,
                DisplayNameKey = item.DisplayNameKey,
                Outcome = outcome,
                PlannedBytes = item.SizeBytes,
                FreedBytes = Measured<long>.NotAvailable("dry run: nothing was deleted"),
                DeletedFiles = Measured<int>.NotAvailable("dry run: nothing was deleted"),
                SkippedFiles = Measured<int>.NotAvailable("dry run: nothing was deleted"),
                Message = message,
            });
        }

        RememberDryRun(plan);
        _protocol.Info(ModuleKey, LocalizedText.Of("Maintenance_DryRun_Completed"), $"{results.Count(r => r.Outcome == StageOutcome.Succeeded)} item(s) executable");

        await _audit.RecordAsync(
            OperationKind.Maintenance,
            plan.PlanId,
            ComponentCategory.Maintenance,
            StageOutcome.Succeeded,
            oldState: "unchanged",
            newState: "dry-run-only",
            evidence: new[] { "mode=dry-run", $"items={plan.Items.Count}", $"protected={plan.Items.Count(i => i.IsProtected)}" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new MaintenanceResult
        {
            PlanId = plan.PlanId,
            Mode = ExecutionMode.DryRun,
            StartedAt = started,
            CompletedAt = _clock.Now,
            Items = results,
            FreedBytes = Measured<long>.NotAvailable("dry run: nothing was deleted"),
            Summary = LocalizedText.Of("Maintenance_Result_DryRun", plan.Items.Count(i => !i.IsProtected), plan.TotalBytesToFree.Display),
        };
    }

    // -----------------------------------------------------------------------------------------
    // 3b. Backup: the step the specification puts between ANALYSIS and USER APPROVAL.
    // -----------------------------------------------------------------------------------------
    public async Task<BackupRecord> RecordBackupAsync(MaintenancePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.BackupRequired)
        {
            // Returning a record here would claim that something was secured that never had to be.
            throw new OperationBlockedException(
                BlockReasons.BackupRequired,
                LocalizedText.Of("Maintenance_Backup_NotRequired", plan.PlanId),
                "the plan only touches re-creatable locations");
        }

        if (_backupService is null)
        {
            throw new OperationBlockedException(
                BlockReasons.BackupRequired,
                LocalizedText.Of("Maintenance_Backup_Unavailable", plan.PlanId, "no backup service is configured"),
                "no backup service is configured");
        }

        var availability = await _backupService.CheckAvailabilityAsync(plan.Risk, cancellationToken).ConfigureAwait(false);
        if (!availability.IsSufficientFor(plan.Risk))
        {
            throw new OperationBlockedException(
                BlockReasons.BackupRequired,
                LocalizedText.Of("Maintenance_Backup_Unavailable", plan.PlanId, availability.Summary.Key),
                string.Join("; ", availability.Details));
        }

        _progress.Start("Progress_Backup", ModuleKey, 1);
        _protocol.Info(ModuleKey, LocalizedText.Of("Maintenance_Backup_Started", plan.PlanId), _backupService.Location);

        var record = await _backupService.CreateAsync(
            new BackupRequest
            {
                OperationId = plan.PlanId,
                Kind = OperationKind.Maintenance,
                Risk = plan.Risk,
                Category = ComponentCategory.Maintenance,
                Reason = LocalizedText.Of("Maintenance_Change_Delete", plan.Items.Count, plan.TotalBytesToFree.Display),
            },
            null,
            cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(record.Id))
        {
            throw new OperationBlockedException(
                BlockReasons.BackupRequired,
                LocalizedText.Of("Maintenance_Backup_Unavailable", plan.PlanId, "the backup service returned no record"),
                "the backup service returned no record");
        }

        lock (_backupGate)
        {
            _backups[Fingerprint(plan.Items)] = record.Id;
        }

        _protocol.Info(ModuleKey, LocalizedText.Of("Maintenance_Backup_Created", record.Id), record.ArtifactPath ?? _backupService.Location);
        await _audit.RecordAsync(
            OperationKind.Backup,
            plan.PlanId,
            ComponentCategory.Maintenance,
            StageOutcome.Succeeded,
            newState: record.Id,
            evidence: record.Evidence,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return record;
    }

    // -----------------------------------------------------------------------------------------
    // 4. Execute: only with an approved plan, only safe items, every file checked individually.
    // -----------------------------------------------------------------------------------------
    public async Task<MaintenanceResult> ExecuteAsync(MaintenancePlan plan, ApprovalRecord approval, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(approval);

        if (plan.Mode != ExecutionMode.Execute)
        {
            // A plan that was built for a dry run is never executed implicitly.
            return await ExecuteDryRunAsync(plan, cancellationToken).ConfigureAwait(false);
        }

        var started = _clock.Now;
        var results = new List<MaintenanceItemResult>();
        var evidence = new List<string>();
        long freedTotal = 0;
        var deletedTotal = 0;

        var approvalValid = approval.Decision == ApprovalDecision.Approved
            && string.Equals(approval.OperationId, plan.PlanId, StringComparison.Ordinal);

        // The mandatory dry run is enforced here and not only in the user interface: a caller that
        // skips it must not be able to delete anything, even with a valid approval.
        var recordedDryRun = RecordedDryRun(Fingerprint(plan.Items));
        var dryRunValid = plan.DryRunPlanId is not null
            && recordedDryRun is not null
            && string.Equals(plan.DryRunPlanId, recordedDryRun, StringComparison.Ordinal);

        if (approvalValid && !dryRunValid)
        {
            var reason = LocalizedText.Of(
                "Maintenance_Blocked_DryRunRequired",
                plan.DryRunPlanId ?? "none",
                recordedDryRun ?? "none");

            _protocol.Blocked(ModuleKey, LocalizedText.Of("Maintenance_Blocked_Title", plan.PlanId), reason.Key);
            await _audit.RecordAsync(
                OperationKind.Maintenance,
                plan.PlanId,
                ComponentCategory.Maintenance,
                StageOutcome.Blocked,
                approval: approval,
                error: BlockReasons.DryRunRequired,
                evidence: new[] { $"dryRunOnPlan={plan.DryRunPlanId ?? "none"}", $"dryRunOnRecord={recordedDryRun ?? "none"}" },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return new MaintenanceResult
            {
                PlanId = plan.PlanId,
                Mode = plan.Mode,
                StartedAt = started,
                CompletedAt = _clock.Now,
                Items = plan.Items.Select(i => new MaintenanceItemResult
                {
                    ItemId = i.ItemId,
                    Category = i.Category,
                    DisplayNameKey = i.DisplayNameKey,
                    Outcome = StageOutcome.Blocked,
                    PlannedBytes = i.SizeBytes,
                    Message = reason,
                }).ToList(),
                FreedBytes = Measured<long>.NotAvailable($"blocked ({BlockReasons.DryRunRequired}): nothing was deleted"),
                Summary = reason,
            };
        }

        // Backups are enforced in the service as well: without a record that covers exactly these
        // locations nothing is deleted, whatever the caller believes (spec sections 44 and 78).
        var recordedBackup = plan.BackupRequired ? RecordedBackup(Fingerprint(plan.Items)) : null;
        // What counts is a backup on record for exactly these locations. A plan that names a record
        // must name the recorded one; a plan that was built before its backup carries no id yet.
        var backupValid = !plan.BackupRequired
            || (recordedBackup is not null
                && (plan.BackupRecordId is null || string.Equals(plan.BackupRecordId, recordedBackup, StringComparison.Ordinal)));

        if (approvalValid && dryRunValid && !backupValid)
        {
            var reason = plan.BackupRecordId is null
                ? LocalizedText.Of("Maintenance_Blocked_BackupRequired", plan.PlanId, "none")
                : LocalizedText.Of("Maintenance_Blocked_BackupRequired", plan.PlanId, recordedBackup ?? "none");

            _protocol.Blocked(ModuleKey, LocalizedText.Of("Maintenance_Blocked_Title", plan.PlanId), reason.Key);
            await _audit.RecordAsync(
                OperationKind.Maintenance,
                plan.PlanId,
                ComponentCategory.Maintenance,
                StageOutcome.Blocked,
                approval: approval,
                error: BlockReasons.BackupRequired,
                evidence: new[]
                {
                    $"backupRequired=true",
                    $"backupOnPlan={plan.BackupRecordId ?? "none"}",
                    $"backupOnRecord={recordedBackup ?? "none"}",
                    $"risk={plan.Risk}",
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return new MaintenanceResult
            {
                PlanId = plan.PlanId,
                Mode = plan.Mode,
                StartedAt = started,
                CompletedAt = _clock.Now,
                Items = plan.Items.Select(i => new MaintenanceItemResult
                {
                    ItemId = i.ItemId,
                    Category = i.Category,
                    DisplayNameKey = i.DisplayNameKey,
                    Outcome = StageOutcome.Blocked,
                    PlannedBytes = i.SizeBytes,
                    Message = reason,
                }).ToList(),
                FreedBytes = Measured<long>.NotAvailable($"blocked ({BlockReasons.BackupRequired}): nothing was deleted"),
                Summary = reason,
            };
        }

        if (!approvalValid)
        {
            var reason = approval.Decision != ApprovalDecision.Approved
                ? LocalizedText.Of("Maintenance_Blocked_NotApproved", approval.Decision.ToString())
                : LocalizedText.Of("Maintenance_Blocked_ApprovalMismatch", approval.OperationId, plan.PlanId);

            _protocol.Blocked(ModuleKey, LocalizedText.Of("Maintenance_Blocked_Title", plan.PlanId), reason.Key);
            await _audit.RecordAsync(
                OperationKind.Maintenance, plan.PlanId, ComponentCategory.Maintenance, StageOutcome.Blocked,
                approval: approval, error: reason.Key, cancellationToken: cancellationToken).ConfigureAwait(false);

            return new MaintenanceResult
            {
                PlanId = plan.PlanId,
                Mode = plan.Mode,
                StartedAt = started,
                CompletedAt = _clock.Now,
                Items = plan.Items.Select(i => new MaintenanceItemResult
                {
                    ItemId = i.ItemId,
                    Category = i.Category,
                    DisplayNameKey = i.DisplayNameKey,
                    Outcome = StageOutcome.Blocked,
                    PlannedBytes = i.SizeBytes,
                    Message = reason,
                }).ToList(),
                FreedBytes = Measured<long>.NotAvailable("blocked: nothing was deleted"),
                Summary = reason,
            };
        }

        var isElevated = _environment.IsElevated;
        var targetMap = CleanupTargetCatalog.ForCurrentMachine(
            includeBrowserCache: _settings.Current.MaintenanceIncludeBrowserCache,
            includePrefetch: _settings.Current.IncludePrefetchedData,
            includeWindowsUpdateCache: _settings.Current.MaintenanceIncludeWindowsUpdateCache);

        _progress.Start("Progress_Maintenance", ModuleKey, plan.Items.Count);
        var step = 0;

        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            step++;
            _progress.ReportFraction((double)step / Math.Max(1, plan.Items.Count), "Maintenance_Step_Clean", item.DisplayNameKey);

            if (item.IsProtected)
            {
                results.Add(Blocked(item, LocalizedText.Of("Maintenance_Blocked_Protected", item.DisplayNameKey)));
                continue;
            }

            if (item.SafetyClass == SafetyClass.Unknown)
            {
                results.Add(Blocked(item, LocalizedText.Of("Maintenance_Blocked_UnknownCategory", item.DisplayNameKey)));
                continue;
            }

            // Several targets can share one category (for example three WER folders). The plan
            // item identifies its own root, so the executor must match on root + category.
            var target = targetMap.FirstOrDefault(t => t.Category == item.Category
                    && !string.IsNullOrWhiteSpace(item.RootPath)
                    && string.Equals(t.Root, item.RootPath, StringComparison.OrdinalIgnoreCase))
                ?? targetMap.FirstOrDefault(t => t.Category == item.Category);
            if (target is null)
            {
                results.Add(Blocked(item, LocalizedText.Of("Maintenance_Blocked_UnknownTarget", item.Category.ToString())));
                continue;
            }

            if (target.RequiresAdministrator && !isElevated)
            {
                results.Add(Blocked(item, LocalizedText.Of("Maintenance_Blocked_NoAdmin", item.DisplayNameKey), BlockReasons.NotElevated));
                continue;
            }

            var roots = ResolveRoots(target);
            if (roots.Count == 0)
            {
                results.Add(Blocked(item, LocalizedText.Of("Maintenance_Blocked_NoLocation", item.DisplayNameKey)));
                continue;
            }

            var deleted = 0;
            var skippedFiles = 0;
            long freed = 0;
            var notes = new List<string>();

            foreach (var root in roots)
            {
                var files = DirectoryMeasurer.EnumerateFiles(root, target.FilePattern, target.MinimumAge, 6, _clock.Now, cancellationToken);

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Every single file must pass the guard for the concrete root of this target.
                    var decision = _pathGuard.Evaluate(file, PathGuardIntent.Delete, new[] { root });
                    if (!decision.Allowed)
                    {
                        skippedFiles++;
                        if (decision.IsProtected)
                        {
                            notes.Add($"refused by path guard: {decision.ReasonCode}");
                        }

                        continue;
                    }

                    if (!_pathGuard.TryNormalise(file, out var normalised, out _))
                    {
                        skippedFiles++;
                        continue;
                    }

                    // A second, independent containment check on the normalised path.
                    if (!PathGuard.IsStrictlyInside(normalised, root))
                    {
                        skippedFiles++;
                        notes.Add($"containment check failed for {normalised}");
                        continue;
                    }

                    try
                    {
                        var length = new FileInfo(normalised).Length;
                        File.Delete(normalised);
                        deleted++;
                        freed += length;
                        deletedTotal++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skippedFiles++;
                        notes.Add($"{Path.GetFileName(normalised)}: {ex.GetType().Name}");
                    }
                }
            }

            // VERIFY: re-measure the same location and report the observed difference.
            var verification = new List<string>();
            foreach (var root in roots)
            {
                var after = DirectoryMeasurer.Measure(root, target.FilePattern, target.MinimumAge, 6, _clock.Now, cancellationToken);
                verification.Add($"re-measured {root}: {after.EligibleFiles.Display()} eligible file(s), {after.EligibleBytes.Display()} byte");
            }

            freedTotal += freed;
            var outcome = deleted > 0 ? StageOutcome.Succeeded : skippedFiles > 0 ? StageOutcome.Failed : StageOutcome.Skipped;

            results.Add(new MaintenanceItemResult
            {
                ItemId = item.ItemId,
                Category = item.Category,
                DisplayNameKey = item.DisplayNameKey,
                Outcome = outcome,
                PlannedBytes = item.SizeBytes,
                FreedBytes = Measured<long>.Known(freed, ValueOrigin.LocalFile(_clock.Now, roots[0])),
                DeletedFiles = Measured<int>.Known(deleted, ValueOrigin.LocalFile(_clock.Now, roots[0])),
                SkippedFiles = Measured<int>.Known(skippedFiles, ValueOrigin.LocalFile(_clock.Now, roots[0])),
                Message = LocalizedText.Of("Maintenance_Item_Result", deleted, freed / (1024d * 1024d), skippedFiles),
            });

            evidence.Add($"item={item.ItemId}; deleted={deleted}; freed={freed}; skipped={skippedFiles}");
            evidence.AddRange(notes.Take(10));
            evidence.AddRange(verification);

            await _audit.RecordAsync(
                OperationKind.FileDeletion,
                item.ItemId,
                ComponentCategory.Maintenance,
                outcome,
                componentId: item.RootPath,
                oldState: item.SizeBytes.Display,
                newState: $"{freed} byte removed ({deleted} file(s))",
                approval: approval,
                evidence: new[] { $"category={item.Category}", $"deleted={deleted}", $"freed={freed}", $"skipped={skippedFiles}" },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        _progress.Complete(true);

        var overall = results.Any(r => r.Outcome == StageOutcome.Blocked) ? StageOutcome.Blocked
            : results.Any(r => r.Outcome == StageOutcome.Failed) ? StageOutcome.Failed
            : results.Count > 0 ? StageOutcome.Succeeded
            : StageOutcome.Skipped;

        await _audit.RecordAsync(
            OperationKind.Maintenance,
            plan.PlanId,
            ComponentCategory.Maintenance,
            overall,
            approval: approval,
            evidence: evidence.Count > 0 ? evidence : new[] { "no item was executed" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        _protocol.Report(ModuleKey, LocalizedText.Of("Maintenance_Execute_Completed", plan.PlanId), overall == StageOutcome.Succeeded ? Severity.Success : Severity.Warning, $"{deletedTotal} file(s), {freedTotal / (1024 * 1024)} MB");

        return new MaintenanceResult
        {
            PlanId = plan.PlanId,
            Mode = ExecutionMode.Execute,
            StartedAt = started,
            CompletedAt = _clock.Now,
            Items = results,
            FreedBytes = deletedTotal > 0
                ? Measured<long>.Known(freedTotal, ValueOrigin.LocalFile(_clock.Now, _environment.MachineName))
                : Measured<long>.NotAvailable("nothing was deleted"),
            Summary = LocalizedText.Of("Maintenance_Result_Executed", deletedTotal, freedTotal / (1024d * 1024d), results.Count(r => r.Outcome == StageOutcome.Blocked)),
        };
    }

    private static MaintenanceItemResult Blocked(MaintenancePlanItem item, LocalizedText reason, string? reasonCode = null) => new()
    {
        ItemId = item.ItemId,
        Category = item.Category,
        DisplayNameKey = item.DisplayNameKey,
        Outcome = StageOutcome.Blocked,
        PlannedBytes = item.SizeBytes,
        FreedBytes = Measured<long>.NotAvailable("blocked: nothing was deleted"),
        Message = reason,
    };

    private MaintenanceItem BuildItem(CleanupTarget target, DirectoryMeasurement? measurement, string? unavailableReason)
    {
        var notes = new List<string>
        {
            $"source: {target.SourceNote}",
        };

        if (measurement is not null)
        {
            notes.AddRange(measurement.Notes);
        }
        else if (unavailableReason is not null)
        {
            notes.Add(unavailableReason);
        }

        return new MaintenanceItem
        {
            Id = $"{target.Category}-{Math.Abs(target.Root.GetHashCode() % 10000):D4}",
            Category = target.Category,
            SafetyClass = target.SafetyClass,
            DisplayNameKey = target.DisplayNameKey,
            Description = target.Description,
            RootPath = string.IsNullOrWhiteSpace(target.Root) ? null : target.Root,
            SizeBytes = measurement is not null ? measurement.EligibleBytes : Measured<long>.NotAvailable(unavailableReason ?? "not measured"),
            FileCount = measurement is not null ? measurement.EligibleFiles : Measured<int>.NotAvailable(unavailableReason ?? "not measured"),
            RequiresAdministrator = target.RequiresAdministrator,
            IsEnabledByDefault = target.SafetyClass == SafetyClass.Safe && target.IsCleanable,
            ProtectionReasonCode = target.IsCleanable ? null : "PROTECTED_CATEGORY",
            ProtectionReason = target.ProtectionReason,
            Notes = notes,
        };
    }

    private Problem BuildProtectedProblem(CleanupTarget target, long bytes, ref int counter) => new()
    {
        Id = $"{ProblemIdFactory.CategoryPrefix(ComponentCategory.Maintenance)}-{++counter:D3}",
        Category = ComponentCategory.Maintenance,
        Severity = Severity.Info,
        Status = ProblemStatus.Open,
        Title = LocalizedText.Of("Problem_ProtectedLocation_Title", target.DisplayNameKey),
        Description = target.ProtectionReason ?? LocalizedText.Of("Problem_ProtectedLocation_Description"),
        Evidence = $"{target.Root}; eligible bytes={bytes}; source={target.SourceNote}",
        Impact = LocalizedText.Of("Problem_ProtectedLocation_Impact"),
        RecommendedAction = LocalizedText.Of("Problem_ProtectedLocation_Action"),
        DetectedAt = _clock.Now,
        ComponentId = target.Root,
        RequiresAdministrator = target.RequiresAdministrator,
        References = new[] { target.SourceNote },
    };

    private PathGuardDecision EvaluateAccess(string path, PathGuardIntent intent)
    {
        var roots = CleanupTargetCatalog.ForCurrentMachine(includeBrowserCache: true, includePrefetch: true, includeWindowsUpdateCache: true)
            .Select(t => t.Root)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return _pathGuard.Evaluate(path, intent, roots);
    }

    /// <summary>
    /// Resolves the concrete roots of a target, including documented sub-directories such as the
    /// per-profile Firefox cache.
    /// </summary>
    /// <summary>
    /// Identifies the set of locations a plan covers (category, root, safety class). Two plans with
    /// the same fingerprint would touch exactly the same places, so a dry run for one of them covers
    /// the other. Anything else - another folder, another category, another safety class - does not.
    /// </summary>
    /// <summary>Id of the backup that is on record for these locations, or <c>null</c>.</summary>
    private string? RecordedBackup(string fingerprint)
    {
        lock (_backupGate)
        {
            return _backups.TryGetValue(fingerprint, out var id) ? id : null;
        }
    }

    /// <summary>
    /// True when the plan touches locations that must be secured first. Cache and other purely
    /// re-creatable data (SafetyClass.Safe) does not need a backup; optional, protected and unknown
    /// categories do, and a protected item is refused anyway.
    /// </summary>
    private static bool RequiresBackup(IReadOnlyList<MaintenancePlanItem> items) =>
        items.Any(item => item.SafetyClass is SafetyClass.Optional or SafetyClass.Protected or SafetyClass.Unknown);

    private static string Fingerprint(IReadOnlyList<MaintenancePlanItem> items) =>
        string.Join(
            "\n",
            items
                .Select(item => $"{item.Category}|{item.RootPath ?? "-"}|{item.SafetyClass}")
                .OrderBy(entry => entry, StringComparer.Ordinal));

    private void RememberDryRun(MaintenancePlan plan)
    {
        lock (_dryRunGate)
        {
            _dryRuns[Fingerprint(plan.Items)] = plan.PlanId;
        }
    }

    private string? RecordedDryRun(string fingerprint)
    {
        lock (_dryRunGate)
        {
            return _dryRuns.TryGetValue(fingerprint, out var planId) ? planId : null;
        }
    }

    private static IReadOnlyList<string> ResolveRoots(CleanupTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.Root))
        {
            return Array.Empty<string>();
        }

        if (string.IsNullOrWhiteSpace(target.WildcardChildDirectory))
        {
            return new[] { target.Root };
        }

        if (!Directory.Exists(target.Root))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.GetDirectories(target.Root)
                .Select(profile => Path.Combine(profile, target.WildcardChildDirectory))
                .Where(child => Directory.Exists(child))
                .ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Array.Empty<string>();
        }
    }
}

/// <summary>Small helper so that reports can render an unknown measurement without a value.</summary>
internal static class MeasuredFormatting
{
    public static string Display<T>(this Measured<T> measured) where T : struct => measured.HasValue
        ? measured.Value!.ToString() ?? "UNKNOWN"
        : $"UNKNOWN ({measured.UnknownReason})";
}
