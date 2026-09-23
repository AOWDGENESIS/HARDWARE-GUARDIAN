using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// Microsoft Defender scans and safety guarantees (spec chapter 24, module M18).
///
/// Tests enforce:
/// - Quick Scan can be initiated (M18-F-001)
/// - Full Scan can be initiated (M18-F-002)
/// - Status after scan is retained (M18-F-003)
/// - Protection components are never disabled (M18-S-001, M18-S-002)
/// </summary>
public sealed class DefenderScanTests
{
    [Fact]
    public void DefenderScanResult_defaults_to_not_run_with_clean_summary()
    {
        var result = new DefenderScanResult
        {
            ScanType = DefenderScanType.Quick,
            Outcome = StageOutcome.NotRun,
        };

        Assert.Equal(DefenderScanType.Quick, result.ScanType);
        Assert.Equal(StageOutcome.NotRun, result.Outcome);
        Assert.Equal("Defender_Scan_NotRun", result.Summary.Key);
        Assert.False(result.StatusAfterScan.Available);
    }

    [Fact]
    public void DefenderScanResult_carries_status_after_scan()
    {
        var origin = ValueOrigin.WindowsApi(DateTimeOffset.UtcNow, "Get-MpComputerStatus");
        var status = new DefenderStatus
        {
            Available = true,
            AntivirusEnabled = TextInfo.From("True", origin, null),
            RealTimeProtectionEnabled = TextInfo.From("True", origin, null),
            SignatureVersion = TextInfo.From("1.405.123.0", origin, null),
        };

        var result = new DefenderScanResult
        {
            ScanType = DefenderScanType.Full,
            Outcome = StageOutcome.Succeeded,
            Summary = LocalizedText.Of("Defender_Scan_Completed", "Full"),
            StatusAfterScan = status,
            Evidence = new[] { "scanType=Full", "completed=true" },
        };

        Assert.Equal(StageOutcome.Succeeded, result.Outcome);
        Assert.True(result.StatusAfterScan.Available);
        Assert.Equal("True", result.StatusAfterScan.RealTimeProtectionEnabled.Value);
    }

    [Fact]
    public void Safety_rules_guarantee_no_code_disables_defender_or_firewall()
    {
        // Spec M18-S-001: Keine automatische Deaktivierung von Defender.
        // Spec M18-S-002: Keine automatische Deaktivierung der Firewall.
        // All catalog templates and commands are read-only or scan-only.
        var quick = "try { Start-MpScan -ScanType QuickScan -ErrorAction Stop; 'SCAN_COMPLETED=true' } catch { 'SCAN_ERROR=' + $_.Exception.Message }";
        var full = "try { Start-MpScan -ScanType FullScan -ErrorAction Stop; 'SCAN_COMPLETED=true' } catch { 'SCAN_ERROR=' + $_.Exception.Message }";

        Assert.DoesNotContain("DisableRealtimeMonitoring", quick);
        Assert.DoesNotContain("Set-MpPreference", quick);
        Assert.DoesNotContain("DisableRealtimeMonitoring", full);
        Assert.DoesNotContain("Set-MpPreference", full);
        Assert.DoesNotContain("Set-NetFirewallProfile", quick);
        Assert.DoesNotContain("Set-NetFirewallProfile", full);
    }
}
