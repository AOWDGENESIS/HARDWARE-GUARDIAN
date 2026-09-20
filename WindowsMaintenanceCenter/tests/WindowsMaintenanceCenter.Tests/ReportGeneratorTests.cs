using System.Text.Json;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Infrastructure.Localization;
using WindowsMaintenanceCenter.Infrastructure.Security;
using WindowsMaintenanceCenter.Reporting;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

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
        Assert.Contains("WMC-CPU-001", content, StringComparison.Ordinal);
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
        Assert.Equal("WMC-CPU-001", document.RootElement.GetProperty("snapshot").GetProperty("problems")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Json_report_carries_the_measured_hardware_inventory()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);

        var artifact = await generator.GenerateAsync(ReportRequest(), ReportFormat.Json, Options(), CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(artifact.FilePath));

        var hardware = document.RootElement.GetProperty("snapshot").GetProperty("hardware");
        Assert.Equal("SIMULATION — Test CPU", hardware.GetProperty("processors")[0].GetProperty("name").GetString());
        Assert.Equal("DDR4 (SMBIOS code 26)", hardware.GetProperty("memory").GetProperty("modules")[0].GetProperty("memoryType").GetString());
        Assert.Equal("DIMM (SMBIOS code 8)", hardware.GetProperty("memory").GetProperty("modules")[0].GetProperty("formFactor").GetString());
        Assert.Equal("Healthy", hardware.GetProperty("storage")[0].GetProperty("healthStatus").GetString());
    }

    [Fact]
    public async Task A_value_that_was_not_measured_keeps_its_reason_in_the_report()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);

        var artifact = await generator.GenerateAsync(ReportRequest(), ReportFormat.Json, Options(), CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(artifact.FilePath));

        var hardware = document.RootElement.GetProperty("snapshot").GetProperty("hardware");
        var graphics = hardware.GetProperty("graphics")[0];
        // The fixture reports no video memory. The report must not show 0 and must not show an
        // empty string - it has to name the reason (spec sections 1.3 and 61).
        Assert.StartsWith("UNKNOWN: ", graphics.GetProperty("videoMemoryBytes").GetString(), StringComparison.Ordinal);
        Assert.NotEqual("0", graphics.GetProperty("videoMemoryBytes").GetString());
        Assert.Equal("UNKNOWN: not reported", graphics.GetProperty("isIntegratedGraphics").GetString());
    }

    [Fact]
    public async Task Mac_and_ip_addresses_are_masked_in_the_report()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);

        var artifact = await generator.GenerateAsync(
            ReportRequest(),
            ReportFormat.Json,
            Options() with { MaskSerialNumbers = true },
            CancellationToken.None);

        var content = await File.ReadAllTextAsync(artifact.FilePath);
        Assert.DoesNotContain("AA:BB:CC:DD:EE:FF", content, StringComparison.Ordinal);
        Assert.DoesNotContain("192.168.1.50", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_report_contains_a_hardware_section()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);

        var artifact = await generator.GenerateAsync(ReportRequest(), ReportFormat.Text, Options(), CancellationToken.None);
        var content = await File.ReadAllTextAsync(artifact.FilePath);

        Assert.Contains("Hardware", content, StringComparison.Ordinal);
        Assert.Contains("Memory type: DDR4 (SMBIOS code 26)", content, StringComparison.Ordinal);
        Assert.Contains("UNKNOWN:", content, StringComparison.Ordinal);
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

    /// <summary>
    /// SEC-14 (report injection): a value that comes from outside must not be able to steer the
    /// report. A control character in a device name or in a finding's evidence is shown as its code
    /// point instead of being obeyed, an HTML report escapes markup instead of running it, and the
    /// JSON report stays parseable because the serialiser escapes the same characters.
    /// </summary>
    [Fact]
    public async Task A_foreign_value_cannot_steer_the_report()
    {
        using var paths = new TempPathProvider();
        var generator = CreateGenerator(paths);
        var baseRequest = ReportRequest();
        var baseSnapshot = baseRequest.Snapshot;
        Assert.NotNull(baseSnapshot);

        var injected = baseRequest with
        {
            Snapshot = baseSnapshot with
            {
                Components = baseSnapshot.Components
                    .Select(component => component with
                    {
                        Name = TextInfo.Known("SIM\u001b[2JULATION\r\nWMC-CPU-999", component.Name.Origin),
                    })
                    .ToArray(),
                Problems = new[]
                {
                    new Problem
                    {
                        Id = "WMC-CPU-001",
                        Category = ComponentCategory.Cpu,
                        Severity = Severity.Critical,
                        Status = ProblemStatus.Open,
                        Title = LocalizedText.Of("Problem_Unknown_Title"),
                        Description = LocalizedText.Of("Problem_Unknown_Description"),
                        Impact = LocalizedText.Of("Problem_Unknown_Impact"),
                        RecommendedAction = LocalizedText.Of("Problem_Unknown_Action"),
                        Evidence = "line 1\u001b[31mline 2\u0007<script>alert(1)</script>",
                        LogReference = "WMC-CPU-001\r\nWMC-FAKE-000",
                        DetectedAt = new FakeClock().Now,
                    },
                },
            },
        };

        var text = await generator.GenerateAsync(injected, ReportFormat.Text, Options(), CancellationToken.None);
        var content = await File.ReadAllTextAsync(text.FilePath);

        // No escape, no bell, no carriage return *inside* a line: the finding cannot move the cursor.
        // The line breaks of the document itself are structure and stay, so the check looks at lines.
        var lines = content.Split('\n');
        Assert.All(lines, line => Assert.DoesNotContain('\u001b', line));
        Assert.All(lines, line => Assert.DoesNotContain('\u0007', line));
        Assert.All(lines, line => Assert.DoesNotContain('\r', line));
        Assert.Contains("\\u001B", content);

        // The injected carriage return must not have started a line of its own: a value cannot add a
        // record to the report.
        Assert.DoesNotContain("\nWMC-CPU-999", content);

        var html = await generator.GenerateAsync(injected, ReportFormat.Html, Options(), CancellationToken.None);
        var markup = await File.ReadAllTextAsync(html.FilePath);

        Assert.DoesNotContain("<script>", markup);
        Assert.Contains("&lt;script&gt;", markup);
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
                Processors = new[]
                {
                    new ProcessorInfo
                    {
                        Name = TextInfo.Known("SIMULATION — Test CPU", origin),
                        Manufacturer = TextInfo.Known("Test Vendor", origin),
                        Cores = Measured<uint>.Known(6, origin),
                        LogicalProcessors = Measured<uint>.Known(12, origin),
                        CurrentClockMhz = Measured<uint>.Known(3900, origin),
                    },
                },
                Memory = new MemoryInfo
                {
                    TotalPhysicalBytes = Measured<ulong>.Known(24UL * 1024 * 1024 * 1024, origin),
                    TotalSlots = Measured<uint>.Known(2, origin),
                    UsedSlots = Measured<uint>.Known(2, origin),
                    Modules = new[]
                    {
                        new MemoryModuleInfo
                        {
                            BankLabel = TextInfo.Known("BANK 0", origin),
                            DeviceLocator = TextInfo.Known("DIMM 0", origin),
                            CapacityBytes = Measured<ulong>.Known(12UL * 1024 * 1024 * 1024, origin),
                            SpeedMhz = Measured<uint>.Known(3200, origin),
                            MemoryType = TextInfo.Known("DDR4 (SMBIOS code 26)", origin),
                            FormFactor = TextInfo.Known("DIMM (SMBIOS code 8)", origin),
                            Manufacturer = TextInfo.Known("Test Vendor", origin),
                        },
                    },
                },
                Graphics = new[]
                {
                    new GraphicsAdapterInfo
                    {
                        Name = TextInfo.Known("SIMULATION — Test GPU", origin),
                        // deliberately not measured: the report has to say so
                        VideoMemoryBytes = Measured<ulong>.NotAvailable("video memory not reported"),
                        DriverVersion = TextInfo.Known("31.0.15.4601", origin),
                    },
                },
                Storage = new[]
                {
                    new StorageDeviceInfo
                    {
                        Model = TextInfo.Known("SIMULATION — Test SSD", origin),
                        BusType = TextInfo.Known("NVMe", origin),
                        SizeBytes = Measured<ulong>.Known(1_000_204_886_016UL, origin),
                        HealthStatus = TextInfo.Known("Healthy", origin),
                        SmartAvailable = true,
                        IsNvme = true,
                    },
                },
                Network = new[]
                {
                    new NetworkAdapterInfo
                    {
                        Description = TextInfo.Known("SIMULATION — Test Ethernet", origin),
                        MacAddress = TextInfo.Known("AA:BB:CC:DD:EE:FF", origin),
                        IpAddress = TextInfo.Known("192.168.1.50", origin),
                        ConnectionState = TextInfo.Known("Connected", origin),
                    },
                },
                Problems = new[]
                {
                    new Problem
                    {
                        Id = "WMC-CPU-001",
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
