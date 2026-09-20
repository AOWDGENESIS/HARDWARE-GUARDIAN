using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Maintenance;

/// <summary>
/// Result of measuring a cleanup location. <see cref="EligibleBytes"/> is the part that a clean-up
/// would actually remove (files older than the minimum age), so the UI can never promise more than
/// it will free.
/// </summary>
public sealed record DirectoryMeasurement
{
    public bool RootExists { get; init; }

    public Measured<long> TotalBytes { get; init; } = Measured<long>.NotAvailable("not measured");

    public Measured<int> TotalFiles { get; init; } = Measured<int>.NotAvailable("not measured");

    public Measured<long> EligibleBytes { get; init; } = Measured<long>.NotAvailable("not measured");

    public Measured<int> EligibleFiles { get; init; } = Measured<int>.NotAvailable("not measured");

    /// <summary>Number of entries that could not be read (locked or access denied).</summary>
    public int SkippedCount { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    public static DirectoryMeasurement Missing(string root) => new()
    {
        RootExists = false,
        Notes = new[] { $"location does not exist: {root}" },
    };
}

/// <summary>
/// Read-only directory measurement and file enumeration for the maintenance engine.
/// Reparse points are never followed, the depth is bounded and unreadable entries are counted
/// instead of throwing (spec sections 77 and 80).
/// </summary>
/// <summary>
/// Counts entries that could not be read. An iterator method cannot take a <c>ref</c> parameter, so
/// the count lives in an object that the caller owns.
/// </summary>
public sealed class SkipTally
{
    public int Count { get; private set; }

    public void Add() => Count++;
}

public static class DirectoryMeasurer
{
    public static DirectoryMeasurement Measure(string root, string pattern, TimeSpan minimumAge, int maxDepth, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return DirectoryMeasurement.Missing(root);
        }

        var origin = ValueOrigin.LocalFile(now, root);
        long totalBytes = 0;
        var totalFiles = 0;
        long eligibleBytes = 0;
        var eligibleFiles = 0;
        var tally = new SkipTally();
        var notes = new List<string>();

        foreach (var (path, length, lastWrite) in Enumerate(root, pattern, maxDepth, notes, tally, cancellationToken))
        {
            totalBytes += length;
            totalFiles++;

            if (now - lastWrite >= minimumAge)
            {
                eligibleBytes += length;
                eligibleFiles++;
            }
        }

        notes.Add($"measured={totalFiles} file(s); eligible (older than {minimumAge.TotalHours:0} h)={eligibleFiles} file(s)");

        return new DirectoryMeasurement
        {
            RootExists = true,
            TotalBytes = Measured<long>.Known(totalBytes, origin),
            TotalFiles = Measured<int>.Known(totalFiles, origin),
            EligibleBytes = Measured<long>.Known(eligibleBytes, origin),
            EligibleFiles = Measured<int>.Known(eligibleFiles, origin),
            SkippedCount = tally.Count,
            Notes = notes,
        };
    }

    /// <summary>
    /// Enumerates the files that a clean-up would consider. The caller must still let every single
    /// path pass the <see cref="WindowsMaintenanceCenter.Core.Abstractions.IPathGuard"/> before deleting it.
    /// </summary>
    public static IReadOnlyList<string> EnumerateFiles(string root, string pattern, TimeSpan minimumAge, int maxDepth, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var tally = new SkipTally();
        var result = new List<string>();

        foreach (var item in Enumerate(root, pattern, maxDepth, notes, tally, cancellationToken))
        {
            if (now - item.LastWrite >= minimumAge)
            {
                result.Add(item.Path);
            }
        }

        return result;
    }

    private static IEnumerable<(string Path, long Length, DateTimeOffset LastWrite)> Enumerate(
        string root,
        string pattern,
        int maxDepth,
        List<string> notes,
        SkipTally skipped,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var effectivePattern = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, depth) = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current, effectivePattern, SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException or System.Security.SecurityException)
            {
                skipped.Add();
                notes.Add($"{current}: {ex.GetType().Name}");
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        skipped.Add();
                        continue;
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    skipped.Add();
                    continue;
                }

                long length;
                try
                {
                    length = info.Length;
                }
                catch (IOException)
                {
                    skipped.Add();
                    continue;
                }

                yield return (file, length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
            }

            if (depth >= maxDepth)
            {
                continue;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException or System.Security.SecurityException)
            {
                skipped.Add();
                notes.Add($"{current}: {ex.GetType().Name}");
                continue;
            }

            foreach (var directory in directories)
            {
                try
                {
                    var attributes = File.GetAttributes(directory);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        // Junctions and symlinks are never followed: they could point anywhere.
                        notes.Add($"reparse point skipped: {directory}");
                        continue;
                    }
                }
                catch (Exception)
                {
                    continue;
                }

                pending.Push((directory, depth + 1));
            }
        }
    }
}
