using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// Overall status (spec section 22). The important part is the fail-closed case: a module that ran
/// no checks must not produce "healthy".
/// </summary>
public sealed class OverallStatusCalculatorTests
{
    [Fact]
    public void Critical_wins_over_everything_else()
    {
        var status = OverallStatusCalculator.Calculate(
            new[] { Problem(Severity.Warning), Problem(Severity.Critical), Problem(Severity.Info) },
            new[] { Module(HealthStatus.Healthy, checks: 3) },
            out _);

        Assert.Equal(HealthStatus.Critical, status);
    }

    [Fact]
    public void Module_without_executed_checks_keeps_the_overall_state_unknown()
    {
        var status = OverallStatusCalculator.Calculate(
            Array.Empty<Problem>(),
            new[] { Module(HealthStatus.Unknown, checks: 0) },
            out var summary);

        Assert.Equal(HealthStatus.Unknown, status);
        Assert.Equal("Overall_Unknown", summary.Key);
    }

    [Fact]
    public void Skipped_modules_are_reported_but_do_not_fake_a_problem()
    {
        var skipped = new ModuleResult
        {
            ModuleId = "WIN-HEALTH",
            DisplayNameKey = "Module_Windows",
            Status = HealthStatus.Unknown,
            WasSkipped = true,
            SkipReason = LocalizedText.Of("Module_Skipped_Offline"),
            SkipReasonCode = BlockReasons.OfflineMode,
            ChecksExecuted = 0,
        };

        var status = OverallStatusCalculator.Calculate(
            Array.Empty<Problem>(),
            new[] { Module(HealthStatus.Healthy, checks: 4), skipped },
            out var summary);

        Assert.Equal(HealthStatus.Healthy, status);
        Assert.Equal("Overall_Healthy_WithSkips", summary.Key);
    }

    [Fact]
    public void A_warning_problem_results_in_attention()
    {
        var status = OverallStatusCalculator.Calculate(
            new[] { Problem(Severity.Warning) },
            new[] { Module(HealthStatus.Healthy, checks: 1) },
            out var summary);

        Assert.Equal(HealthStatus.Attention, status);
        Assert.Equal("Overall_Attention", summary.Key);
    }

    [Fact]
    public void An_error_problem_results_in_warning()
    {
        var status = OverallStatusCalculator.Calculate(
            new[] { Problem(Severity.Error) },
            new[] { Module(HealthStatus.Healthy, checks: 1) },
            out var summary);

        Assert.Equal(HealthStatus.Warning, status);
        Assert.Equal("Overall_Warning", summary.Key);
    }

    [Theory]
    [InlineData(Severity.Critical, HealthStatus.Critical)]
    [InlineData(Severity.Error, HealthStatus.Warning)]
    [InlineData(Severity.Warning, HealthStatus.Attention)]
    [InlineData(Severity.Blocked, HealthStatus.Attention)]
    [InlineData(Severity.Info, HealthStatus.Unknown)]
    public void Severity_mapping_is_stable(Severity severity, HealthStatus expected) =>
        Assert.Equal(expected, OverallStatusCalculator.FromSeverity(severity));

    private static Problem Problem(Severity severity) => new()
    {
        Id = "HW-CPU-001",
        Category = ComponentCategory.Cpu,
        Severity = severity,
        Title = LocalizedText.Of("Problem_Unknown_Title"),
        Description = LocalizedText.Of("Problem_Unknown_Description"),
        Impact = LocalizedText.Of("Problem_Unknown_Impact"),
        RecommendedAction = LocalizedText.Of("Problem_Unknown_Action"),
    };

    private static ModuleResult Module(HealthStatus status, int checks) => new()
    {
        ModuleId = "TEST",
        DisplayNameKey = "Module_System",
        Status = status,
        ChecksExecuted = checks,
    };
}
