using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using HardwareGuardian.Core;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.App.Services;

/// <summary>Maps a health status to a brush from the active theme. No meaning is invented here.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            HealthStatus.Healthy => "BrushHealthy",
            HealthStatus.Attention => "BrushAttention",
            HealthStatus.Warning => "BrushWarning",
            HealthStatus.Critical => "BrushCritical",
            StageOutcome.Succeeded => "BrushHealthy",
            StageOutcome.Failed => "BrushCritical",
            StageOutcome.Blocked => "BrushBlocked",
            StageOutcome.Skipped => "BrushAttention",
            StageOutcome.Cancelled => "BrushAttention",
            _ => "BrushUnknown",
        };

        return Lookup(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static object Lookup(string key) =>
        Application.Current?.TryFindResource(key) ?? Brushes.Gray;
}

/// <summary>Maps a severity to a brush from the active theme.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            Severity.Critical => "BrushCritical",
            Severity.Error => "BrushCritical",
            Severity.Warning => "BrushWarning",
            Severity.Success => "BrushHealthy",
            Severity.Blocked => "BrushBlocked",
            _ => "BrushUnknown",
        };

        return Application.Current?.TryFindResource(key) ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Resolves a <see cref="LocalizedText"/> through the active localizer. The view model raises a
/// change for the whole view model when the language changes, so the converter runs again.
/// </summary>
public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LocalizedText text => Resolve(text),
        string key when !string.IsNullOrWhiteSpace(key) => key.StartsWith("[[", StringComparison.Ordinal) ? key : LookupString(key),
        null => string.Empty,
        _ => value.ToString() ?? string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string Resolve(LocalizedText text)
    {
        var localizer = AppServices.CurrentLocalizer;
        return localizer is null ? text.Key : localizer.Resolve(text);
    }

    private static string LookupString(string key)
    {
        var localizer = AppServices.CurrentLocalizer;
        return localizer is null ? key : localizer[key];
    }
}

/// <summary>
/// Renders a measured value as "value unit" or as the reason why it is unknown. A missing value is
/// never rendered as 0.
/// </summary>
public sealed class MeasuredConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var unit = parameter as string ?? string.Empty;
        switch (value)
        {
            case Measured<int> i:
                return i.HasValue ? $"{i.Value!.Value.ToString(culture)}{unit}" : $"UNKNOWN ({i.UnknownReason})";
            case Measured<long> l:
                return l.HasValue ? $"{l.Value!.Value.ToString(culture)}{unit}" : $"UNKNOWN ({l.UnknownReason})";
            case Measured<uint> u:
                return u.HasValue ? $"{u.Value!.Value.ToString(culture)}{unit}" : $"UNKNOWN ({u.UnknownReason})";
            case Measured<ulong> ul:
                return ul.HasValue ? $"{ul.Value!.Value.ToString(culture)}{unit}" : $"UNKNOWN ({ul.UnknownReason})";
            case Measured<double> d:
                return d.HasValue ? $"{d.Value!.Value.ToString("0.##", culture)}{unit}" : $"UNKNOWN ({d.UnknownReason})";
            case TextInfo text:
                return text.IsKnown ? text.Display : $"UNKNOWN ({text.UnknownReason ?? "not reported"})";
            case null:
                return "UNKNOWN";
            default:
                return value.ToString() ?? "UNKNOWN";
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
