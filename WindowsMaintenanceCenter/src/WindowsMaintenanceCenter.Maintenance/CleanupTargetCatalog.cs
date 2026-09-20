using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Maintenance;

/// <summary>
/// The documented Windows locations that Windows Maintenance Center may measure and - after approval -
/// clean. Every entry names its source: an environment folder, <c>SpecialFolder</c> or the
/// Windows directory. Nothing is derived from a guess, and there is no entry that touches user
/// documents, browser profiles beyond their cache directories, or the registry (spec section 18).
/// </summary>
public sealed record CleanupTarget
{
    public required MaintenanceCategory Category { get; init; }

    public required string DisplayNameKey { get; init; }

    /// <summary>Absolute root path. Empty when the location does not exist on this machine.</summary>
    public required string Root { get; init; }

    public required SafetyClass SafetyClass { get; init; }

    public required LocalizedText Description { get; init; }

    public bool RequiresAdministrator { get; init; }

    /// <summary>False for targets that are measured but never cleaned automatically.</summary>
    public bool IsCleanable { get; init; } = true;

    /// <summary>Set when the target must not be cleaned, together with the technical reason.</summary>
    public LocalizedText? ProtectionReason { get; init; }

    /// <summary>Optional file pattern filter, e.g. <c>thumbcache_*.db</c>. Empty means all files.</summary>
    public string FilePattern { get; init; } = "*";

    /// <summary>Name of the environment source the path came from, for the evidence line.</summary>
    public required string SourceNote { get; init; }

    /// <summary>Files younger than this are left alone, so that running programs are not disturbed.</summary>
    public TimeSpan MinimumAge { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Documented child directory that occurs once per profile (for example the Firefox cache).
    /// When set, the root is expanded into one location per existing child directory.
    /// </summary>
    public string? WildcardChildDirectory { get; init; }

    /// <summary>Optional command template that reports the real size (used for Delivery Optimization).</summary>
    public string? MeasurementTemplate { get; init; }
}

/// <summary>
/// Builds the cleanup target list for the current machine. Documented paths only; a path that
/// cannot be resolved on this system is reported as unavailable instead of being invented.
/// </summary>
public static class CleanupTargetCatalog
{
    public static IReadOnlyList<CleanupTarget> ForCurrentMachine(bool includeBrowserCache, bool includePrefetch, bool includeWindowsUpdateCache)
    {
        var targets = new List<CleanupTarget>();

        // --- Temporary files ---------------------------------------------------------------
        AddDirectory(targets, MaintenanceCategory.TemporaryFiles, "Maintenance_TemporaryFiles_Name", "Temp",
            "Maintenance_TemporaryFiles_Description", SafetyClass.Safe, requiresAdmin: false,
            sourceNote: "Path.GetTempPath()");
        AddDirectory(targets, MaintenanceCategory.TemporaryFiles, "Maintenance_TemporaryFiles_Name", "WindowsTemp",
            "Maintenance_TemporaryFiles_Description_System", SafetyClass.Safe, requiresAdmin: true,
            sourceNote: "%SystemRoot%\\Temp");

        // --- Windows Update cache ----------------------------------------------------------
        if (includeWindowsUpdateCache)
        {
            AddDirectory(targets, MaintenanceCategory.WindowsUpdateCache, "Maintenance_UpdateCache_Name", "WindowsUpdateDownload",
                "Maintenance_UpdateCache_Description", SafetyClass.Optional, requiresAdmin: true,
                sourceNote: "%SystemRoot%\\SoftwareDistribution\\Download",
                protectionReason: "Maintenance_UpdateCache_Reason",
                note: "Windows must not be updating while this folder is cleaned; the folder itself is kept.");
        }

        // --- Delivery Optimization cache ---------------------------------------------------
        targets.Add(new CleanupTarget
        {
            Category = MaintenanceCategory.DeliveryOptimizationCache,
            DisplayNameKey = "Maintenance_DeliveryOptimization_Name",
            Root = string.Empty,
            SafetyClass = SafetyClass.Optional,
            Description = LocalizedText.Of("Maintenance_DeliveryOptimization_Description"),
            RequiresAdministrator = true,
            IsCleanable = true,
            SourceNote = "Get-DeliveryOptimizationStatus / Delete-DeliveryOptimizationCache",
            MeasurementTemplate = InfrastructureCommandTemplates.DeliveryOptimizationSize,
            FilePattern = string.Empty,
        });

        // --- Crash dumps --------------------------------------------------------------------
        AddDirectory(targets, MaintenanceCategory.CrashDumps, "Maintenance_CrashDumps_Name", "CrashDumps",
            "Maintenance_CrashDumps_Description", SafetyClass.Safe, requiresAdmin: false,
            sourceNote: "%LOCALAPPDATA%\\CrashDumps");

        // --- Error reports (WER) ------------------------------------------------------------
        AddDirectory(targets, MaintenanceCategory.ErrorReports, "Maintenance_ErrorReports_Name", "WerMachineArchive",
            "Maintenance_ErrorReports_Description", SafetyClass.Safe, requiresAdmin: true,
            sourceNote: "%PROGRAMDATA%\\Microsoft\\Windows\\WER\\ReportArchive");
        AddDirectory(targets, MaintenanceCategory.ErrorReports, "Maintenance_ErrorReports_Name", "WerMachineQueue",
            "Maintenance_ErrorReports_Description", SafetyClass.Safe, requiresAdmin: true,
            sourceNote: "%PROGRAMDATA%\\Microsoft\\Windows\\WER\\ReportQueue");
        AddDirectory(targets, MaintenanceCategory.ErrorReports, "Maintenance_ErrorReports_Name", "WerUserArchive",
            "Maintenance_ErrorReports_Description", SafetyClass.Safe, requiresAdmin: false,
            sourceNote: "%LOCALAPPDATA%\\Microsoft\\Windows\\WER\\ReportArchive");

        // --- Thumbnail cache (files are usually locked, therefore opt-in and measured only) --
        AddDirectory(targets, MaintenanceCategory.ThumbnailCache, "Maintenance_ThumbnailCache_Name", "ExplorerThumbnails",
            "Maintenance_ThumbnailCache_Description", SafetyClass.Optional, requiresAdmin: false,
            sourceNote: "%LOCALAPPDATA%\\Microsoft\\Windows\\Explorer\\thumbcache_*.db",
            filePattern: "thumbcache_*.db",
            protectionReason: "Maintenance_ThumbnailCache_Reason");

        // --- Shader caches (vendor locations, documented) ------------------------------------
        AddDirectory(targets, MaintenanceCategory.ShaderCache, "Maintenance_ShaderCache_Name", "NvidiaDxCache",
            "Maintenance_ShaderCache_Description", SafetyClass.Optional, requiresAdmin: false,
            sourceNote: "%LOCALAPPDATA%\\NVIDIA\\DXCache");
        AddDirectory(targets, MaintenanceCategory.ShaderCache, "Maintenance_ShaderCache_Name", "NvidiaGlCache",
            "Maintenance_ShaderCache_Description", SafetyClass.Optional, requiresAdmin: false,
            sourceNote: "%LOCALAPPDATA%\\NVIDIA\\GLCache");
        AddDirectory(targets, MaintenanceCategory.ShaderCache, "Maintenance_ShaderCache_Name", "AmdDxCache",
            "Maintenance_ShaderCache_Description", SafetyClass.Optional, requiresAdmin: false,
            sourceNote: "%LOCALAPPDATA%\\AMD\\DxCache");

        // --- Browser caches (opt-in, cache directories only, never profile data) -------------
        if (includeBrowserCache)
        {
            AddDirectory(targets, MaintenanceCategory.BrowserCache, "Maintenance_BrowserCache_Name", "EdgeCache",
                "Maintenance_BrowserCache_Description", SafetyClass.Optional, requiresAdmin: false,
                sourceNote: "%LOCALAPPDATA%\\Microsoft\\Edge\\User Data\\Default\\Cache",
                note: "Only the cache directory is measured; cookies, history and sessions are never touched.");
            AddDirectory(targets, MaintenanceCategory.BrowserCache, "Maintenance_BrowserCache_Name", "ChromeCache",
                "Maintenance_BrowserCache_Description", SafetyClass.Optional, requiresAdmin: false,
                sourceNote: "%LOCALAPPDATA%\\Google\\Chrome\\User Data\\Default\\Cache",
                note: "Only the cache directory is measured; cookies, history and sessions are never touched.");
            AddDirectory(targets, MaintenanceCategory.BrowserCache, "Maintenance_BrowserCache_Name", "FirefoxCache",
                "Maintenance_BrowserCache_Description", SafetyClass.Optional, requiresAdmin: false,
                sourceNote: "%LOCALAPPDATA%\\Mozilla\\Firefox\\Profiles\\*\\cache2",
                wildcardChild: "cache2");
        }

        // --- Protected locations: measured and explained, never cleaned ----------------------
        AddDirectory(targets, MaintenanceCategory.PrefetchedData, "Maintenance_Prefetch_Name", "Prefetch",
            "Maintenance_Prefetch_Description", SafetyClass.Protected, requiresAdmin: true,
            sourceNote: "%SystemRoot%\\Prefetch",
            protectionReason: "Maintenance_Prefetch_Reason",
            cleanable: includePrefetch);

        AddDirectory(targets, MaintenanceCategory.WindowsInstallerCache, "Maintenance_InstallerCache_Name", "WindowsInstaller",
            "Maintenance_InstallerCache_Description", SafetyClass.Protected, requiresAdmin: true,
            sourceNote: "%SystemRoot%\\Installer",
            protectionReason: "Maintenance_InstallerCache_Reason",
            cleanable: false);

        AddDirectory(targets, MaintenanceCategory.RecycleBin, "Maintenance_RecycleBin_Name", "RecycleBin",
            "Maintenance_RecycleBin_Description", SafetyClass.Protected, requiresAdmin: false,
            sourceNote: "%SystemDrive%\\$Recycle.Bin",
            protectionReason: "Maintenance_RecycleBin_Reason",
            cleanable: false);

        // --- Installer leftovers / old logs: not offered -------------------------------------
        //  InstallerLeftovers and OldLogs are deliberately absent: a safe automatic distinction
        //  between "leftover" and "still required" cannot be proven, therefore Windows Maintenance Center
        //  does not offer these categories at all (fail closed, spec sections 1.3 and 91).

        return targets;
    }

    /// <summary>Categories the application can report on, including the ones it refuses to clean.</summary>
    public static IReadOnlyList<(MaintenanceCategory Category, string NameKey, LocalizedText Description, SafetyClass Safety, bool RequiresAdmin, bool IsOptIn, LocalizedText? Restriction, bool IsOffered)> DescribeCategories() => new[]
    {
        (MaintenanceCategory.TemporaryFiles, "Maintenance_TemporaryFiles_Name", LocalizedText.Of("Maintenance_TemporaryFiles_Description"), SafetyClass.Safe, false, false, null, true),
        (MaintenanceCategory.WindowsUpdateCache, "Maintenance_UpdateCache_Name", LocalizedText.Of("Maintenance_UpdateCache_Description"), SafetyClass.Optional, true, true, LocalizedText.Of("Maintenance_UpdateCache_Reason"), true),
        (MaintenanceCategory.DeliveryOptimizationCache, "Maintenance_DeliveryOptimization_Name", LocalizedText.Of("Maintenance_DeliveryOptimization_Description"), SafetyClass.Optional, true, true, LocalizedText.Of("Maintenance_DeliveryOptimization_Reason"), true),
        (MaintenanceCategory.RecycleBin, "Maintenance_RecycleBin_Name", LocalizedText.Of("Maintenance_RecycleBin_Description"), SafetyClass.Protected, false, true, LocalizedText.Of("Maintenance_RecycleBin_Reason"), true),
        (MaintenanceCategory.CrashDumps, "Maintenance_CrashDumps_Name", LocalizedText.Of("Maintenance_CrashDumps_Description"), SafetyClass.Safe, false, false, null, true),
        (MaintenanceCategory.ErrorReports, "Maintenance_ErrorReports_Name", LocalizedText.Of("Maintenance_ErrorReports_Description"), SafetyClass.Safe, false, false, null, true),
        (MaintenanceCategory.ThumbnailCache, "Maintenance_ThumbnailCache_Name", LocalizedText.Of("Maintenance_ThumbnailCache_Description"), SafetyClass.Optional, false, true, LocalizedText.Of("Maintenance_ThumbnailCache_Reason"), true),
        (MaintenanceCategory.ShaderCache, "Maintenance_ShaderCache_Name", LocalizedText.Of("Maintenance_ShaderCache_Description"), SafetyClass.Optional, false, true, LocalizedText.Of("Maintenance_ShaderCache_Reason"), true),
        (MaintenanceCategory.BrowserCache, "Maintenance_BrowserCache_Name", LocalizedText.Of("Maintenance_BrowserCache_Description"), SafetyClass.Optional, false, true, LocalizedText.Of("Maintenance_BrowserCache_Reason"), true),
        (MaintenanceCategory.PrefetchedData, "Maintenance_Prefetch_Name", LocalizedText.Of("Maintenance_Prefetch_Description"), SafetyClass.Protected, true, true, LocalizedText.Of("Maintenance_Prefetch_Reason"), true),
        (MaintenanceCategory.WindowsInstallerCache, "Maintenance_InstallerCache_Name", LocalizedText.Of("Maintenance_InstallerCache_Description"), SafetyClass.Protected, true, true, LocalizedText.Of("Maintenance_InstallerCache_Reason"), true),
    };

    private static void AddDirectory(
        List<CleanupTarget> targets,
        MaintenanceCategory category,
        string nameKey,
        string id,
        string descriptionKey,
        SafetyClass safety,
        bool requiresAdmin,
        string sourceNote,
        string filePattern = "*",
        bool cleanable = true,
        string? wildcardChild = null,
        string? protectionReason = null,
        string? note = null)
    {
        var root = ResolveRoot(id);
        targets.Add(new CleanupTarget
        {
            Category = category,
            DisplayNameKey = nameKey,
            Root = root,
            SafetyClass = safety,
            Description = LocalizedText.Of(descriptionKey),
            RequiresAdministrator = requiresAdmin,
            IsCleanable = cleanable,
            ProtectionReason = protectionReason is null ? null : LocalizedText.Of(protectionReason),
            FilePattern = filePattern,
            WildcardChildDirectory = wildcardChild,
            SourceNote = note is null ? sourceNote : $"{sourceNote} ({note})",
        });
    }

    private static string ResolveRoot(string id) => id switch
    {
        "Temp" => Path.GetTempPath(),
        "WindowsTemp" => Combine(SpecialFolder.Windows, "Temp"),
        "WindowsUpdateDownload" => Combine(SpecialFolder.Windows, @"SoftwareDistribution\Download"),
        "CrashDumps" => Combine(SpecialFolder.LocalApplicationData, "CrashDumps"),
        "WerMachineArchive" => Combine(SpecialFolder.CommonApplicationData, @"Microsoft\Windows\WER\ReportArchive"),
        "WerMachineQueue" => Combine(SpecialFolder.CommonApplicationData, @"Microsoft\Windows\WER\ReportQueue"),
        "WerUserArchive" => Combine(SpecialFolder.LocalApplicationData, @"Microsoft\Windows\WER\ReportArchive"),
        "ExplorerThumbnails" => Combine(SpecialFolder.LocalApplicationData, @"Microsoft\Windows\Explorer"),
        "NvidiaDxCache" => Combine(SpecialFolder.LocalApplicationData, @"NVIDIA\DXCache"),
        "NvidiaGlCache" => Combine(SpecialFolder.LocalApplicationData, @"NVIDIA\GLCache"),
        "AmdDxCache" => Combine(SpecialFolder.LocalApplicationData, @"AMD\DxCache"),
        "EdgeCache" => Combine(SpecialFolder.LocalApplicationData, @"Microsoft\Edge\User Data\Default\Cache"),
        "ChromeCache" => Combine(SpecialFolder.LocalApplicationData, @"Google\Chrome\User Data\Default\Cache"),
        "FirefoxCache" => Combine(SpecialFolder.LocalApplicationData, @"Mozilla\Firefox\Profiles"),
        "Prefetch" => Combine(SpecialFolder.Windows, "Prefetch"),
        "WindowsInstaller" => Combine(SpecialFolder.Windows, "Installer"),
        "RecycleBin" => Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? string.Empty, "$Recycle.Bin"),
        _ => string.Empty,
    };

    private static string Combine(Environment.SpecialFolder folder, string relative)
    {
        var basePath = Environment.GetFolderPath(folder);
        return string.IsNullOrEmpty(basePath) ? string.Empty : Path.Combine(basePath, relative);
    }
}

/// <summary>Command template identifiers used by the maintenance layer.</summary>
internal static class InfrastructureCommandTemplates
{
    /// <summary>Matches the allow-listed PowerShell template for the Delivery Optimization cache.</summary>
    public const string DeliveryOptimizationSize = "deliveryoptimization.cache.size";

    public const string DeliveryOptimizationClear = "deliveryoptimization.cache.clear";
}
