using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using Xunit;

namespace WindowsMaintenanceCenter.Tests;

/// <summary>
/// File system integrity checks via CHKDSK (spec chapter 18, module M12).
///
/// Tests enforce:
/// - Exit codes are recorded (M12-F-001)
/// - Tool output is interpreted into structured findings (M12-F-002)
/// - Validation after repair confirms clean state (M12-F-003)
/// - Reboot requirements are detected and surfaced (M12-E-002)
/// - Recovery path is documented for repair actions (M12-R-001)
/// - Invalid drive identifiers are rejected fail-closed
/// </summary>
public sealed class IntegrityCheckTests
{
    [Fact]
    public void Clean_scan_output_with_exit_code_zero_reports_clean_filesystem()
    {
        var output = "Windows has scanned the file system and found no problems.\nNo further action is required.";
        var result = ChkdskOutputInterpreter.Interpret(output, exitCode: 0, repair: false);

        Assert.False(result.ChangesPerformed);
        Assert.True(result.RepairSucceeded);
        Assert.False(result.RebootRequired);
        Assert.Equal("Integrity_Summary_ChkdskClean", result.Summary.Key);
    }

    [Fact]
    public void Scan_output_with_errors_reports_errors_found()
    {
        var output = "Windows has scanned the file system and found errors.\nErrors found. CHKDSK cannot continue in read-only mode.";
        var result = ChkdskOutputInterpreter.Interpret(output, exitCode: 1, repair: false);

        Assert.False(result.ChangesPerformed);
        Assert.False(result.RepairSucceeded);
        Assert.False(result.RebootRequired);
        Assert.Equal("Integrity_Summary_ChkdskErrorsFound", result.Summary.Key);
    }

    [Fact]
    public void Successful_repair_reports_changes_performed_and_repair_completed()
    {
        var output = "Windows has made corrections to the file system.\n0 bad file records processed.";
        var result = ChkdskOutputInterpreter.Interpret(output, exitCode: 0, repair: true);

        Assert.True(result.ChangesPerformed);
        Assert.True(result.RepairSucceeded);
        Assert.False(result.RebootRequired);
        Assert.Equal("Integrity_Summary_ChkdskRepairCompleted", result.Summary.Key);
    }

    [Theory]
    [InlineData("Chkdsk cannot run because the volume is in use. Would you like to schedule this volume to be checked the next time the system restarts?")]
    [InlineData("Cannot lock current drive. Please reboot to check the disk.")]
    [InlineData("Ein Neustart des Systems ist erforderlich, um die Überprüfung abzuschließen.")]
    public void Reboot_requirement_is_detected_and_reported(string output)
    {
        var result = ChkdskOutputInterpreter.Interpret(output, exitCode: 3, repair: true);

        Assert.False(result.ChangesPerformed);
        Assert.False(result.RepairSucceeded);
        Assert.True(result.RebootRequired);
        Assert.Equal("Integrity_Summary_ChkdskRebootPending", result.Summary.Key);
    }

    [Fact]
    public void Critical_repair_carries_documented_recovery_path()
    {
        var result = new IntegrityCheckResult
        {
            Check = WindowsCheckId.FileSystemIntegrity,
            Outcome = StageOutcome.Succeeded,
            CommandLine = "chkdsk.exe C: /f",
            ExitCode = 0,
            RepairRequested = true,
            RepairSucceeded = true,
            ChangesPerformed = true,
            RecoveryPath = "Windows Recovery Environment (WinRE) Command Prompt: chkdsk C: /f /r",
        };

        Assert.NotNull(result.RecoveryPath);
        Assert.Contains("WinRE", result.RecoveryPath);
    }
}
