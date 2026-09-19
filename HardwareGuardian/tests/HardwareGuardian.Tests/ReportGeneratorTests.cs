using System.Text.Json;
using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;
using HardwareGuardian.Infrastructure.Localization;
using HardwareGuardian.Infrastructure.Security;
using HardwareGuardian.Reporting;
using Xunit;

namespace HardwareGuardian.Tests;

/// <summary>
/// Report generation (spec sections 56 and 57). The tests check the fail-closed format handling and
/// that a report really carries the provenance of the values it shows.
/// </summary>
public sealed class ReportGeneratorTests
{
    [Fact]
    public async Task Unsupported_format_is_blocked_and_no_file_is_written()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);
        var request = ReportRequest();

        var error = await Assert.ThrowsAsync<OperationBlockedException>(
            () => generator.GenerateAsync(request, ReportFormat.Pdf, Options(), CancellationToken.None));

        Assert.Equal(BlockReasons.UnsupportedPlatform, error.ReasonCode);
        Assert.False(Directory.Exists(paths.ReportDirectory) && Directory.GetFiles(paths.ReportDirectory).Length > 0);
    }

    [Fact]
    public async Task Pdf_is_not_advertised_as_supported()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);

        Assert.DoesNotContain(ReportFormat.Pdf, generator.SupportedFormats);
        Assert.Contains(ReportFormat.Html, generator.SupportedFormats);
        Assert.Contains(ReportFormat.Json, generator.SupportedFormats);
        Assert.Contains(ReportFormat.Text, generator.SupportedFormats);
    }

    [Fact]
    public async Task Text_report_contains_problem_id_evidence_and_origin()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);

        var artifact = await generator.GenerateAsync(ReportRequest(), ReportFormat.Text, Options(), CancellationToken.None);
        var content = await File.ReadAllTextAsync(artifact.FilePath);

        Assert.Equal(ReportFormat.Text, artifact.Format);
        Assert.True(artifact.SizeBytes > 0);
        Assert.Contains("HW-CPU-001", content, StringComparison.Ordinal);
        Assert.Contains("synthetic evidence", content, StringComparison.Ordinal);
        Assert.Contains("WMI", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_report_is_machine_readable_and_marked_as_simulation()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);

        var artifact = await generator.GenerateAsync(ReportRequest(), ReportFormat.Json, Options(), CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(artifact.FilePath));

        Assert.Equal("system", document.RootElement.GetProperty("reportType").GetString());
        Assert.True(document.RootElement.GetProperty("snapshot").GetProperty("isSimulation").GetBoolean());
        Assert.Equal("HW-CPU-001", document.RootElement.GetProperty("snapshot").GetProperty("problems")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Serial_numbers_are_masked_when_configured()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);

        var artifact = await generator.GenerateAsync(
            ReportRequest(),
            ReportFormat.Json,
            Options() with { MaskSerialNumbers = true },
            CancellationToken.None);

        var content = await File.ReadAllTextAsync(artifact.FilePath);
        Assert.DoesNotContain("SUBSYS_12345678", content, StringComparison.Ordinal);
        Assert.Contains("5678", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_report_hash_is_computed_for_the_written_file()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);

        var artifact = await generator.GenerateAsync(ReportRequest(), ReportFormat.Text, Options(), CancellationToken.None);

        Assert.True(artifact.Hash.Succeeded);
        Assert.Equal("SHA256", artifact.Hash.Algorithm);
        Assert.Equal(new FileInfo(artifact.FilePath).Length, artifact.SizeBytes);
    }

    private static ReportGenerator CreateGenerator(TempPathProvider paths) => new(
        paths,
        new HashService(new FakeClock()),
        new JsonLocalizer(preference: LanguagePreference.English),
        new FakeEnvironmentProbe(),
        new FakeClock());

    private static ReportOptions Options() => new()
    {
        IncludeEvidence = true,
        MaskSerialNumbers = false,
        MaskUserName = false,
    };

    private static ReportRequest ReportRequest()
    {
        var clock = new FakeClock();
        var origin = ValueOrigin.Wmi(clock.Now, "Win32_Processor");

        return new ReportRequest
        {
            Snapshot = new SystemSnapshot
            {
                Id = "SNAP-TEST",
                CapturedAt = clock.Now,
                IsSimulation = true,
                OverallStatus = HealthStatus.Critical,
                OverallSummary = LocalizedText.Of("Overall_Critical", 1),
                Components = new[]
                {
                    new HardwareComponent
                    {
                        Id = "cpu-1",
                        Category = ComponentCategory.Cpu,
                        CategoryKey = "Component_Cpu",
                        Name = TextInfo.Known("SIMULATION — Test CPU", origin),
                        Manufacturer = TextInfo.Known("Test Vendor", origin),
                        Model = TextInfo.Known("Test Model", origin),
                        DeviceInstanceId = TextInfo.Known("PCI\\VEN_1002&DEV_1638&SUBSYS_12345678", origin),
                        Status = HealthStatus.Critical,
                    },
                },
                Problems = new[]
                {
                    new Problem
                    {
                        Id = "HW-CPU-001",
                        Category = ComponentCategory.Cpu,
                        Severity = Severity.Critical,
                        Status = ProblemStatus.Open,
                        Title = LocalizedText.Of("Problem_Unknown_Title"),
                        Description = LocalizedText.Of("Problem_Unknown_Description"),
                        Impact = LocalizedText.Of("Problem_Unknown_Impact"),
                        RecommendedAction = LocalizedText.Of("Problem_Unknown_Action"),
                        Evidence = "synthetic evidence",
                        DetectedAt = clock.Now,
                    },
                },
            },
        };
    }
}
