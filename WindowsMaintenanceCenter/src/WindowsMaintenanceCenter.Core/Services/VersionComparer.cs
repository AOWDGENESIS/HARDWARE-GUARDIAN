using System.Globalization;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>Result of comparing two version strings.</summary>
public enum VersionComparison
{
    /// <summary>One side is empty or the values cannot be compared meaningfully.</summary>
    Unknown,

    /// <summary>The available version is older than the installed one.</summary>
    AvailableIsOlder,

    /// <summary>Both sides are equal.</summary>
    Same,

    /// <summary>The available version is newer.</summary>
    AvailableIsNewer,

    /// <summary>Both sides are well formed but of different shapes (e.g. "F67" vs "1.2.3").</summary>
    NotComparable,
}

/// <summary>
/// Version string comparison for drivers and firmware. A newer number is deliberately NOT
/// treated as a reason to install anything (spec sections 14 and 49); it only feeds the
/// decision engine, which additionally requires source, compatibility and verification.
/// </summary>
public static class VersionComparer
{
    /// <summary>Splits a version into its numeric and textual tokens.</summary>
    public static IReadOnlyList<long> ParseNumericSegments(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return Array.Empty<long>();
        }

        var segments = new List<long>();
        var current = new System.Text.StringBuilder();

        foreach (var character in version)
        {
            if (char.IsDigit(character))
            {
                current.Append(character);
                continue;
            }

            if (current.Length > 0)
            {
                segments.Add(long.Parse(current.ToString(), CultureInfo.InvariantCulture));
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            segments.Add(long.Parse(current.ToString(), CultureInfo.InvariantCulture));
        }

        return segments;
    }

    /// <summary>Leading letters of a firmware style version, e.g. "F" for "F67".</summary>
    public static string ParsePrefix(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var prefix = new System.Text.StringBuilder();
        foreach (var character in version.Trim())
        {
            if (char.IsLetter(character))
            {
                prefix.Append(char.ToUpperInvariant(character));
                continue;
            }

            break;
        }

        return prefix.ToString();
    }

    public static VersionComparison Compare(string? installed, string? available)
    {
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(available))
        {
            return VersionComparison.Unknown;
        }

        var leftPrefix = ParsePrefix(installed);
        var rightPrefix = ParsePrefix(available);
        var left = ParseNumericSegments(installed);
        var right = ParseNumericSegments(available);

        if (left.Count == 0 || right.Count == 0)
        {
            return VersionComparison.Unknown;
        }

        if (!string.IsNullOrEmpty(leftPrefix) || !string.IsNullOrEmpty(rightPrefix))
        {
            // Firmware style versions: the letter identifies the product line and must match.
            if (!string.Equals(leftPrefix, rightPrefix, StringComparison.Ordinal))
            {
                return VersionComparison.NotComparable;
            }
        }

        var length = Math.Max(left.Count, right.Count);
        for (var i = 0; i < length; i++)
        {
            var leftValue = i < left.Count ? left[i] : 0;
            var rightValue = i < right.Count ? right[i] : 0;
            if (leftValue == rightValue)
            {
                continue;
            }

            return rightValue > leftValue ? VersionComparison.AvailableIsNewer : VersionComparison.AvailableIsOlder;
        }

        return VersionComparison.Same;
    }

    /// <summary>True only when both sides are comparable and the candidate is newer.</summary>
    public static bool IsNewer(string? installed, string? available) =>
        Compare(installed, available) == VersionComparison.AvailableIsNewer;
}
