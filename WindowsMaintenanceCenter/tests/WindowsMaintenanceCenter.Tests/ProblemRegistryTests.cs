using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>Error identifiers and counting (WMC-SPEC chapter 85, e.g. WMC-UPDATE-0042).</summary>
public sealed class ProblemRegistryTests
{
    [Fact]
    public void Problem_ids_follow_prefix_and_are_counted_per_prefix()
    {
        var registry = new ProblemRegistry(new FakeClock());

        var first = registry.Add(Draft(ComponentCategory.Cpu, "WMC-CPU"));
        var second = registry.Add(Draft(ComponentCategory.Cpu, "WMC-CPU"));
        var third = registry.Add(Draft(ComponentCategory.Graphics, "WMC-GPU"));

        Assert.Equal("WMC-CPU-001", first.Id);
        Assert.Equal("WMC-CPU-002", second.Id);
        Assert.Equal("WMC-GPU-001", third.Id);
    }

    [Fact]
    public void Prefix_is_derived_from_the_category_when_not_supplied()
    {
        var registry = new ProblemRegistry(new FakeClock());
        var problem = registry.Add(Draft(ComponentCategory.Bios, prefix: null));
        Assert.StartsWith("WMC-BIOS-", problem.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void New_problems_are_open_and_can_be_updated()
    {
        var registry = new ProblemRegistry(new FakeClock());
        var problem = registry.Add(Draft(ComponentCategory.Storage, "WMC-STORAGE"));
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
        registry.Add(Draft(ComponentCategory.Cpu, "WMC-CPU") with { Severity = Severity.Critical });
        registry.Add(Draft(ComponentCategory.Cpu, "WMC-CPU") with { Severity = Severity.Warning });
        registry.Add(Draft(ComponentCategory.Cpu, "WMC-CPU") with { Severity = Severity.Info });

        var counts = registry.Counts;
        Assert.Equal(1, counts.Critical);
        Assert.Equal(1, counts.Warnings);
        Assert.Equal(1, counts.Information);
        Assert.Equal(3, counts.Total);
    }

    [Fact]
    public void The_theme_of_an_error_identifier_comes_from_the_category()
    {
        // Kapitel 85: WMC-<Thema>-<Nummer>. Das Thema wird abgeleitet, nicht geraten.
        Assert.Equal("WMC-CPU", ProblemIdFactory.CategoryPrefix(ComponentCategory.Cpu));
        Assert.Equal("WMC-UPDATE", ProblemIdFactory.CategoryPrefix(ComponentCategory.Update));
        Assert.Equal("WMC-GENERAL", ProblemIdFactory.CategoryPrefix(ComponentCategory.Unknown));
    }

    [Fact]
    public void An_empty_prefix_is_filled_from_the_category_instead_of_a_placeholder()
    {
        // Vorher war der Standard "GEN": ein Fehler ohne eigene Kennung landete unter einem Thema,
        // das nichts über ihn aussagt.
        var registry = new ProblemRegistry(new FakeClock());
        var problem = registry.Add(new ProblemDraft
        {
            Category = ComponentCategory.Update,
            Severity = Severity.Warning,
            Title = LocalizedText.Of("Problem_Unknown_Title"),
        });

        Assert.StartsWith("WMC-UPDATE-", problem.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void A_vendor_specific_driver_keeps_the_vendor_in_the_identifier()
    {
        Assert.Equal("WMC-DRIVER-NVIDIA", ProblemIdFactory.VendorPrefix("NVIDIA"));
        Assert.Equal("WMC-DRIVER", ProblemIdFactory.VendorPrefix("  "));
    }

    [Fact]
    public void A_finding_carries_the_fields_of_chapter_85()
    {
        var registry = new ProblemRegistry(new FakeClock());
        var problem = registry.Add(Draft(ComponentCategory.Windows, "WMC-WINDOWS") with
        {
            Cause = LocalizedText.Of("Windows_Check_EventLog_Unreadable", "access denied"),
            LogReference = "logs/2026-09-20.json",
        });

        Assert.Equal("WMC-WINDOWS-001", problem.Id);
        Assert.Equal("Windows_Check_EventLog_Unreadable", problem.Cause.Key);
        Assert.Equal("logs/2026-09-20.json", problem.LogReference);
    }

    [Fact]
    public void Without_a_written_log_entry_the_field_stays_empty_instead_of_naming_a_file()
    {
        var registry = new ProblemRegistry(new FakeClock());
        var problem = registry.Add(Draft(ComponentCategory.Cpu, "WMC-CPU"));

        Assert.Null(problem.LogReference);
        Assert.Equal("Problem_Cause_NotDeterminable", problem.Cause.Key);
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
