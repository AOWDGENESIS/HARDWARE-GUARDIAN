using System.Globalization;
using Microsoft.Extensions.Logging;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// One-click maintenance (chapter 33, module M27 - priority P0).
///
/// The specification fixes the sequence and this class runs exactly it, in this order, once:
///
/// <code>
/// DISCOVERY -> DIAGNOSTIC -> PLAN -> APPROVAL -> BACKUP -> EXECUTION -> VALIDATION -> REPORT
/// </code>
///
/// What the class is *not*: it is not a second implementation of the maintenance engine. The plan,
/// the mandatory dry run, the backup gate and the deletion itself belong to
/// <see cref="IMaintenanceService"/>; the approval belongs to <see cref="IApprovalService"/>; the
/// states belong to <see cref="ISystemStateMachine"/>. This class is the conductor: it asks those
/// services in the specified order, refuses to skip a gate, and writes down what happened.
///
/// The rules it enforces itself, because they are the point of the module:
///
/// * **No hidden action (M27-S-001).** Every phase becomes a <see cref="OneClickStep"/> with what,
///   why and risk, and the list is published to the live protocol, written to the audit log and
///   carried into the report. Nothing that is not in the list can be executed, because every
///   executing phase goes through the maintenance service with exactly the plan from the list.
/// * **The user can deselect (M27-S-002).** A category the user left out is listed as
///   <see cref="OneClickPlan.ExcludedByUser"/> and is not part of the plan. It is never silently
///   added back, and the summary says how many items were left out.
/// * **Risks are shown (M27-S-003).** Every step carries the risk of its phase, the planned items
///   carry the risk classes of the maintenance plan, and the plan's own risk is the highest of them.
/// * **Measured before and after (M27-F-001).** Free space is read before and after through the same
///   reading; the delta is computed only when both readings exist, and a missing reading stays
///   UNKNOWN. No "cleaned 4.2 GB" is ever printed when nobody measured 4.2 GB.
/// * **An interruption creates a recovery state (M27-R-001).** The run announces its operation to
///   the state machine before the first changing phase and ends it afterwards. A cancellation (or a
///   crash) therefore leaves the journal naming the operation and the state it was interrupted in -
///   which is exactly what the recovery engine reads.
/// * **Success only after execution *and* validation (chapter 86).** The final state is
///   <see cref="SystemState.Success"/> only when the execution ran and the after-reading happened.
///   Anything else ends as <see cref="SystemState.Blocked"/> (when nothing was changed) or
///   <see cref="SystemState.Error"/> (when something went wrong), with the reason in the steps.
/// </summary>
public interface IOneClickMaintenanceService
{
    /// <summary>
    /// Runs the whole sequence. Never throws for a blocked run: a blocked run is a result
    /// (<see cref="OneClickResult.FinalState"/>), because the caller has to be able to report it.
    /// Only a defect of the application itself escapes as an exception.
    /// </summary>
    Task<OneClickResult> RunAsync(OneClickRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Builds the plan without changing anything: DISCOVERY, DIAGNOSTIC and PLAN, nothing else.
    /// Used by the interface to show what a run would do (and by the tests to check the plan).
    /// </summary>
    Task<OneClickResult> PlanAsync(OneClickRequest request, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IOneClickMaintenanceService"/>
public sealed class OneClickMaintenanceService : IOneClickMaintenanceService
{
    private const string ModuleId = "M27";

    private readonly ISystemStateMachine _state;
    private readonly IScanOrchestrator _scanner;
    private readonly IMaintenanceService _maintenance;
    private readonly IApprovalService _approval;
    private readonly IAuditLog _audit;
    private readonly IReportGenerator _reports;
    private readonly ILiveProtocol _protocol;
    private readonly IElevationService _elevation;
    private readonly IClock _clock;
    private readonly ILogger<OneClickMaintenanceService>? _logger;
    private readonly long _operationCounter;

    public OneClickMaintenanceService(
        ISystemStateMachine state,
        IScanOrchestrator scanner,
        IMaintenanceService maintenance,
        IApprovalService approval,
        IAuditLog audit,
        IReportGenerator reports,
        ILiveProtocol protocol,
        IElevationService elevation,
        IClock clock,
        ILogger<OneClickMaintenanceService>? logger = null)
    {
        _state = state;
        _scanner = scanner;
        _maintenance = maintenance;
        _approval = approval;
        _audit = audit;
        _reports = reports;
        _protocol = protocol;
        _elevation = elevation;
        _clock = clock;
        _logger = logger;

        // The operation id has to be unique per run and has to survive a restart in the journal, where
        // it is read back by the recovery engine. Milliseconds plus a per-process counter is enough for
        // both and needs no dependency beyond the clock the application already injects.
        _operationCounter = clock.UtcNow.ToUnixTimeMilliseconds();
    }

    public async Task<OneClickResult> PlanAsync(OneClickRequest request, CancellationToken cancellationToken)
    {
        var steps = new List<OneClickStep>();
        var openPoints = new List<string>();
        var started = _clock.UtcNow;
        var operationId = NextOperationId();

        var discovery = await RunDiscoveryAsync(steps, openPoints, cancellationToken).ConfigureAwait(false);
        var diagnostic = RunDiagnostic(discovery, steps, openPoints);
        var plan = await PlanAsync(request, discovery, diagnostic, steps, openPoints, cancellationToken).ConfigureAwait(false);

        // The plan is a state of the machine (chapter 40), not just a local variable: without this
        // transition the journal would jump from DIAGNOSTIC to AWAITING_APPROVAL.
        _state.TryTransitionTo(SystemState.PlanGenerated, "M27 run: plan built");

        return new OneClickResult
        {
            OperationId = operationId,
            Plan = plan,
            Steps = steps,
            Measurements = Array.Empty<OneClickMeasurement>(),
            FinalState = SystemState.PlanGenerated,
            Summary = LocalizedText.Of("OneClick_Summary_Planned"),
            OpenPoints = openPoints,
            StartedAt = started,
            CompletedAt = _clock.UtcNow,
        };
    }

    public async Task<OneClickResult> RunAsync(OneClickRequest request, CancellationToken cancellationToken)
    {
        var steps = new List<OneClickStep>();
        var openPoints = new List<string>();
        var measurements = new List<OneClickMeasurement>();
        var started = _clock.UtcNow;
        var operationId = NextOperationId();

        var state = SystemState.Blocked;
        var summary = LocalizedText.Of("OneClick_Result_NotRun");
        MaintenanceResult? execution = null;
        BackupRecord? backup = null;
        ApprovalRecord? approval = null;
        ReportArtifact? report = null;
        var plan = new OneClickPlan { PlanId = string.Empty, CreatedAt = started };

        // 1. DISCOVERY (read only).
        var snapshotBefore = await RunDiscoveryAsync(steps, openPoints, cancellationToken).ConfigureAwait(false);

        // 2. DIAGNOSTIC (read only).
        var problems = RunDiagnostic(snapshotBefore, steps, openPoints);

        // 3. PLAN - the list everything else is measured against.
        plan = await PlanAsync(request, snapshotBefore, problems, steps, openPoints, cancellationToken).ConfigureAwait(false);
        _state.TryTransitionTo(SystemState.PlanGenerated, "M27 run: plan built");
        state = SystemState.PlanGenerated;

        if (request.PlanOnly)
        {
            return Result(
                SystemState.PlanGenerated,
                LocalizedText.Of("OneClick_Summary_Planned"),
                plan,
                steps,
                measurements,
                execution: null,
                backup: null,
                approval: null,
                report: null,
                openPoints,
                operationId,
                started);
        }

        if (snapshotBefore is null)
        {
            // Without the reading there is no "before" to measure against, and no reading the plan could
            // be based on. Claiming a result here would be exactly the fake diagnosis chapter 1.2 forbids.
            openPoints.Add("the discovery failed, so there is no reading to measure against; nothing was changed");
            MarkPhase(steps, OneClickPhase.Approval, StageOutcome.Skipped, "there is no reading to plan for");
            MarkRemaining(steps, from: OneClickPhase.Backup, reason: "the discovery did not deliver a reading");
            await AuditAsync(operationId, StageOutcome.Blocked, "discovery failed", cancellationToken).ConfigureAwait(false);
            return Result(
                SystemState.Blocked,
                LocalizedText.Of("OneClick_Summary_Blocked"),
                plan,
                steps,
                measurements,
                null,
                null,
                null,
                null,
                openPoints,
                operationId,
                started);
        }

        if (plan.Maintenance is null || plan.Maintenance.Items.Count == 0)
        {
            // Nothing to do is a result, not a failure - and it is not a success of any cleaning either.
            openPoints.Add("no maintenance item was selectable, so nothing was planned and nothing was changed");
            MarkPhase(steps, OneClickPhase.Execution, StageOutcome.Skipped, "the plan is empty");
            MarkPhase(steps, OneClickPhase.Validation, StageOutcome.Skipped, "nothing was changed, so there is nothing to validate");
            MarkPhase(steps, OneClickPhase.Report, StageOutcome.Skipped, "the plan is empty and the run changes nothing");
            return Result(
                SystemState.Blocked,
                LocalizedText.Of("OneClick_Summary_NothingToDo"),
                plan,
                steps,
                measurements,
                null,
                null,
                null,
                null,
                openPoints,
                operationId,
                started);
        }

        // 4. APPROVAL. The approval of the maintenance service is bound to exactly this plan - the
        //    service refuses a mismatching one, so the gate cannot be bypassed by calling it directly.
        var approvalStep = Begin(steps, OneClickPhase.Approval);
        try
        {
            _state.TryTransitionTo(SystemState.AwaitingApproval, "M27 run: approval of the plan");
            approval = await _approval.EnsureApprovedAsync(DraftFor(plan.Maintenance, operationId), cancellationToken)
                .ConfigureAwait(false);
            Complete(
                steps,
                approvalStep,
                StageOutcome.Succeeded,
                $"decision {approval.Decision} for plan {plan.Maintenance.PlanId}, request {approval.RequestId}");
        }
        catch (OperationBlockedException blocked)
        {
            Complete(steps, approvalStep, StageOutcome.Blocked, $"{blocked.ReasonCode}: {blocked.Reason.Key}");
            openPoints.Add($"the run was not approved ({blocked.ReasonCode}), so nothing was changed");
            MarkRemaining(steps, from: OneClickPhase.Backup, reason: "the approval was not given");
            await AuditAsync(operationId, StageOutcome.Blocked, blocked.ReasonCode, cancellationToken).ConfigureAwait(false);
            _state.TryTransitionTo(SystemState.Blocked, $"approval refused: {blocked.ReasonCode}");
            return Result(
                SystemState.Blocked,
                LocalizedText.Of("OneClick_Summary_NotApproved"),
                plan,
                steps,
                measurements,
                null,
                null,
                null,
                null,
                openPoints,
                operationId,
                started);
        }

        // From here on the run changes things. The operation is announced, so a crash in the middle
        // leaves a named operation behind for the recovery engine (M27-R-001).
        _state.BeginOperation(operationId, actionId: plan.Maintenance.PlanId);

        try
        {
            // 5. BACKUP (the gate the maintenance service enforces itself: a plan that needs one is
            //    refused without a backup record).
            var backupStep = Begin(steps, OneClickPhase.Backup);
            if (plan.Maintenance.BackupRequired)
            {
                _state.TryTransitionTo(SystemState.Backup, "M27 run: securing the affected locations");
                backup = await _maintenance.RecordBackupAsync(plan.Maintenance, cancellationToken).ConfigureAwait(false);
                Complete(
                    steps,
                    backupStep,
                    StageOutcome.Succeeded,
                    $"backup {backup.Id} at {backup.ArtifactPath ?? "not reported"}, {backup.Evidence.Count} evidence line(s)");
            }
            else
            {
                Complete(steps, backupStep, StageOutcome.Skipped,
                    "these locations are caches and temporary files; the plan does not require a backup");
            }

            // 6. EXECUTION.
            var executionStep = Begin(steps, OneClickPhase.Execution);
            _state.TryTransitionTo(SystemState.Executing, "M27 run: executing the approved plan");
            execution = await _maintenance.ExecuteAsync(plan.Maintenance, approval, cancellationToken).ConfigureAwait(false);
            Complete(steps, executionStep, execution.Items.Any(item => item.Outcome == StageOutcome.Failed)
                    ? StageOutcome.Failed
                    : StageOutcome.Succeeded,
                $"plan {execution.PlanId}: {execution.Items.Count} item(s), freed {Describe(execution.FreedBytes)}");

            // 7. VALIDATION - measured, not assumed (M27-F-001).
            var validationStep = Begin(steps, OneClickPhase.Validation);
            _state.TryTransitionTo(SystemState.Validating, "M27 run: measuring the state after the run");
            var snapshotAfter = await _scanner.RunFullScanAsync(cancellationToken).ConfigureAwait(false);
            measurements.AddRange(MeasureBeforeAfter(snapshotBefore, snapshotAfter));

            var freed = execution.FreedBytes;
            var delta = measurements.FirstOrDefault(m => m.Name == MeasurementName);
            Complete(steps, validationStep, StageOutcome.Succeeded,
                $"free space before {Describe(delta?.Before)}, after {Describe(delta?.After)}, change {Describe(delta?.Delta)}; " +
                $"the maintenance engine reported {Describe(freed)} freed");

            // 8. REPORT.
            var reportStep = Begin(steps, OneClickPhase.Report);
            report = await _reports.GenerateAsync(
                    new ReportRequest
                    {
                        Snapshot = snapshotAfter,
                        Maintenance = new[] { execution },
                        Audit = Array.Empty<AuditEntry>(),
                    },
                    request.ReportFormat,
                    request.Report ?? new ReportOptions())
                .ConfigureAwait(false);
            Complete(steps, reportStep, StageOutcome.Succeeded, $"{report.Format} report at {report.FilePath}");

            // Executed AND validated: only now may the run be called successful (chapter 86).
            var failures = execution.Items.Count(item => item.Outcome == StageOutcome.Failed);
            if (failures > 0)
            {
                state = SystemState.Error;
                summary = LocalizedText.Of("OneClick_Summary_PartlyFailed");
                openPoints.Add($"{failures} maintenance item(s) failed; the run is not a success even though steps ran");
                _state.TryTransitionTo(SystemState.Error, $"{failures} item(s) failed");
            }
            else
            {
                state = SystemState.Success;
                summary = LocalizedText.Of("OneClick_Summary_Completed");
                _state.TryTransitionTo(SystemState.Success, "execution and validation completed");
            }
        }
        catch (OperationCanceledException)
        {
            // The user stopped the run. That is not a defect: it is a cancelled run, and it has to
            // leave the same evidence behind as any other, so the recovery engine can find it.
            openPoints.Add("the run was stopped by the user; the state journal names the operation and the state it was interrupted in");
            foreach (var step in steps
                         .Where(candidate => candidate.Outcome is StageOutcome.Running or StageOutcome.NotRun)
                         .ToList())
            {
                Complete(steps, step, StageOutcome.Cancelled, "the user stopped the run");
            }

            await AuditAsync(operationId, StageOutcome.Cancelled, "cancelled by the user", CancellationToken.None).ConfigureAwait(false);
            _state.TryTransitionTo(SystemState.Cancelled, "M27 run cancelled by the user");
            MarkPhase(steps, OneClickPhase.Report, StageOutcome.Skipped, "the run was stopped before a report could be written");
            state = SystemState.Cancelled;
            summary = LocalizedText.Of("OneClick_Summary_Cancelled");
        }
        catch (OperationBlockedException blocked)
        {
            openPoints.Add($"the run was blocked ({blocked.ReasonCode}); nothing further was executed");
            foreach (var step in steps
                         .Where(candidate => candidate.Outcome is StageOutcome.Running or StageOutcome.NotRun)
                         .ToList())
            {
                Complete(steps, step, StageOutcome.Blocked, $"{blocked.ReasonCode}: {blocked.Detail ?? blocked.Reason.Key}");
            }

            await AuditAsync(operationId, StageOutcome.Blocked, blocked.ReasonCode, CancellationToken.None).ConfigureAwait(false);
            _state.TryTransitionTo(SystemState.Blocked, $"M27 run blocked: {blocked.ReasonCode}");
            state = SystemState.Blocked;
            summary = LocalizedText.Of("OneClick_Summary_Blocked");
        }
        catch (Exception error)
        {
            // An unexpected failure is reported as a failure - never as a success and never swallowed.
            _logger?.LogError(error, "the one-click run {OperationId} failed", operationId);
            openPoints.Add($"the run failed with {error.GetType().Name}: {error.Message}");
            foreach (var step in steps
                         .Where(candidate => candidate.Outcome is StageOutcome.Running or StageOutcome.NotRun)
                         .ToList())
            {
                Complete(steps, step, StageOutcome.Failed, error.Message);
            }

            await AuditAsync(operationId, StageOutcome.Failed, error.Message, CancellationToken.None).ConfigureAwait(false);
            _state.TryTransitionTo(SystemState.Error, "M27 run failed");
            state = SystemState.Error;
            summary = LocalizedText.Of("OneClick_Summary_Failed");
        }
        finally
        {
            _state.EndOperation();
        }

        return Result(state, summary, plan, steps, measurements, execution, backup, approval, report, openPoints, operationId, started);
    }

    // ---------- phases ------------------------------------------------------------------------------

    private async Task<SystemSnapshot?> RunDiscoveryAsync(
        List<OneClickStep> steps,
        List<string> openPoints,
        CancellationToken cancellationToken)
    {
        var step = Begin(steps, OneClickPhase.Discovery);
        try
        {
            _state.TryTransitionTo(SystemState.Discovery, "M27 run: reading the system");
            var snapshot = await _scanner.RunFullScanAsync(cancellationToken).ConfigureAwait(false);
            Complete(steps, step, StageOutcome.Succeeded,
                $"{snapshot.Components.Count} component(s), {snapshot.Storage.Count} storage device(s), {snapshot.Problems.Count} finding(s)");
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            // Without the reading there is no plan. The run says so instead of planning on nothing.
            Complete(steps, step, StageOutcome.Failed, error.Message);
            openPoints.Add($"the discovery failed ({error.GetType().Name}); no plan could be built");
            return null;
        }
    }

    private IReadOnlyList<Problem> RunDiagnostic(
        SystemSnapshot? snapshot,
        List<OneClickStep> steps,
        List<string> openPoints)
    {
        var step = Begin(steps, OneClickPhase.Diagnostic);
        if (snapshot is null)
        {
            Complete(steps, step, StageOutcome.Skipped, "there is nothing to assess: the discovery did not deliver a snapshot");
            return Array.Empty<Problem>();
        }

        _state.TryTransitionTo(SystemState.Diagnostic, "M27 run: assessing the readings");
        var problems = snapshot.Problems;
        var relevant = problems.Count(problem => problem.Severity is Severity.Warning or Severity.Error or Severity.Critical);
        Complete(steps, step, StageOutcome.Succeeded,
            $"{problems.Count} finding(s), {relevant} of them with severity warning or higher; overall {snapshot.OverallStatus}");

        if (problems.Count == 0)
        {
            openPoints.Add("the diagnostic reported no finding, so the run has no reason to change anything beyond the chosen cleanup");
        }

        return problems;
    }

    private async Task<OneClickPlan> PlanAsync(
        OneClickRequest request,
        SystemSnapshot? snapshot,
        IReadOnlyList<Problem> problems,
        List<OneClickStep> steps,
        List<string> openPoints,
        CancellationToken cancellationToken)
    {
        var step = Begin(steps, OneClickPhase.Plan);
        var createdAt = _clock.UtcNow;
        var descriptors = _maintenance.DescribeCategories();

        var selection = request.Selection is null
            ? descriptors
                .Where(descriptor => descriptor.SafetyClass is SafetyClass.Safe or SafetyClass.Optional
                                     || (request.IncludeProtectedCategories && descriptor.SafetyClass == SafetyClass.Protected))
                .Select(descriptor => descriptor.Category)
                .ToList()
            : request.Selection.ToList();

        var selected = new List<MaintenanceCategory>();
        var excludedByUser = new List<MaintenanceCategory>();
        var excludedByPolicy = new List<MaintenanceCategory>();

        foreach (var descriptor in descriptors)
        {
            if (request.Selection is not null && !request.Selection.Contains(descriptor.Category))
            {
                excludedByUser.Add(descriptor.Category);
                continue;
            }

            if (descriptor.SafetyClass == SafetyClass.Protected && !request.IncludeProtectedCategories)
            {
                excludedByPolicy.Add(descriptor.Category);
                continue;
            }

            if (descriptor.SafetyClass == SafetyClass.Unknown)
            {
                // An item whose class nobody classified must not be cleaned. The reason is reported.
                excludedByPolicy.Add(descriptor.Category);
                openPoints.Add($"{descriptor.Category} has an unknown safety class and was left out");
                continue;
            }

            if (descriptor.RequiresAdministrator && !_elevation.IsElevated)
            {
                // The maintenance service would block it anyway; saying it here means the user sees it
                // in the plan instead of in a failed run.
                excludedByPolicy.Add(descriptor.Category);
                openPoints.Add($"{descriptor.Category} needs administrator rights and this session does not have them");
                continue;
            }

            selected.Add(descriptor.Category);
        }

        // The scan and the plan itself belong to the maintenance engine: the mandatory dry run it
        // performs while building the plan is the reason this class does not need its own scan.
        MaintenancePlan? maintenancePlan = null;
        var outcome = StageOutcome.Succeeded;
        string detail;
        try
        {
            var scan = await _maintenance.ScanAsync(request.Scan, cancellationToken).ConfigureAwait(false);

            // The selection is applied here, item by item: what the user left out is not in the plan,
            // and what is in the plan is what the approval and the execution see (M27-S-002).
            maintenancePlan = await _maintenance
                .BuildPlanAsync(scan, selected, ExecutionMode.Execute, cancellationToken)
                .ConfigureAwait(false);

            detail = $"{maintenancePlan.Items.Count} item(s) planned, {maintenancePlan.TotalBytesToFree.Display(CultureInfo.InvariantCulture)} to free, " +
                     $"risk {maintenancePlan.Risk}, backup required: {maintenancePlan.BackupRequired}" +
                     (excludedByUser.Count > 0 ? $"; left out by the user: {excludedByUser.Count}" : string.Empty);

            if (maintenancePlan.Items.Count == 0)
            {
                outcome = StageOutcome.Skipped;
                detail += "; nothing selectable on this machine";
            }
        }
        catch (OperationBlockedException blocked)
        {
            // A plan the maintenance engine refuses (for example: no usable location) is a blocked step,
            // not an empty plan that looks harmless.
            outcome = StageOutcome.Blocked;
            detail = $"{blocked.ReasonCode}: {blocked.Detail ?? blocked.Reason.Key}";
            openPoints.Add($"the maintenance plan could not be built ({blocked.ReasonCode}), so nothing was planned");
        }
        catch (Exception error)
        {
            outcome = StageOutcome.Failed;
            detail = error.Message;
            openPoints.Add($"the maintenance plan failed with {error.GetType().Name}: {error.Message}");
        }

        // Every planned item becomes its own line in the list. That is M27-S-001 in its strict form:
        // a reader can compare the plan with the list and sees every action that may follow, with the
        // location, the size and the safety class the maintenance engine measured for it.
        if (maintenancePlan is not null)
        {
            foreach (var item in maintenancePlan.Items)
            {
                steps.Add(new OneClickStep
                {
                    Phase = OneClickPhase.Plan,
                    IsPhaseStep = false,
                    What = item.Reason,
                    Why = item.Change,
                    Risk = item.Risk,
                    Outcome = StageOutcome.NotRun,
                    Detail = $"planned: {item.DisplayNameKey} at {item.RootPath ?? "(no single path)"}, " +
                             $"{item.SizeBytes.Display(CultureInfo.InvariantCulture)} byte(s), " +
                             $"{item.FileCount.Display(CultureInfo.InvariantCulture)} file(s), safety {item.SafetyClass}",
                    StartedAt = createdAt,
                });
            }
        }

        Complete(steps, step, outcome, detail);

        RiskLevel? risk = maintenancePlan is null ? null : maintenancePlan.Risk;
        if (problems.Any(problem => problem.Severity == Severity.Critical) && risk is not null && risk < RiskLevel.High)
        {
            // A critical finding somewhere in the system raises the attention this run asks for, even
            // when the cleanup itself is harmless.
            openPoints.Add("the diagnostic found a critical problem; the cleanup does not repair it");
        }

        return new OneClickPlan
        {
            PlanId = maintenancePlan?.PlanId ?? string.Empty,
            Maintenance = maintenancePlan,
            Selected = selected,
            ExcludedByUser = excludedByUser,
            ExcludedByPolicy = excludedByPolicy,
            Risk = risk,
            CreatedAt = createdAt,
            Summary = maintenancePlan?.Summary ?? LocalizedText.Of("Maintenance_Plan_Empty"),
        };
    }

    // ---------- measurements -----------------------------------------------------------------------

    /// <summary>The name of the free space measurement. One name, one place - the report names it too.</summary>
    public const string MeasurementName = "freeSpace";

    /// <summary>Localisation key of the same measurement, so the interface can show a translated name.</summary>
    public const string MeasurementDisplayKey = "OneClick_Measurement_FreeSpace";

    private static IReadOnlyList<OneClickMeasurement> MeasureBeforeAfter(SystemSnapshot? before, SystemSnapshot? after)
    {
        if (before is null || after is null)
        {
            return new[]
            {
                new OneClickMeasurement
                {
                    Name = MeasurementName,
                    DisplayNameKey = MeasurementDisplayKey,
                    Before = Measured<long>.NotAvailable("no snapshot before the run"),
                    After = Measured<long>.NotAvailable("no snapshot after the run"),
                    Unit = "bytes",
                    Source = "SystemSnapshot.Storage[].Volumes[].FreeBytes",
                },
            };
        }

        var beforeSum = SumFreeBytes(before);
        var afterSum = SumFreeBytes(after);

        return new[]
        {
            new OneClickMeasurement
            {
                Name = MeasurementName,
                DisplayNameKey = MeasurementDisplayKey,
                Before = beforeSum,
                After = afterSum,
                Unit = "bytes",
                Source = "SystemSnapshot.Storage[].Volumes[].FreeBytes, summed over the reported volumes",
            },
        };
    }

    /// <summary>
    /// Sums the free space of every reported volume. A volume that did not report its free space makes
    /// the whole sum unknown - summing only the reported ones would compare two different sets of
    /// volumes before and after and turn that difference into a "freed space" that nobody measured.
    /// </summary>
    private static Measured<long> SumFreeBytes(SystemSnapshot snapshot)
    {
        long sum = 0;
        var volumes = 0;
        var unknown = new List<string>();

        foreach (var device in snapshot.Storage)
        {
            foreach (var volume in device.Volumes)
            {
                volumes++;
                if (volume.FreeBytes.HasValue)
                {
                    sum += (long)volume.FreeBytes.Value!.Value;
                }
                else
                {
                    unknown.Add(volume.DriveLetter.IsKnown ? volume.DriveLetter.Value! : "unnamed volume");
                }
            }
        }

        if (volumes == 0)
        {
            return Measured<long>.NotAvailable("no volume was reported");
        }

        if (unknown.Count > 0)
        {
            return Measured<long>.NotAvailable($"the free space of {unknown.Count} volume(s) was not reported ({string.Join(", ", unknown.Take(3))})");
        }

        return Measured<long>.Known(sum, ValueOrigin.Create(DataSource.WindowsApi, SensorQuality.High, snapshot.CapturedAt, "Win32_LogicalDisk FreeSpace"));
    }

    private static string Describe(Measured<long>? value) =>
        value is null || value.Value.IsUnknown ? "UNKNOWN" : $"{value.Value.Value} bytes";

    // ---------- helpers ----------------------------------------------------------------------------

    private string NextOperationId() =>
        $"M27-{_operationCounter.ToString("X", CultureInfo.InvariantCulture)}-{Interlocked.Increment(ref _sequence):D3}";

    private int _sequence;

    private static OneClickStep Begin(List<OneClickStep> steps, OneClickPhase phase)
    {
        var index = steps.FindIndex(candidate =>
            candidate.IsPhaseStep && candidate.Phase == phase && candidate.Outcome == StageOutcome.Running);
        if (index >= 0)
        {
            return steps[index];
        }

        var step = NewPhaseStep(phase);
        steps.Add(step);
        return step;
    }

    private static OneClickStep NewPhaseStep(OneClickPhase phase) => new()
    {
        Phase = phase,
        IsPhaseStep = true,
        What = LocalizedText.Of(WhatKey(phase)),
        Why = LocalizedText.Of(WhyKey(phase)),
        Risk = RiskOf(phase),
        Outcome = StageOutcome.Running,
        StartedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Replaces exactly the step instance that was handed out. The comparison is by reference on
    /// purpose: records compare by value, and two planned lines for the same category with the same
    /// size would otherwise be indistinguishable - completing one would overwrite the other.
    /// </summary>
    private static void Complete(List<OneClickStep> steps, OneClickStep step, StageOutcome outcome, string detail)
    {
        var completed = step with { Outcome = outcome, Detail = detail, CompletedAt = DateTimeOffset.UtcNow };
        var index = steps.FindIndex(candidate => ReferenceEquals(candidate, step));
        if (index >= 0)
        {
            steps[index] = completed;
        }
        else
        {
            steps.Add(completed);
        }
    }

    /// <summary>Writes the outcome of a phase, reusing the phase's own line so it cannot appear twice.</summary>
    private static void MarkPhase(List<OneClickStep> steps, OneClickPhase phase, StageOutcome outcome, string detail)
    {
        var index = steps.FindIndex(candidate => candidate.IsPhaseStep && candidate.Phase == phase);
        var step = index >= 0 ? steps[index] : NewPhaseStep(phase);
        if (index < 0)
        {
            steps.Add(step);
        }

        Complete(steps, step, outcome, detail);
    }

    private static void MarkRemaining(List<OneClickStep> steps, OneClickPhase from, string reason)
    {
        foreach (var phase in Enum.GetValues<OneClickPhase>().Where(phase => phase >= from))
        {
            MarkPhase(steps, phase, StageOutcome.Skipped, reason);
        }
    }

    private OneClickResult Result(
        SystemState state,
        LocalizedText summary,
        OneClickPlan plan,
        IReadOnlyList<OneClickStep> steps,
        IReadOnlyList<OneClickMeasurement> measurements,
        MaintenanceResult? execution,
        BackupRecord? backup,
        ApprovalRecord? approval,
        ReportArtifact? report,
        IReadOnlyList<string> openPoints,
        string operationId,
        DateTimeOffset started)
    {
        // The list is the evidence of M27-S-001: it goes to the protocol in the same order the phases ran.
        foreach (var step in steps.Where(step => step.IsPhaseStep).OrderBy(step => (int)step.Phase))
        {
            _protocol.Publish(
                ModuleId,
                LocalizedText.Of(step.Outcome == StageOutcome.Succeeded ? "OneClick_Step_Done" : "OneClick_Step_Result"),
                step.Outcome switch
                {
                    StageOutcome.Succeeded => Severity.Success,
                    StageOutcome.Failed => Severity.Error,
                    StageOutcome.Blocked => Severity.Warning,
                    StageOutcome.Cancelled => Severity.Warning,
                    StageOutcome.Skipped => Severity.Info,
                    _ => Severity.Info,
                },
                $"{step.Phase}: {step.Detail ?? "(no detail)"}");
        }

        return new OneClickResult
        {
            OperationId = operationId,
            Plan = plan,
            Steps = steps,
            Measurements = measurements,
            Execution = execution,
            Backup = backup,
            Approval = approval,
            Report = report,
            FinalState = state,
            Summary = summary,
            OpenPoints = openPoints,
            StartedAt = started,
            CompletedAt = _clock.UtcNow,
        };
    }

    /// <summary>
    /// The approval draft of the whole run. It names every planned item, because the maintenance
    /// service checks that the approval belongs to exactly this plan - and because a confirmation
    /// dialog that does not say what will happen would be a hidden action (M27-S-001).
    /// </summary>
    private static ApprovalRequestDraft DraftFor(MaintenancePlan plan, string operationId) => new()
    {
        OperationId = operationId,
        Operation = OperationKind.Maintenance,
        Category = ComponentCategory.Maintenance,
        Action = LocalizedText.Of("OneClick_Approval_Action"),
        What = LocalizedText.Of("OneClick_Approval_What", plan.Items.Count),
        Why = LocalizedText.Of("OneClick_Approval_Why"),
        Risk = plan.Risk,
        RiskSummary = plan.Summary,
        RequiresAdministrator = plan.RequiresAdministrator,
        Steps = plan.Items.Select(item => item.Change).ToList(),
        Preview = plan.Items.Select(item => new ChangePreview
        {
            LabelKey = item.DisplayNameKey,
            OldValue = item.SizeBytes.Display(CultureInfo.InvariantCulture),
            NewValue = LocalizedText.Of("OneClick_Approval_NewValue"),
            Reason = item.Reason,
            Risk = item.Risk,
        }).ToList(),
        Evidence = plan.Items
            .Select(item => $"item={item.ItemId} root={item.RootPath ?? "n/a"} safety={item.SafetyClass}")
            .ToList(),
        Backup = new BackupAvailability
        {
            RequiredLevel = plan.BackupRequired ? plan.Risk : RiskLevel.Low,
            RestorePointPossible = false,
            ConfigurationBackupAvailable = plan.BackupRecordId is not null,
            Summary = LocalizedText.Of(plan.BackupRequired ? "OneClick_Backup_Planned" : "OneClick_Backup_NotRequired"),
            Details = new[]
            {
                plan.BackupRecordId is null
                    ? "no backup record exists for this plan yet"
                    : $"backup record {plan.BackupRecordId} exists for this plan",
            },
        },
    };

    private async Task AuditAsync(string operationId, StageOutcome outcome, string detail, CancellationToken cancellationToken)
    {
        await _audit.RecordAsync(
                OperationKind.Maintenance,
                operationId,
                ComponentCategory.Maintenance,
                outcome,
                error: detail,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static string WhatKey(OneClickPhase phase) => phase switch
    {
        OneClickPhase.Discovery => "OneClick_Discovery_What",
        OneClickPhase.Diagnostic => "OneClick_Diagnostic_What",
        OneClickPhase.Plan => "OneClick_Plan_What",
        OneClickPhase.Approval => "OneClick_Approval_What",
        OneClickPhase.Backup => "OneClick_Backup_What",
        OneClickPhase.Execution => "OneClick_Execution_What",
        OneClickPhase.Validation => "OneClick_Validation_What",
        _ => "OneClick_Report_What",
    };

    private static string WhyKey(OneClickPhase phase) => phase switch
    {
        OneClickPhase.Discovery => "OneClick_Discovery_Why",
        OneClickPhase.Diagnostic => "OneClick_Diagnostic_Why",
        OneClickPhase.Plan => "OneClick_Plan_Why",
        OneClickPhase.Approval => "OneClick_Approval_Why",
        OneClickPhase.Backup => "OneClick_Backup_Why",
        OneClickPhase.Execution => "OneClick_Execution_Why",
        OneClickPhase.Validation => "OneClick_Validation_Why",
        _ => "OneClick_Report_Why",
    };

    /// <summary>Risk of a phase. Reading is low; changing the system is never low.</summary>
    private static RiskLevel RiskOf(OneClickPhase phase) => phase switch
    {
        OneClickPhase.Discovery or OneClickPhase.Diagnostic => RiskLevel.Low,
        OneClickPhase.Validation or OneClickPhase.Report => RiskLevel.Low,
        OneClickPhase.Plan or OneClickPhase.Approval => RiskLevel.Medium,
        _ => RiskLevel.High,
    };
}
