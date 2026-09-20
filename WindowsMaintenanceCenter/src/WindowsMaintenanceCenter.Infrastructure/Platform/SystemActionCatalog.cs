using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Infrastructure.Platform;

/// <summary>
/// The actions this version registers (WMC specification, chapters 32, 38 and 96).
///
/// Two groups are visible here, and the difference is deliberate:
///
/// * Released actions (<see cref="RegisteredAction.Allowed"/> is true) are read-only system queries
///   that every Windows installation answers. They need no administrator rights, they change
///   nothing, and their exit code says "answered" or "did not answer" - so nothing can be reported
///   as success that is not one.
/// * Registered but not released actions are the repair and check tools (DISM, SFC, CHKDSK). They
///   are declared so that the interface can show them and refuse them (chapter 96) instead of
///   pretending they do not exist. They stay not released until
///     - the approval and backup gate is wired in front of them (chapters 30, 44), and
///     - their exit codes are confirmed on a real system, because some of these tools answer with a
///       code of their own when they find something. Until that is measured, a non-zero code is
///       reported as a failed run, and that would be a wrong statement about the run.
///
/// Reference for the tool names: the programs are part of Windows itself (dism.exe, sfc.exe,
/// chkdsk.exe, fsutil.exe, ipconfig.exe, systeminfo.exe). None of them is downloaded and none of
/// them comes from a third party.
/// </summary>
public static class SystemActionCatalog
{
    /// <summary>Identifier area for Windows itself.</summary>
    public const string SystemArea = "System";

    /// <summary>Identifier area for network queries.</summary>
    public const string NetworkArea = "Network";

    /// <summary>Identifier area for power queries.</summary>
    public const string PowerArea = "Power";

    /// <summary>Identifier area for volumes.</summary>
    public const string VolumeArea = "Volume";

    /// <summary>Every action of this version, released ones first.</summary>
    public static IReadOnlyList<RegisteredAction> Create() => new[]
    {
        // ---------------------------------------------------------------- released, read only
        new RegisteredAction
        {
            Id = Identifier(SystemArea, "FsutilDeleteNotifyQuery"),
            Description = LocalizedText.Of("Action_FsutilDeleteNotifyQuery"),
            Risk = RiskLevel.Low,
            RequiresAdmin = false,
            SelfValidating = true,
            Timeout = TimeSpan.FromMinutes(1),
            Executable = "fsutil.exe",
            ArgumentTemplate = new[] { "behavior", "query", "DisableDeleteNotify" },
        },
        new RegisteredAction
        {
            Id = Identifier(NetworkArea, "IpConfigAll"),
            Description = LocalizedText.Of("Action_IpConfigAll"),
            Risk = RiskLevel.Low,
            RequiresAdmin = false,
            SelfValidating = true,
            Timeout = TimeSpan.FromMinutes(1),
            Executable = "ipconfig.exe",
            ArgumentTemplate = new[] { "/all" },
        },
        new RegisteredAction
        {
            Id = Identifier(SystemArea, "SystemInfoSnapshot"),
            Description = LocalizedText.Of("Action_SystemInfoSnapshot"),
            Risk = RiskLevel.Low,
            RequiresAdmin = false,
            SelfValidating = true,
            Timeout = TimeSpan.FromMinutes(5),
            Executable = "systeminfo.exe",
            ArgumentTemplate = new[] { "/fo", "list" },
        },

        // ------------------------------------------------- registered, deliberately not released
        new RegisteredAction
        {
            Id = Identifier(SystemArea, "DismScanHealth"),
            Description = LocalizedText.Of("Action_DismScan"),
            Risk = RiskLevel.Medium,
            RequiresAdmin = true,
            SelfValidating = true,
            Allowed = false,
            Timeout = TimeSpan.FromMinutes(30),
            Executable = "dism.exe",
            ArgumentTemplate = new[] { "/Online", "/Cleanup-Image", "/ScanHealth" },
        },
        new RegisteredAction
        {
            Id = Identifier(SystemArea, "DismRestoreHealth"),
            Description = LocalizedText.Of("Action_DismRepair"),
            Risk = RiskLevel.High,
            RequiresAdmin = true,
            // This one changes system files, so it needs a secured state and an approval for this very
            // action before it may ever run (chapters 30 and 44).
            RequiresApproval = true,
            RequiresBackup = true,
            Allowed = false,
            Timeout = TimeSpan.FromMinutes(60),
            Executable = "dism.exe",
            ArgumentTemplate = new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" },
        },
        new RegisteredAction
        {
            Id = Identifier(SystemArea, "SfcVerifyOnly"),
            Description = LocalizedText.Of("Action_SfcVerify"),
            Risk = RiskLevel.Medium,
            RequiresAdmin = true,
            SelfValidating = true,
            Allowed = false,
            Timeout = TimeSpan.FromMinutes(60),
            Executable = "sfc.exe",
            ArgumentTemplate = new[] { "/verifyonly" },
        },
        new RegisteredAction
        {
            Id = Identifier(SystemArea, "SfcScanNow"),
            Description = LocalizedText.Of("Action_SfcScanNow"),
            Risk = RiskLevel.High,
            RequiresAdmin = true,
            RequiresApproval = true,
            RequiresBackup = true,
            Allowed = false,
            Timeout = TimeSpan.FromMinutes(60),
            Executable = "sfc.exe",
            ArgumentTemplate = new[] { "/scannow" },
        },
        new RegisteredAction
        {
            Id = Identifier(VolumeArea, "ChkdskScan"),
            Description = LocalizedText.Of("Action_ChkdskScan"),
            Risk = RiskLevel.Medium,
            RequiresAdmin = true,
            SelfValidating = true,
            Allowed = false,
            Timeout = TimeSpan.FromMinutes(120),
            Executable = "chkdsk.exe",
            Arguments = new[]
            {
                new ActionArgumentSpec
                {
                    Name = "Volume",
                    Kind = ActionArgumentKind.Path,
                    Required = true,
                    MaxLength = 3,
                    AllowSpaces = false,
                },
            },
            ArgumentTemplate = new[] { "{Volume}", "/scan" },
        },
    };

    /// <summary>Identifier form of chapter 32: <c>Area.WhatItDoes</c>.</summary>
    public static string Identifier(string area, string what) => RegisteredActionFactory.Identifier(area, what);
}
