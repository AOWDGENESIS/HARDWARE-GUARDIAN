using WindowsMaintenanceCenter.Core.Abstractions;

namespace WindowsMaintenanceCenter.App.Services;

/// <summary>
/// Access point for the active localizer. XAML value converters run without a dependency injection
/// container, so the localizer is published here once at startup. It is only ever set by
/// <c>App</c>; nothing else may replace it, which is why the setter is intentionally explicit.
/// </summary>
public static class AppServices
{
    public static ILocalizer? CurrentLocalizer { get; set; }
}
