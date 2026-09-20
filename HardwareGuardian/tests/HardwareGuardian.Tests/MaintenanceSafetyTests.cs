using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Services;
using HardwareGuardian.Core.Values;
using HardwareGuardian.Maintenance;
using Xunit;

namespace HardwareGuardian.Tests;

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
