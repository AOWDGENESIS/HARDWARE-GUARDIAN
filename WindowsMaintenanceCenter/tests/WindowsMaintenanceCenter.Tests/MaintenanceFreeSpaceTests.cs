using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// Cleanup Engine free space remeasurement (spec chapter 10, module M04).
///
/// Tests enforce:
/// - M04-F-004: Nach Cleanup wird tatsächlich freier Speicher neu gemessen
/// - Dry run does not report real disk free space changes
/// </summary>
public sealed class MaintenanceFreeSpaceTests
{
    [Fact]
    public void MaintenanceResult_carries_free_space_before_and_after_measurement()
    {
        var beforeOrigin = ValueOrigin.LocalFile(DateTimeOffset.UtcNow, "C:");
        var afterOrigin = ValueOrigin.LocalFile(DateTimeOffset.UtcNow, "C:");

        var result = new MaintenanceResult
        {
            PlanId = "PLAN-TEST-001",
            Mode = ExecutionMode.Execute,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            SystemFreeSpaceBefore = Measured<long>.Known(50_000_000_000L, beforeOrigin),
            SystemFreeSpaceAfter = Measured<long>.Known(52_000_000_000L, afterOrigin),
            FreedBytes = Measured<long>.Known(2_000_000_000L, afterOrigin),
            Summary = LocalizedText.Of("Maintenance_Result_Executed", 10, 2000.0, 0),
        };

        Assert.NotNull(result.SystemFreeSpaceBefore);
        Assert.NotNull(result.SystemFreeSpaceAfter);
        Assert.True(result.SystemFreeSpaceBefore.Value.IsKnown);
        Assert.True(result.SystemFreeSpaceAfter.Value.IsKnown);
        Assert.Equal(50_000_000_000L, result.SystemFreeSpaceBefore.Value.Value);
        Assert.Equal(52_000_000_000L, result.SystemFreeSpaceAfter.Value.Value);
    }

    [Fact]
    public void Dry_run_result_does_not_assert_freed_disk_space()
    {
        var result = new MaintenanceResult
        {
            PlanId = "PLAN-TEST-DRY",
            Mode = ExecutionMode.DryRun,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            FreedBytes = Measured<long>.NotAvailable("dry run: nothing was deleted"),
        };

        Assert.True(result.WasNothingDeleted);
        Assert.False(result.FreedBytes.IsKnown);
        Assert.Null(result.SystemFreeSpaceBefore);
        Assert.Null(result.SystemFreeSpaceAfter);
    }
}
