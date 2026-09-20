using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Maintenance;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// Maintenance safety (spec sections 44 to 46 and 77). These are the tests that matter most: a dry
/// run must never delete, and an execution without a matching approval must never delete either.
/// </summary>
public sealed class MaintenanceSafetyTests : IDisposable
{
    private readonly FakeClock _clock = new();
    private readonly TempPathProvider _paths = new();
    private readonly FakeEnvironmentProbe _environment = new();
    private readonly FakeSettingsService _settings = new();
    private readonly MaintenanceService _service;
    private readonly string _cleanupRoot;
    private readonly List<string> _files = new();

    public MaintenanceSafetyTests()
    {
        _service = new MaintenanceService(
            new PathGuard(),
            new AuditLogService(new InMemoryAuditSink(), _clock, _environment),
            new LiveProtocol(_clock),
            new ProgressReporter(_clock),
            _environment,
            _settings,
            _clock);

        _cleanupRoot = Path.Combine(_paths.DataRoot, "temp-target");
        Directory.CreateDirectory(_cleanupRoot);

        for (var i = 1; i <= 3; i++)
        {
            var file = Path.Combine(_cleanupRoot, $"cache-{i}.tmp");
            File.WriteAllText(file, new string('x', 2048));
            _files.Add(file);
        }
    }

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void All_files_still_exist_before_anything_runs()
    {
        Assert.All(_files, file => Assert.True(File.Exists(file)));
    }

    [Fact]
    public async Task Dry_run_reports_but_deletes_nothing()
    {
        var plan = await _service.BuildPlanAsync(await ScanAsync(), new[] { MaintenanceCategory.TemporaryFiles }, ExecutionMode.DryRun, CancellationToken.None);
        var result = await _service.ExecuteDryRunAsync(plan, CancellationToken.None);

        Assert.Equal(ExecutionMode.DryRun, result.Mode);
        Assert.True(result.WasNothingDeleted);
        Assert.All(_files, file => Assert.True(File.Exists(file), $"{file} was deleted by a dry run"));
    }

    [Fact]
    public async Task Rejected_approval_blocks_the_execution_and_keeps_the_files()
    {
        await DryRunAsync();
        var plan = await ExecutePlanAsync();
        var rejected = Approval(plan, ApprovalDecision.Rejected);

        var result = await _service.ExecuteAsync(plan, rejected, CancellationToken.None);

        Assert.Equal(StageOutcome.Blocked, Assert.Single(result.Items).Outcome);
        Assert.All(_files, file => Assert.True(File.Exists(file), $"{file} was deleted without approval"));
    }

    [Fact]
    public async Task Approval_for_a_different_plan_blocks_the_execution()
    {
        await DryRunAsync();
        var plan = await ExecutePlanAsync();
        var mismatched = Approval(plan, ApprovalDecision.Approved) with { OperationId = "PLAN-OTHER" };

        var result = await _service.ExecuteAsync(plan, mismatched, CancellationToken.None);

        Assert.Equal(StageOutcome.Blocked, Assert.Single(result.Items).Outcome);
        Assert.All(_files, file => Assert.True(File.Exists(file), $"{file} was deleted with a foreign approval"));
    }

    [Fact]
    public async Task A_dry_run_plan_is_never_executed_even_when_approved()
    {
        var dryPlan = await _service.BuildPlanAsync(await ScanAsync(), new[] { MaintenanceCategory.TemporaryFiles }, ExecutionMode.DryRun, CancellationToken.None);
        var approved = Approval(dryPlan, ApprovalDecision.Approved);

        var result = await _service.ExecuteAsync(dryPlan, approved, CancellationToken.None);

        // The service falls back to the dry run instead of executing: nothing is deleted.
        Assert.Equal(ExecutionMode.DryRun, result.Mode);
        Assert.True(result.WasNothingDeleted);
        Assert.All(_files, file => Assert.True(File.Exists(file)));
    }

    [Fact]
    public async Task Protected_categories_are_marked_high_risk_and_never_executed()
    {
        var scan = new MaintenanceScanResult
        {
            ScannedAt = _clock.Now,
            Items = new[] { Item(MaintenanceCategory.WindowsUpdateCache, SafetyClass.Protected) },
        };

        var plan = await _service.BuildPlanAsync(scan, new[] { MaintenanceCategory.WindowsUpdateCache }, ExecutionMode.Execute, CancellationToken.None);
        var item = Assert.Single(plan.Items);

        Assert.True(item.IsProtected);
        Assert.Equal(RiskLevel.High, item.Risk);
        Assert.False(item.IsSelected);

        var dry = await _service.ExecuteDryRunAsync(plan, CancellationToken.None);
        Assert.Equal(StageOutcome.Blocked, Assert.Single(dry.Items).Outcome);
    }

    [Fact]
    public async Task An_approved_plan_never_touches_files_outside_the_allow_list()
    {
        // The documented order: SCAN -> PLAN -> DRY RUN -> APPROVAL -> EXECUTE -> VERIFY.
        await DryRunAsync();
        var plan = await ExecutePlanAsync();
        var approved = Approval(plan, ApprovalDecision.Approved);

        var result = await _service.ExecuteAsync(plan, approved, CancellationToken.None);

        // The approval is honoured (the run executes), but the execution resolves its roots from the
        // cleanup catalogue - never from the plan. Files that are not inside an allowed root must
        // survive, which is the containment rule of the path guard.
        Assert.Equal(ExecutionMode.Execute, result.Mode);
        Assert.Single(result.Items);
        Assert.All(_files, file => Assert.True(File.Exists(file), $"{file} was deleted outside the allow list"));
    }

    [Fact]
    public async Task Executing_without_a_dry_run_is_blocked_and_deletes_nothing()
    {
        // A perfectly valid approval is not enough: the mandatory dry run has to be on record for
        // exactly these locations. This is the rule a direct caller of the service could otherwise
        // bypass, and it is why the check lives in the service and not only in the user interface.
        var plan = await ExecutePlanAsync();
        var approved = Approval(plan, ApprovalDecision.Approved);

        var result = await _service.ExecuteAsync(plan, approved, CancellationToken.None);

        Assert.Equal(StageOutcome.Blocked, Assert.Single(result.Items).Outcome);
        Assert.False(result.FreedBytes.HasValue);
        Assert.All(_files, file => Assert.True(File.Exists(file), $"'{file}' was deleted without a dry run"));
    }

    [Fact]
    public async Task A_plan_that_needs_a_backup_is_blocked_without_one()
    {
        // An optional category is not lost data, but it is also not re-creatable without cost, so
        // the specification puts BACKUP before APPROVAL and EXECUTE (section 44).
        var scan = OptionalScan();
        await DryRunAsync(MaintenanceCategory.WindowsUpdateCache, scan);

        var plan = await _service.BuildPlanAsync(scan, new[] { MaintenanceCategory.WindowsUpdateCache }, ExecutionMode.Execute, CancellationToken.None);
        Assert.True(plan.BackupRequired);

        var result = await _service.ExecuteAsync(plan, Approval(plan, ApprovalDecision.Approved), CancellationToken.None);

        Assert.Equal(StageOutcome.Blocked, Assert.Single(result.Items).Outcome);
        Assert.All(_files, file => Assert.True(File.Exists(file), $"'{file}' was deleted without a backup"));
    }

    [Fact]
    public async Task A_recorded_backup_makes_the_execution_possible()
    {
        var backups = new RecordingBackupService(_clock);
        var service = ServiceWith(backups);

        var scan = OptionalScan();
        await DryRunAsync(MaintenanceCategory.WindowsUpdateCache, scan, service);

        var plan = await service.BuildPlanAsync(scan, new[] { MaintenanceCategory.WindowsUpdateCache }, ExecutionMode.Execute, CancellationToken.None);
        var record = await service.RecordBackupAsync(plan, CancellationToken.None);

        Assert.Contains(plan.PlanId, backups.OperationIds);

        // The plan is rebuilt after the backup, exactly like the view model does it, so the plan
        // names the record that now exists.
        plan = await service.BuildPlanAsync(scan, new[] { MaintenanceCategory.WindowsUpdateCache }, ExecutionMode.Execute, CancellationToken.None);
        Assert.Equal(record.Id, plan.BackupRecordId);

        var result = await service.ExecuteAsync(plan, Approval(plan, ApprovalDecision.Approved), CancellationToken.None);

        Assert.Equal(ExecutionMode.Execute, result.Mode);
    }

    [Fact]
    public async Task A_backup_for_other_locations_does_not_authorise_the_execution()
    {
        var backups = new RecordingBackupService(_clock);
        var service = ServiceWith(backups);

        var otherRoot = Path.Combine(_paths.DataRoot, "other-target");
        Directory.CreateDirectory(otherRoot);
        File.WriteAllText(Path.Combine(otherRoot, "cache-other.tmp"), new string('y', 512));

        // A backup is on record, but for different locations.
        var otherScan = new MaintenanceScanResult
        {
            ScannedAt = _clock.Now,
            Items = new[] { Item(MaintenanceCategory.WindowsUpdateCache, SafetyClass.Optional, otherRoot) },
        };
        await DryRunAsync(MaintenanceCategory.WindowsUpdateCache, otherScan, service);
        var otherPlan = await service.BuildPlanAsync(otherScan, new[] { MaintenanceCategory.WindowsUpdateCache }, ExecutionMode.Execute, CancellationToken.None);
        await service.RecordBackupAsync(otherPlan, CancellationToken.None);

        var scan = OptionalScan();
        await DryRunAsync(MaintenanceCategory.WindowsUpdateCache, scan, service);
        var plan = await service.BuildPlanAsync(scan, new[] { MaintenanceCategory.WindowsUpdateCache }, ExecutionMode.Execute, CancellationToken.None);

        var result = await service.ExecuteAsync(plan, Approval(plan, ApprovalDecision.Approved), CancellationToken.None);

        Assert.Equal(StageOutcome.Blocked, Assert.Single(result.Items).Outcome);
        Assert.All(_files, file => Assert.True(File.Exists(file), $"'{file}' was deleted although the backup covered other locations"));
    }

    [Fact]
    public async Task Without_a_backup_service_nothing_that_needs_a_backup_is_executed()
    {
        // The default service in the constructor has no backup service at all. Fail closed.
        var scan = OptionalScan();
        await DryRunAsync(MaintenanceCategory.WindowsUpdateCache, scan);

        var plan = await _service.BuildPlanAsync(scan, new[] { MaintenanceCategory.WindowsUpdateCache }, ExecutionMode.Execute, CancellationToken.None);
        await Assert.ThrowsAsync<OperationBlockedException>(
            () => _service.RecordBackupAsync(plan, CancellationToken.None));
    }

    [Fact]
    public async Task A_plan_that_needs_no_backup_is_told_so_instead_of_getting_a_record()
    {
        var backups = new RecordingBackupService(_clock);
        var service = ServiceWith(backups);
        var plan = await service.BuildPlanAsync(await ScanAsync(), new[] { MaintenanceCategory.TemporaryFiles }, ExecutionMode.Execute, CancellationToken.None);

        Assert.False(plan.BackupRequired);
        await Assert.ThrowsAsync<OperationBlockedException>(
            () => service.RecordBackupAsync(plan, CancellationToken.None));
        Assert.Empty(backups.OperationIds);
    }

    [Fact]
    public async Task A_dry_run_for_other_locations_does_not_authorise_the_execution()
    {
        // The dry run must cover what is executed. A dry run for a different folder leaves the
        // execution blocked, even though a dry run happened at some point.
        await DryRunAsync();

        var otherRoot = Path.Combine(_paths.DataRoot, "other-target");
        Directory.CreateDirectory(otherRoot);
        var otherFile = Path.Combine(otherRoot, "cache-other.tmp");
        File.WriteAllText(otherFile, new string('y', 1024));

        var scan = new MaintenanceScanResult
        {
            ScannedAt = _clock.Now,
            Items = new[] { Item(MaintenanceCategory.TemporaryFiles, SafetyClass.Safe, otherRoot) },
            TotalSizeBytes = Measured<long>.Known(1024, ValueOrigin.LocalFile(_clock.Now, otherRoot)),
        };

        var plan = await _service.BuildPlanAsync(scan, new[] { MaintenanceCategory.TemporaryFiles }, ExecutionMode.Execute, CancellationToken.None);
        var result = await _service.ExecuteAsync(plan, Approval(plan, ApprovalDecision.Approved), CancellationToken.None);

        Assert.Equal(StageOutcome.Blocked, Assert.Single(result.Items).Outcome);
        Assert.True(File.Exists(otherFile), "a dry run for another folder authorised a deletion");
    }

    [Fact]
    public async Task A_dry_run_makes_the_execution_possible_and_records_the_link()
    {
        var dryPlan = await DryRunAsync();
        var plan = await ExecutePlanAsync();

        Assert.Equal(dryPlan.PlanId, plan.DryRunPlanId);

        var result = await _service.ExecuteAsync(plan, Approval(plan, ApprovalDecision.Approved), CancellationToken.None);

        Assert.Equal(ExecutionMode.Execute, result.Mode);
        Assert.NotEqual(StageOutcome.Blocked, Assert.Single(result.Items).Outcome);
    }

    [Fact]
    public async Task Only_selected_categories_are_planned()
    {
        var scan = await ScanAsync();
        var plan = await _service.BuildPlanAsync(scan, new[] { MaintenanceCategory.TemporaryFiles }, ExecutionMode.DryRun, CancellationToken.None);

        Assert.All(plan.Items, item => Assert.Equal(MaintenanceCategory.TemporaryFiles, item.Category));
    }

    private MaintenanceItem Item(MaintenanceCategory category, SafetyClass safetyClass, string? root = null) => new()
    {
        Id = category.ToString(),
        Category = category,
        SafetyClass = safetyClass,
        DisplayNameKey = "Maintenance_TemporaryFiles_Name",
        Description = LocalizedText.Of("Maintenance_TemporaryFiles_Description"),
        RootPath = root ?? _cleanupRoot,
        SizeBytes = Measured<long>.Known(6144, ValueOrigin.LocalFile(_clock.Now, root ?? _cleanupRoot)),
        FileCount = Measured<int>.Known(_files.Count, ValueOrigin.LocalFile(_clock.Now, _cleanupRoot)),
        IsEnabledByDefault = safetyClass == SafetyClass.Safe,
        Notes = new[] { "synthetic test item" },
    };

    private Task<MaintenanceScanResult> ScanAsync() => Task.FromResult(new MaintenanceScanResult
    {
        ScannedAt = _clock.Now,
        Items = new[] { Item(MaintenanceCategory.TemporaryFiles, SafetyClass.Safe) },
        TotalSizeBytes = Measured<long>.Known(6144, ValueOrigin.LocalFile(_clock.Now, _cleanupRoot)),
    });

    /// <summary>Runs the dry run of the documented pipeline and returns the plan it covered.</summary>
    private async Task<MaintenancePlan> DryRunAsync()
    {
        var plan = await _service.BuildPlanAsync(
            await ScanAsync(),
            new[] { MaintenanceCategory.TemporaryFiles },
            ExecutionMode.DryRun,
            CancellationToken.None);

        await _service.ExecuteDryRunAsync(plan, CancellationToken.None);
        return plan;
    }

    /// <summary>Runs the dry run for a category of a prepared scan.</summary>
    private async Task<MaintenancePlan> DryRunAsync(
        MaintenanceCategory category,
        MaintenanceScanResult scan,
        MaintenanceService? service = null)
    {
        var target = service ?? _service;
        var plan = await target.BuildPlanAsync(scan, new[] { category }, ExecutionMode.DryRun, CancellationToken.None);
        await target.ExecuteDryRunAsync(plan, CancellationToken.None);
        return plan;
    }

    /// <summary>Service instance with a backup service, for the backup gate.</summary>
    private MaintenanceService ServiceWith(IBackupService backups) => new(
        new PathGuard(),
        new AuditLogService(new InMemoryAuditSink(), _clock, _environment),
        new LiveProtocol(_clock),
        new ProgressReporter(_clock),
        _environment,
        _settings,
        _clock,
        backups);

    /// <summary>A scan whose single item is an optional category (backup required, not protected).</summary>
    private MaintenanceScanResult OptionalScan() => new()
    {
        ScannedAt = _clock.Now,
        Items = new[] { Item(MaintenanceCategory.WindowsUpdateCache, SafetyClass.Optional) },
        TotalSizeBytes = Measured<long>.Known(6144, ValueOrigin.LocalFile(_clock.Now, _cleanupRoot)),
    };

    private async Task<MaintenancePlan> ExecutePlanAsync() => await _service.BuildPlanAsync(
        await ScanAsync(),
        new[] { MaintenanceCategory.TemporaryFiles },
        ExecutionMode.Execute,
        CancellationToken.None);

    private ApprovalRecord Approval(MaintenancePlan plan, ApprovalDecision decision) => new()
    {
        RequestId = "REQ-1",
        OperationId = plan.PlanId,
        Decision = decision,
        DecidedAt = _clock.Now,
        Risk = plan.Risk,
        WasRequired = true,
    };
}
