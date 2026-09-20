using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// Safety gate for every destructive file operation (spec sections 77 and 80).
/// Fail closed: without an allow list nothing is allowed, without a proven containment
/// nothing is allowed, and a list of protected locations is refused unconditionally.
/// </summary>
public sealed class PathGuard : IPathGuard
{
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
    private readonly Func<string, string>? _resolveRealPath;

    /// <summary>Uses the real file system to resolve links.</summary>
    public PathGuard()
    {
    }

    /// <summary>
    /// Test seam: replaces the resolution of links and junctions. The production behaviour is in
    /// <see cref="TryResolveRealPath"/>; this constructor lets a test state "this path resolves to
    /// that location" without needing the privileges to create a link.
    /// </summary>
    public PathGuard(Func<string, string> resolveRealPath) => _resolveRealPath = resolveRealPath;

    public PathGuardDecision Evaluate(string path, PathGuardIntent intent, IEnumerable<string> allowedRoots)
    {
        var notes = new List<string>();

        if (string.IsNullOrWhiteSpace(path))
        {
            return Deny(null, "PathGuard_Reason_Empty", "empty path");
        }

        // A segment that ends in a dot or a space is ambiguous: Windows strips such characters when
        // the path is handed to the file system, so the string the caller checked and the file that
        // is touched would not be the same one. The check therefore has to look at the path as it was
        // written - Path.GetFullPath removes those characters, which is exactly what makes them worth
        // refusing (spec section 77; test A_segment_that_ends_with_a_dot_or_a_space_is_refused).
        if (HasAmbiguousSegment(path))
        {
            return Deny(path, "PathGuard_Reason_AmbiguousSegment", "a path segment ends with a dot or a space");
        }

        if (!TryNormalise(path, out var normalised, out var normaliseReason))
        {
            return Deny(null, "PathGuard_Reason_Invalid", normaliseReason ?? "path could not be normalised");
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

        // The same check on the normalised form: a link target can carry a segment of this kind.
        if (HasAmbiguousSegment(normalised))
        {
            return Deny(normalised, "PathGuard_Reason_AmbiguousSegment", "a path segment ends with a dot or a space", notes);
        }

        // The root of an allowed root is the boundary, not a member of it: an operation on it would
        // touch everything the allow list covers at once. This has to be checked before the
        // containment test, because "strictly inside" is false for the root itself and the message
        // would then claim the path is outside every allowed root - true for the containment test,
        // misleading for the reader (test The_allow_list_root_itself_is_never_deleted).
        if (roots.Any(root => string.Equals(root, normalised, StringComparison.OrdinalIgnoreCase)))
        {
            return Deny(normalised, "PathGuard_Reason_IsAllowedRoot", "operation on an allow list root itself is refused", notes);
        }

        // A link or junction inside the allowed root can point anywhere. Both the path as written
        // and the location it really resolves to must be inside an allowed root and unprotected;
        // otherwise deleting through the link would touch files the allow list never covered.
        var realPath = normalised;
        if (_resolveRealPath is not null)
        {
            realPath = _resolveRealPath(normalised);
        }
        else if (!TryResolveRealPath(normalised, out realPath, out var resolveNote))
        {
            notes.Add(resolveNote ?? "link resolution failed");
            return Deny(normalised, "PathGuard_Reason_Invalid", "the location of the path could not be resolved", notes);
        }

        var realNormalised = realPath;
        if (!string.Equals(realPath, normalised, StringComparison.OrdinalIgnoreCase))
        {
            if (!TryNormalise(realPath, out realNormalised, out var realReason))
            {
                notes.Add($"resolved path is not usable: {realReason}");
                return Deny(normalised, "PathGuard_Reason_Invalid", "the resolved location is not a valid path", notes);
            }

            notes.Add($"path resolves to {realNormalised}");
        }

        var containingRoot = roots.FirstOrDefault(root => IsStrictlyInside(normalised, root));
        if (containingRoot is null)
        {
            notes.Add($"candidate={normalised}");
            notes.Add("allowed roots=" + string.Join(", ", roots));
            return Deny(normalised, "PathGuard_Reason_OutsideAllowedRoots", "path is outside every allowed root", notes);
        }

        var realContainingRoot = roots.FirstOrDefault(root => IsStrictlyInside(realNormalised, root));
        if (realContainingRoot is null)
        {
            notes.Add($"resolved={realNormalised}");
            notes.Add("allowed roots=" + string.Join(", ", roots));
            return Deny(normalised, "PathGuard_Reason_OutsideAllowedRoots", "the location the path points to is outside every allowed root", notes);
        }

        if (IsProtected(realNormalised, out var realProtectedReason))
        {
            return new PathGuardDecision
            {
                Allowed = false,
                IsProtected = true,
                Path = normalised,
                ReasonCode = "PathGuard_Reason_Protected",
                Reason = LocalizedText.Of("PathGuard_Reason_Protected", realProtectedReason ?? "protected location"),
                Notes = notes,
            };
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
        if (roots.Any(root => string.Equals(root, normalised, StringComparison.OrdinalIgnoreCase)
            || string.Equals(root, realNormalised, StringComparison.OrdinalIgnoreCase)))
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

    /// <summary>True when a path segment ends with a dot or a space.</summary>
    public static bool HasAmbiguousSegment(string normalisedPath)
    {
        foreach (var segment in normalisedPath.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
            {
                continue;
            }

            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Follows links and junctions segment by segment and returns the location the path really
    /// points to. Path.GetFullPath only cleans the text up; it does not touch the file system, so a
    /// junction inside an allowed root would otherwise smuggle in a protected location. A segment
    /// that does not exist yet cannot be a link and is kept as it is.
    /// </summary>
    public static bool TryResolveRealPath(string normalisedPath, out string realPath, out string? note)
    {
        realPath = normalisedPath;
        note = null;

        try
        {
            var current = Path.GetPathRoot(normalisedPath) ?? string.Empty;
            if (string.IsNullOrEmpty(current) || current.StartsWith(@"\\", StringComparison.Ordinal))
            {
                // Drive relative paths and UNC shares cannot be resolved segment by segment here;
                // the caller keeps the text form, which the containment checks already cover.
                return true;
            }

            var rest = normalisedPath[current.Length..];
            foreach (var segment in rest.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                var next = Path.Combine(current, segment);

                FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
                if (!info.Exists)
                {
                    current = next;
                    continue;
                }

                if (info.LinkTarget is not null)
                {
                    var target = info.ResolveLinkTarget(returnFinalTarget: true);
                    if (target is null)
                    {
                        note = $"link at {next} could not be resolved";
                        return false;
                    }

                    next = target.FullName;
                }

                current = next;
            }

            realPath = current;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
        {
            note = $"link resolution failed: {ex.GetType().Name}";
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
