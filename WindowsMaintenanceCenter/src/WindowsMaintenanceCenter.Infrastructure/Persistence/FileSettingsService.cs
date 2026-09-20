using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using Microsoft.Extensions.Logging;

namespace WindowsMaintenanceCenter.Infrastructure.Persistence;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON (spec sections 40, 72). Unknown or out of range
/// values are clamped to the safe default instead of being accepted silently.
/// </summary>
public sealed class FileSettingsService : ISettingsService
{
    private readonly IPathProvider _paths;
    private readonly ILogger<FileSettingsService>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppSettings _current = new();

    public FileSettingsService(IPathProvider paths, ILogger<FileSettingsService>? logger = null)
    {
        _paths = paths;
        _logger = logger;
    }

    public event EventHandler<AppSettings>? Changed;

    public AppSettings Current => _current;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureDirectory(_paths.ConfigurationDirectory);
            AppSettings? loaded = null;
            try
            {
                loaded = await JsonFileStore.ReadAsync<AppSettings>(_paths.SettingsFilePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Settings file could not be read; defaults are used.");
            }

            _current = Sanitise(loaded ?? new AppSettings());
            _paths.SetReportDirectoryOverride(_current.ReportDirectory);
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sanitised = Sanitise(settings);
            await JsonFileStore.WriteAsync(_paths.SettingsFilePath, sanitised, cancellationToken).ConfigureAwait(false);
            _current = sanitised;
            _paths.SetReportDirectoryOverride(_current.ReportDirectory);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, _current);
    }

    public async Task<AppSettings> UpdateAsync(Func<AppSettings, AppSettings> mutator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mutator);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        AppSettings updated;
        try
        {
            updated = Sanitise(mutator(_current));
            await JsonFileStore.WriteAsync(_paths.SettingsFilePath, updated, cancellationToken).ConfigureAwait(false);
            _current = updated;
            _paths.SetReportDirectoryOverride(_current.ReportDirectory);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, _current);
        return _current;
    }

    /// <summary>Applies documented bounds. Telemetry can never be enabled by a settings file.</summary>
    public static AppSettings Sanitise(AppSettings settings) => settings with
    {
        SchemaVersion = Math.Max(1, settings.SchemaVersion),
        LogLevel = NormaliseLogLevel(settings.LogLevel),
        LogRetentionDays = Math.Clamp(settings.LogRetentionDays, 1, 3650),
        CacheRetentionDays = Math.Clamp(settings.CacheRetentionDays, 1, 365),
        HistoryRetentionEntries = Math.Clamp(settings.HistoryRetentionEntries, 10, 50_000),
        SensorIntervalMilliseconds = Math.Clamp(settings.SensorIntervalMilliseconds, 500, 60_000),
        TelemetryEnabled = false,
    };

    private static string NormaliseLogLevel(string? level)
    {
        var allowed = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None" };
        if (string.IsNullOrWhiteSpace(level))
        {
            return "Information";
        }

        var match = allowed.FirstOrDefault(a => string.Equals(a, level.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? "Information";
    }
}
