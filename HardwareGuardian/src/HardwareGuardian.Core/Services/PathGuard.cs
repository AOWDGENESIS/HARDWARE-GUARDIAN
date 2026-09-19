using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Services;

/// <summary>
/// Safety gate for every destructive file operation (spec sections 77 and 80).
/// Fail closed: without an allow list nothing is allowed, without a proven containment
/// nothing is allowed, and a list of protected locations is refused unconditionally.
/// </summary>
public sealed class PathGuard : IPathGuard
{
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    public PathGuardDecision Evaluate(string path, PathGuardIntent intent, IEnumerable<string> allowedRoots)
    {
        var notes = new List<string>();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Deny(null, "PathGuard_Reason_Empty", "empty path");
        }

        if (!TryNormalise(path, out var normalised, out var normaliseReason))
        {
            return Deny(null, "PathGuard_Reason_Invalid", normaliseReason);
        }

        var roots = new List<string>();
        foreach (var root in allowedRoots ?? Array.Empty<string>())
        {
            if (TryNormalise(root, out var normalisedRoot, out _))
            {
                roots.Add(normalisedRoot);
            }
            else
            {
                notes.Add($"ignored invalid allow list entry: {root}");
            }
        }

        if (roots.Count == 0)
        {
            return Deny(normalised, "PathGuard_Reason_NoAllowedRoot", "allow list is empty", notes);
        }

        var containingRoot = roots.FirstOrDefault(root => IsStrictlyInside(normalised, root));
        if (containingRoot is null)
        {
            notes.Add($"candidate={normalised}");
            notes.Add("allowed roots=" + string.Join(", ", roots));
            return Deny(normalised, "PathGuard_Reason_OutsideAllowedRoots", "path is outside every allowed root", notes);
        }

        if (IsProtected(normalised, out var protectedReason))
        {
            return new PathGuardDecision
            {
                Allowed = false,
                IsProtected = true,
                Path = normalised,
                ReasonCode = "PathGuard_Reason_Protected",
                Reason = LocalizedText.Of("PathGuard_Reason_Protected", protectedReason ?? "protected location"),
                Notes = notes,
            };
        }

        // Never operate on the allow list root itself: only on its contents.
        if (roots.Any(root => string.Equals(root, normalised, StringComparison.OrdinalIgnoreCase)))
        {
            return Deny(normalised, "PathGuard_Reason_IsAllowedRoot", "operation on an allow list root itself is refused", notes);
        }

        var result = new PathGuardDecision
        {
            Allowed = true,
            Path = normalised,
            ReasonCode = null,
            Reason = LocalizedText.Of("PathGuard_Reason_Allowed", containingRoot),
            Notes = notes,
        };

        // Containment and protection checks above apply to every intent. The intent itself only
        // documents why the caller asked; it must never turn a refusal into an allowance.
        _ = intent;
        return result;
    }

    public bool TryNormalise(string path, out string normalised, out string? reason)
    {
        normalised = string.Empty;
        reason = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "empty path";
            return false;
        }

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            reason = "path contains invalid characters";
            return false;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? string.Empty;
            if (string.Equals(full.TrimEnd(Separators), root.TrimEnd(Separators), StringComparison.OrdinalIgnoreCase))
            {
                reason = "drive or filesystem root";
                return false;
            }

            normalised = full.TrimEnd(Separators);
            if (normalised.Length == 0)
            {
                reason = "path normalised to empty";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            reason = ex.Message;
            return false;
        }
    }

    /// <summary>True when candidate is inside root (never equal to root).</summary>
    public static bool IsStrictlyInside(string candidate, string root)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(root))
        {
            return false;
        }

        var normalisedRoot = root.TrimEnd(Separators);
        if (string.Equals(candidate, normalisedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return candidate.StartsWith(normalisedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(normalisedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Locations that must never be deleted from, regardless of the allow list.
    /// </summary>
    public static bool IsProtected(string normalisedPath, out string? reason)
    {
        foreach (var (path, label) in ProtectedLocations())
        {
            if (!string.IsNullOrEmpty(path) && string.Equals(normalisedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                reason = label;
                return true;
            }
        }

        // A candidate that contains a protected location is refused as well (e.g. a parent folder).
        foreach (var (path, _) in ProtectedLocations())
        {
            if (!string.IsNullOrEmpty(path) && IsStrictlyInside(path, normalisedPath))
            {
                reason = "contains a protected location";
                return true;
            }
        }

        reason = null;
        return false;
    }

    private static IEnumerable<(string Path, string Label)> ProtectedLocations()
    {
        yield return (SystemRoot(), "Windows directory");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Program Files");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Program Files (x86)");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ProgramData");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "user profile root");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "desktop");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "documents");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "pictures");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "music");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "videos");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "roaming application data root");
        yield return (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "local application data root");
        yield return (Path.Combine(SystemRoot(), "System32"), "System32");
        yield return (Path.Combine(SystemRoot(), "SysWOW64"), "SysWOW64");
        yield return (Path.Combine(SystemRoot(), "Installer"), "Windows Installer cache (required for repair and uninstall)");
        yield return (Path.Combine(SystemRoot(), "WinSxS"), "component store");
        yield return (Path.Combine(SystemRoot(), "Fonts"), "font cache");
        yield return (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft"), "Microsoft shared configuration");
    }

    private static string SystemRoot()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows))
        {
            return windows;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("SystemRoot");
        return string.IsNullOrWhiteSpace(fromEnvironment) ? string.Empty : fromEnvironment;
    }

    private static PathGuardDecision Deny(string? path, string reasonKey, string detail, IReadOnlyList<string>? notes = null) => new()
    {
        Allowed = false,
        Path = path,
        ReasonCode = reasonKey,
        Reason = LocalizedText.Of(reasonKey, detail),
        Notes = notes ?? Array.Empty<string>(),
    };
}
