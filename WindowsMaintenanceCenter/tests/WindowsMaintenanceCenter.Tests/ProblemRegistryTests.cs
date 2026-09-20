using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>Problem identifiers and counting (spec section 23, e.g. HW-CPU-001).</summary>
public sealed class ProblemRegistryTests
{
    [Fact]
    public void Problem_ids_follow_prefix_and_are_counted_per_prefix()
    {
        var registry = new ProblemRegistry(new FakeClock());

        var first = registry.Add(Draft(ComponentCategory.Cpu, "HW-CPU"));
        var second = registry.Add(Draft(ComponentCategory.Cpu, "HW-CPU"));
        var third = registry.Add(Draft(ComponentCategory.Graphics, "HW-GPU"));

        Assert.Equal("HW-CPU-001", first.Id);
        Assert.Equal("HW-CPU-002", second.Id);
        Assert.Equal("HW-GPU-001", third.Id);
    }

    [Fact]
    public void Prefix_is_derived_from_the_category_when_not_supplied()
    {
        var registry = new ProblemRegistry(new FakeClock());
        var problem = registry.Add(Draft(ComponentCategory.Bios, prefix: null));
        Assert.StartsWith("BIOS-", problem.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void New_problems_are_open_and_can_be_updated()
    {
        var registry = new ProblemRegistry(new FakeClock());
        var problem = registry.Add(Draft(ComponentCategory.Storage, "HW-STORAGE"));
        Assert.Equal(ProblemStatus.Open, problem.Status);

        registry.UpdateStatus(problem.Id, ProblemStatus.Acknowledged, "seen");
        Assert.Equal(ProblemStatus.Acknowledged, registry.Find(problem.Id)!.Status);
    }

    [Fact]
    public void Blocked_operations_are_carried_into_the_problem()
    {
        var registry = new ProblemRegistry(new FakeClock());
        var draft = Draft(ComponentCategory.Windows, "WIN") with
        {
            BlockedOperation = new BlockedOperation
            {
                OperationId = "dism-repair",
                ReasonCode = BlockReasons.NotElevated,
                Reason = LocalizedText.Of("Admin_Reason_SystemIntegrity"),
            },
        };

        var problem = registry.Add(draft);
        var blocked = Assert.Single(problem.BlockedOperations);
        Assert.Equal(BlockReasons.NotElevated, blocked.ReasonCode);
    }

    [Fact]
    public void Counting_matches_the_severities()
    {
        var registry = new ProblemRegistry(new FakeClock());
        registry.Add(Draft(ComponentCategory.Cpu, "HW-CPU") with { Severity = Severity.Critical });
        registry.Add(Draft(ComponentCategory.Cpu, "HW-CPU") with { Severity = Severity.Warning });
        registry.Add(Draft(ComponentCategory.Cpu, "HW-CPU") with { Severity = Severity.Info });

        var counts = registry.Counts;
        Assert.Equal(1, counts.Critical);
        Assert.Equal(1, counts.Warnings);
        Assert.Equal(1, counts.Information);
        Assert.Equal(3, counts.Total);
    }

    private static ProblemDraft Draft(ComponentCategory category, string? prefix) => new()
    {
        IdPrefix = prefix ?? string.Empty,
        Category = category,
        Severity = Severity.Warning,
        Title = LocalizedText.Of("Problem_Unknown_Title"),
        Description = LocalizedText.Of("Problem_Unknown_Description"),
        Evidence = "test evidence",
    };
}
