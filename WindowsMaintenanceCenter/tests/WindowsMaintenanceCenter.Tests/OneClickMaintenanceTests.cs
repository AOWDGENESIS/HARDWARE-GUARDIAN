using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// One-click maintenance (chapter 33, module M27). What these tests pin down is not "the phase list
/// has eight entries" but the rules that make the module trustworthy:
///
/// * the sequence is the specified one and every phase states what, why and how risky it is,
/// * a category the user deselected never ends up in the plan,
/// * nothing is executed without an approval for exactly this plan,
/// * before/after is measured and a missing reading stays UNKNOWN instead of becoming a zero,
/// * a cancelled or blocked run is reported as such - never as a success,
/// * the run announces its operation, so an interruption leaves something the recovery engine can find.
///
/// The doubles replace the maintenance engine, the approval service and the report generator, so the
/// checks are about the conductor's decisions and run on every platform.
/// </summary>
public sealed class OneClickMaintenanceTests
{
    private readonly FakeClock _clock = new();
    private readonly RecordingLiveProtocol _protocol;
    private readonly RecordingAuditLog _audit = new();
    private readonly FakeMaintenanceService _maintenance = new();
    private readonly FakeScanOrchestrator _scanner = new();
    private readonly FakeApprovalService _approvals = new();
    private readonly FakeReportGenerator _reports = new();
    private readonly FakeElevationService _elevation = new();
    private readonly SystemStateMachine _state;
    private readonly OneClickMaintenanceService _service;

    public OneClickMaintenanceTests()
    {
        _protocol = new RecordingLiveProtocol();
        _state = new SystemStateMachine(_clock);
        _service = new OneClickMaintenanceService(
            _state,
            _scanner,
            _maintenance,
            _approvals,
            _audit,
            _reports,
            _protocol,
            _elevation,
            _clock);
    }

    [Fact]
    public async Task The_eight_phases_run_in_the_specified_order()
    {
        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        var phases = result.Steps.Where(step => step.IsPhaseStep).Select(step => step.Phase).ToArray();

        Assert.Equal(
            new[]
            {
                OneClickPhase.Discovery,
                OneClickPhase.Diagnostic,
                OneClickPhase.Plan,
                OneClickPhase.Approval,
                OneClickPhase.Backup,
                OneClickPhase.Execution,
                OneClickPhase.Validation,
                OneClickPhase.Report,
            },
            phases);
    }

    [Fact]
    public async Task Every_phase_states_what_it_does_why_it_runs_and_its_risk()
    {
        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        Assert.NotEmpty(result.Steps);
        Assert.All(result.Steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.What.Key));
            Assert.False(string.IsNullOrWhiteSpace(step.Why.Key));
            Assert.NotNull(step.Risk);
            Assert.NotEqual(StageOutcome.Running, step.Outcome);
            Assert.NotNull(step.Detail);
        });

        // Reading is low risk, changing the system never is. That is the statement the user sees.
        Assert.All(
            result.Steps.Where(step => step.Phase is OneClickPhase.Execution or OneClickPhase.Backup),
            step => Assert.True(step.Risk >= RiskLevel.High, $"{step.Phase} is change work and must not be reported as low risk"));
    }

    [Fact]
    public async Task The_run_moves_the_state_machine_through_the_specified_states()
    {
        await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        // The journal is what the recovery engine reads after a crash. It has to name the phases, not
        // just the end: a run that only recorded SUCCESS could not be told apart from a silent no-op.
        var states = _state.History.Select(change => change.Current).ToArray();

        Assert.Contains(SystemState.Discovery, states);
        Assert.Contains(SystemState.Diagnostic, states);
        Assert.Contains(SystemState.PlanGenerated, states);
        Assert.Contains(SystemState.AwaitingApproval, states);
        Assert.Contains(SystemState.Executing, states);
        Assert.Contains(SystemState.Validating, states);
        Assert.Equal(SystemState.Success, states[^1]);
    }

    [Fact]
    public async Task Every_phase_step_reaches_the_live_protocol()
    {
        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        var phaseCount = result.Steps.Count(step => step.IsPhaseStep);
        Assert.Equal(8, phaseCount);
        Assert.Equal(phaseCount, _protocol.Entries.Count);
    }

    [Fact]
    public async Task Nothing_is_executed_when_the_approval_is_refused()
    {
        _approvals.Outcome = ApprovalDecision.Rejected;

        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        Assert.Equal(SystemState.Blocked, result.FinalState);
        Assert.Equal(0, _maintenance.ExecuteCalls);
        Assert.Equal(0, _maintenance.BackupCalls);
        Assert.Null(result.Execution);
        Assert.Contains(result.OpenPoints, point => point.Contains("not approved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Plan_only_stops_after_the_plan_and_changes_nothing()
    {
        var result = await _service.RunAsync(new OneClickRequest { PlanOnly = true }, CancellationToken.None);

        Assert.Equal(SystemState.PlanGenerated, result.FinalState);
        Assert.Equal(0, _approvals.CreatedRequests);
        Assert.Equal(0, _maintenance.ExecuteCalls);
        Assert.Equal(0, _maintenance.BackupCalls);
        Assert.Equal(0, _reports.GenerateCalls);
        Assert.Equal(1, _scanner.FullScans); // the discovery ran; no second reading for a validation that did not happen
    }

    [Fact]
    public async Task A_category_the_user_deselected_is_not_in_the_plan()
    {
        _maintenance.Categories = new[]
        {
            Descriptor(MaintenanceCategory.TemporaryFiles, SafetyClass.Safe),
            Descriptor(MaintenanceCategory.ThumbnailCache, SafetyClass.Safe),
        };

        var result = await _service.RunAsync(
            new OneClickRequest { Selection = new[] { MaintenanceCategory.TemporaryFiles } },
            CancellationToken.None);

        Assert.Equal(new[] { MaintenanceCategory.TemporaryFiles }, result.Plan.Selected);
        Assert.Equal(new[] { MaintenanceCategory.ThumbnailCache }, result.Plan.ExcludedByUser);
        Assert.DoesNotContain(MaintenanceCategory.ThumbnailCache, _maintenance.LastSelection ?? Array.Empty<MaintenanceCategory>());
    }

    [Fact]
    public async Task A_protected_category_runs_only_when_the_user_asked_for_it()
    {
        _maintenance.Categories = new[] { Descriptor(MaintenanceCategory.WindowsUpdateCache, SafetyClass.Protected) };

        var without = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);
        var including = await _service.RunAsync(
            new OneClickRequest { IncludeProtectedCategories = true },
            CancellationToken.None);

        Assert.Equal(new[] { MaintenanceCategory.WindowsUpdateCache }, without.Plan.ExcludedByPolicy);
        Assert.Contains(MaintenanceCategory.WindowsUpdateCache, including.Plan.Selected);
    }

    [Fact]
    public async Task A_category_with_an_unknown_safety_class_is_left_out_and_named()
    {
        _maintenance.Categories = new[] { Descriptor(MaintenanceCategory.PrefetchedData, SafetyClass.Unknown) };

        var result = await _service.RunAsync(
            new OneClickRequest { IncludeProtectedCategories = true },
            CancellationToken.None);

        Assert.Equal(new[] { MaintenanceCategory.PrefetchedData }, result.Plan.ExcludedByPolicy);
        Assert.Contains(result.OpenPoints, point => point.Contains("unknown safety class", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Free_space_is_measured_before_and_after_and_the_difference_is_stated()
    {
        _scanner.Snapshots.Enqueue(Snapshot(freeBytes: 1_000));
        _scanner.Snapshots.Enqueue(Snapshot(freeBytes: 4_500));

        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        var measurement = Assert.Single(result.Measurements);
        Assert.Equal(OneClickMaintenanceService.MeasurementName, measurement.Name);
        Assert.Equal(1_000L, measurement.Before.Value!.Value);
        Assert.Equal(4_500L, measurement.After.Value!.Value);
        Assert.Equal(3_500L, measurement.Delta.Value!.Value);
    }

    [Fact]
    public async Task A_missing_reading_never_becomes_a_zero()
    {
        _scanner.Snapshots.Enqueue(Snapshot(freeBytes: 1_000));
        _scanner.Snapshots.Enqueue(Snapshot(freeBytes: null));

        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        var measurement = Assert.Single(result.Measurements);
        Assert.True(measurement.After.IsUnknown);
        Assert.True(measurement.Delta.IsUnknown);
        Assert.False(string.IsNullOrWhiteSpace(measurement.Delta.UnknownReason));
    }

    [Fact]
    public async Task A_failed_item_never_ends_as_success()
    {
        _maintenance.ItemOutcome = StageOutcome.Failed;

        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        Assert.Equal(SystemState.Error, result.FinalState);
        Assert.Contains(result.OpenPoints, point => point.Contains("failed", StringComparison.OrdinalIgnoreCase));
        // The report is still written: a failure that is not written down is not evidence.
        Assert.Equal(1, _reports.GenerateCalls);
    }

    [Fact]
    public async Task An_executed_and_validated_run_ends_as_success()
    {
        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        Assert.Equal(SystemState.Success, result.FinalState);
        Assert.Equal(1, _maintenance.ExecuteCalls);
        Assert.Equal(1, _reports.GenerateCalls);
        Assert.NotNull(result.Report);
        Assert.NotNull(result.Approval);
    }

    [Fact]
    public async Task A_cancelled_run_is_reported_as_cancelled_and_leaves_the_state_for_recovery()
    {
        using var cancellation = new CancellationTokenSource();
        _maintenance.OnExecute = () => cancellation.Cancel();

        var result = await _service.RunAsync(new OneClickRequest(), cancellation.Token);

        Assert.Equal(SystemState.Cancelled, result.FinalState);
        Assert.Equal(SystemState.Cancelled, _state.Current);
        Assert.Contains(
            result.Steps,
            step => step.Outcome == StageOutcome.Cancelled);
        Assert.Contains(_audit.Entries, entry => entry.Result == StageOutcome.Cancelled);
        Assert.Contains(result.OpenPoints, point => point.Contains("stopped by the user", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_refused_discovery_blocks_the_run_instead_of_planning_on_nothing()
    {
        _scanner.Failure = new InvalidOperationException("WMI is not answering");

        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        Assert.Equal(SystemState.Blocked, result.FinalState);
        Assert.Equal(0, _maintenance.ExecuteCalls);
        Assert.Contains(
            result.Steps,
            step => step.Phase == OneClickPhase.Discovery && step.Outcome == StageOutcome.Failed);
        Assert.Contains(result.OpenPoints, point => point.Contains("discovery failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_empty_plan_reports_that_it_did_nothing_instead_of_claiming_success()
    {
        _maintenance.Categories = Array.Empty<MaintenanceCategoryDescriptor>();
        _maintenance.ItemOutcome = StageOutcome.Succeeded;
        _maintenance.EmptyPlan = true;

        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        Assert.Equal(SystemState.Blocked, result.FinalState);
        Assert.Equal(0, _maintenance.ExecuteCalls);
        Assert.Contains(result.OpenPoints, point => point.Contains("nothing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_plan_that_needs_a_backup_is_secured_before_the_execution()
    {
        // The fake keeps the order of the calls: a backup that came after the execution would be a
        // changed system without a fallback, which is exactly what chapter 44 forbids.
        _maintenance.BackupRequired = true;

        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        Assert.Equal(new[] { "backup", "execute" }, _maintenance.CallOrder);

        var states = _state.History.Select(change => change.Current).ToList();
        Assert.Contains(SystemState.Backup, states);
        Assert.True(
            states.IndexOf(SystemState.Backup) < states.IndexOf(SystemState.Executing),
            "the backup state has to be entered before the execution state");
        Assert.NotNull(result.Backup);
        Assert.Equal(StageOutcome.Succeeded, Assert.Single(result.Steps, step => step.Phase == OneClickPhase.Backup).Outcome);
    }

    [Fact]
    public async Task A_plan_without_backup_says_so_instead_of_leaving_the_phase_blank()
    {
        var result = await _service.RunAsync(new OneClickRequest(), CancellationToken.None);

        var backupStep = Assert.Single(result.Steps, step => step.Phase == OneClickPhase.Backup && step.IsPhaseStep);
        Assert.Equal(StageOutcome.Skipped, backupStep.Outcome);
        Assert.Contains("does not require a backup", backupStep.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_plan_lists_every_item_it_may_change()
    {
        _maintenance.PlannedItems = 3;

        var result = await _service.RunAsync(new OneClickRequest { PlanOnly = true }, CancellationToken.None);

        var planned = result.Steps.Where(step => step.Phase == OneClickPhase.Plan && !step.IsPhaseStep).ToArray();
        Assert.Equal(3, planned.Length);
        Assert.All(planned, step => Assert.NotNull(step.Risk));
    }

    private static MaintenanceCategoryDescriptor Descriptor(MaintenanceCategory category, SafetyClass safety) => new()
    {
        Category = category,
        DisplayNameKey = "Maintenance_TemporaryFiles_Name",
        SafetyClass = safety,
    };

    private static SystemSnapshot Snapshot(long? freeBytes) => new()
    {
        Id = $"SNAP-{freeBytes?.ToString() ?? "unknown"}",
        CapturedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
        Storage = new[]
        {
            new StorageDeviceInfo
            {
                Model = TextInfo.Known("Test disk", DataSource.LocalData),
                Volumes = new[]
                {
                    new VolumeInfo
                    {
                        DriveLetter = TextInfo.Known("C:", DataSource.LocalData),
                        SizeBytes = Measured<ulong>.Known(10_000u, ValueOrigin.Unknown),
                        FreeBytes = freeBytes is null
                            ? Measured<ulong>.NotAvailable("the volume did not report its free space")
                            : Measured<ulong>.Known((ulong)freeBytes.Value, ValueOrigin.Unknown),
                    },
                },
            },
        },
    };
}

/// <summary>
/// Maintenance engine double. It answers with the plan the test asked for and records the calls in the
/// order they arrived, so "backup before execution" and "no execution without approval" are checkable.
/// </summary>
internal sealed class FakeMaintenanceService : IMaintenanceService
{
    public List<string> CallOrder { get; } = new();

    public int ExecuteCalls { get; private set; }

    public int BackupCalls { get; private set; }

    public IReadOnlyList<MaintenanceCategoryDescriptor> Categories { get; set; } = new[]
    {
        new MaintenanceCategoryDescriptor
        {
            Category = MaintenanceCategory.TemporaryFiles,
            DisplayNameKey = "Maintenance_TemporaryFiles_Name",
            SafetyClass = SafetyClass.Safe,
        },
    };

    public IReadOnlyCollection<MaintenanceCategory>? LastSelection { get; private set; }

    public StageOutcome ItemOutcome { get; set; } = StageOutcome.Succeeded;

    public bool BackupRequired { get; set; }

    public bool EmptyPlan { get; set; }

    public int PlannedItems { get; set; } = 1;

    public IReadOnlyList<MaintenanceCategoryDescriptor> DescribeCategories() => Categories;

    public Task<MaintenanceScanResult> ScanAsync(MaintenanceScanOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(new MaintenanceScanResult
        {
            ScannedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            Items = Array.Empty<MaintenanceItem>(),
        });

    public Task<MaintenancePlan> BuildPlanAsync(
        MaintenanceScanResult scan,
        IReadOnlyList<MaintenanceCategory> selection,
        ExecutionMode mode,
        CancellationToken cancellationToken)
    {
        LastSelection = selection.ToArray();

        var items = EmptyPlan || selection.Count == 0
            ? Array.Empty<MaintenancePlanItem>()
            : Enumerable.Range(1, PlannedItems)
                .Select(index => new MaintenancePlanItem
                {
                    ItemId = $"ITEM-{index}",
                    Category = selection.Count > 0 ? selection[0] : MaintenanceCategory.TemporaryFiles,
                    DisplayNameKey = "Maintenance_TemporaryFiles_Name",
                    SafetyClass = SafetyClass.Safe,
                    RootPath = @"C:\Temp",
                    SizeBytes = Measured<long>.Known(1024, ValueOrigin.Unknown),
                    FileCount = Measured<int>.Known(4, ValueOrigin.Unknown),
                    Change = LocalizedText.Of("Maintenance_Change_Delete", 4),
                    Reason = LocalizedText.Of("Maintenance_Reason_Evidence", "safe cache"),
                    Risk = RiskLevel.Low,
                    IsSelected = true,
                })
                .ToArray();

        return Task.FromResult(new MaintenancePlan
        {
            PlanId = "PLAN-M27-1",
            Mode = mode,
            Items = items,
            TotalBytesToFree = Measured<long>.Known(items.Length * 1024L, ValueOrigin.Unknown),
            Risk = BackupRequired ? RiskLevel.High : RiskLevel.Low,
            BackupRequired = BackupRequired,
            DryRunPlanId = "PLAN-M27-DRY",
            CreatedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
        });
    }

    public Task<MaintenanceResult> ExecuteAsync(
        MaintenancePlan plan,
        ApprovalRecord approval,
        CancellationToken cancellationToken)
    {
        ExecuteCalls++;
        CallOrder.Add("execute");
        OnExecute?.Invoke();

        // A real execution stops when the user cancels; the double has to behave the same way, or the
        // cancellation test would prove nothing.
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new MaintenanceResult
        {
            PlanId = plan.PlanId,
            Mode = ExecutionMode.Execute,
            Items = plan.Items
                .Select(item => new MaintenanceItemResult
                {
                    ItemId = item.ItemId,
                    Category = item.Category,
                    DisplayNameKey = item.DisplayNameKey,
                    Outcome = ItemOutcome,
                    FreedBytes = Measured<long>.Known(1024, ValueOrigin.Unknown),
                })
                .ToArray(),
            FreedBytes = Measured<long>.Known(1024, ValueOrigin.Unknown),
        });
    }

    public Task<MaintenanceResult> ExecuteDryRunAsync(MaintenancePlan plan, CancellationToken cancellationToken) =>
        Task.FromResult(new MaintenanceResult { PlanId = plan.PlanId, Mode = ExecutionMode.DryRun });

    public Task<BackupRecord> RecordBackupAsync(MaintenancePlan plan, CancellationToken cancellationToken)
    {
        BackupCalls++;
        CallOrder.Add("backup");

        return Task.FromResult(new BackupRecord
        {
            Id = "BACKUP-M27-1",
            OperationId = plan.PlanId,
            CreatedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            ArtifactPath = @"C:\ProgramData\WindowsMaintenanceCenter\backups\BACKUP-M27-1.zip",
            Evidence = new[] { "item=ITEM-1 root=C:\\Temp safety=Safe" },
        });
    }

    /// <summary>Called inside the execution, so a test can cancel the run at exactly that moment.</summary>
    public Action? OnExecute { get; set; }
}

/// <summary>Scanner double: hands out prepared snapshots and can fail like a missing WMI class does.</summary>
internal sealed class FakeScanOrchestrator : IScanOrchestrator
{
    private readonly Queue<SystemSnapshot> _prepared = new();

    public Queue<SystemSnapshot> Snapshots => _prepared;

    public Exception? Failure { get; set; }

    public int FullScans { get; private set; }

    public IReadOnlyList<IDiagnosticModule> Modules => Array.Empty<IDiagnosticModule>();

    public SystemSnapshot? LastSnapshot { get; private set; }

    public event EventHandler<SystemSnapshot>? SnapshotCompleted;

    public Task<SystemSnapshot> RunFullScanAsync(CancellationToken cancellationToken)
    {
        FullScans++;

        var failure = Failure;
        if (failure is not null)
        {
            return Task.FromException<SystemSnapshot>(failure);
        }

        var snapshot = _prepared.Count > 0
            ? _prepared.Dequeue()
            : new SystemSnapshot { Id = $"SNAP-{FullScans}", CapturedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero) };

        LastSnapshot = snapshot;
        SnapshotCompleted?.Invoke(this, snapshot);
        return Task.FromResult(snapshot);
    }

    public Task<SystemSnapshot> RunModuleAsync(string moduleId, CancellationToken cancellationToken) =>
        RunFullScanAsync(cancellationToken);

    public Task<SystemSnapshot> ReadInventoryAsync(CancellationToken cancellationToken) =>
        RunFullScanAsync(cancellationToken);
}

/// <summary>Approval double: decides the way the test says, without asking anybody.</summary>
internal sealed class FakeApprovalService : IApprovalService
{
    public ApprovalDecision Outcome { get; set; } = ApprovalDecision.Approved;

    public int CreatedRequests { get; private set; }

    public ApprovalRequestDraft? LastDraft { get; private set; }

    public event EventHandler<ApprovalRequest>? ApprovalRequested;

    public event EventHandler<ApprovalRequest>? ApprovalDecided;

    public IReadOnlyList<ApprovalRecord> History => Array.Empty<ApprovalRecord>();

    public bool IsConfirmationRequired(RiskLevel risk) => true;

    public Task<ApprovalRequest> CreateAsync(ApprovalRequestDraft draft, CancellationToken cancellationToken)
    {
        CreatedRequests++;
        LastDraft = draft;
        var request = new ApprovalRequest
        {
            RequestId = $"REQ-{CreatedRequests}",
            Draft = draft,
            Decision = Outcome,
            CreatedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            DecidedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
        };

        ApprovalRequested?.Invoke(this, request);
        ApprovalDecided?.Invoke(this, request);
        return Task.FromResult(request);
    }

    public Task<ApprovalRequest> WaitForDecisionAsync(string requestId, CancellationToken cancellationToken) =>
        CreateAsync(new ApprovalRequestDraft(), cancellationToken);

    public void Decide(string requestId, ApprovalDecision decision, string? note = null) => Outcome = decision;

    public async Task<ApprovalRecord> EnsureApprovedAsync(ApprovalRequestDraft draft, CancellationToken cancellationToken)
    {
        var request = await CreateAsync(draft, cancellationToken).ConfigureAwait(false);
        if (Outcome != ApprovalDecision.Approved)
        {
            throw new OperationBlockedException(
                "APPROVAL_REJECTED",
                LocalizedText.Of("Approval_Rejected"),
                "the test double was told to refuse");
        }

        return new ApprovalRecord
        {
            RequestId = request.RequestId,
            OperationId = draft.OperationId,
            Decision = Outcome,
            DecidedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
            Risk = draft.Risk,
            WasRequired = true,
        };
    }
}

/// <summary>Report double: counts calls and answers with an artefact path that names the format.</summary>
internal sealed class FakeReportGenerator : IReportGenerator
{
    public int GenerateCalls { get; private set; }

    public IReadOnlyList<ReportFormat> SupportedFormats { get; } =
        new[] { ReportFormat.Html, ReportFormat.Text, ReportFormat.Json, ReportFormat.Pdf };

    public Task<ReportArtifact> GenerateAsync(
        ReportRequest request,
        ReportFormat format,
        ReportOptions options,
        CancellationToken cancellationToken)
    {
        GenerateCalls++;
        return Task.FromResult(new ReportArtifact
        {
            Format = format,
            FilePath = $@"C:\ProgramData\WindowsMaintenanceCenter\reports\run-{GenerateCalls}.{format.ToString().ToLowerInvariant()}",
            CreatedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
        });
    }
}

/// <summary>Elevation double: reports the session privilege a test needs.</summary>
internal sealed class FakeElevationService : IElevationService
{
    public bool IsElevated { get; set; } = true;

    public LocalizedText ExplainRequirement(string reasonCode) => LocalizedText.Of(reasonCode);

    public Task<bool> RestartElevatedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
}
