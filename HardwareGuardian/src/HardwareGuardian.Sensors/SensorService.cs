using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;
using Microsoft.Extensions.Logging;

namespace HardwareGuardian.Sensors;

/// <summary>
/// Live sensor service (spec section 35). Readings are refreshed on a configurable interval
/// (default 2 s) and every reading keeps its source and quality. Without a vendor sensor driver
/// the application reports unknown values with a reason instead of approximating them.
/// </summary>
public sealed class SensorService : ISensorService, IDisposable
{
    private readonly IReadOnlyList<ISensorProvider> _providers;
    private readonly ILogger<SensorService>? _logger;
    private readonly ISettingsService _settings;
    private CancellationTokenSource? _monitorSource;
    private Task? _monitorTask;

    public SensorService(IEnumerable<ISensorProvider> providers, ISettingsService settings, ILogger<SensorService>? logger = null)
    {
        _providers = providers.ToList();
        _settings = settings;
        _logger = logger;
    }

    public event EventHandler<SensorSnapshot>? Sampled;

    public IReadOnlyList<SensorProviderInfo> Providers => _providers.Select(p => p.Info).ToList();

    public bool IsMonitoring { get; private set; }

    public TimeSpan Interval { get; private set; } = TimeSpan.FromSeconds(2);

    public async Task<SensorSnapshot> SampleAsync(CancellationToken cancellationToken)
    {
        var readings = new List<SensorReading>();
        var providers = new List<SensorProviderInfo>();

        foreach (var provider in _providers)
        {
            providers.Add(provider.Info);
            try
            {
                var produced = await provider.ReadAsync(cancellationToken).ConfigureAwait(false);
                readings.AddRange(produced);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Sensor provider {Provider} failed", provider.Info.Id);
                readings.Add(SensorReading.NotAvailable(
                    $"provider-{provider.Info.Id}",
                    provider.Info.DisplayNameKey,
                    ComponentCategory.Sensor,
                    string.Empty,
                    $"{ex.GetType().Name}: {ex.Message}",
                    "SensorPoint_Provider"));
            }
        }

        var snapshot = new SensorSnapshot
        {
            SampledAt = DateTimeOffset.Now,
            Readings = readings,
            Providers = providers,
            Summary = readings.Count(r => r.Value.HasValue) switch
            {
                0 => LocalizedText.Of("Sensor_Summary_None"),
                var available => LocalizedText.Of("Sensor_Summary_Available", available, readings.Count),
            },
        };

        Raise(snapshot);
        return snapshot;
    }

    public void StartMonitoring(TimeSpan interval)
    {
        if (IsMonitoring)
        {
            return;
        }

        Interval = interval < TimeSpan.FromMilliseconds(500) ? TimeSpan.FromMilliseconds(500) : interval;
        _monitorSource = new CancellationTokenSource();
        IsMonitoring = true;

        _monitorTask = Task.Run(async () =>
        {
            var token = _monitorSource.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await SampleAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Sensor sampling failed");
                }

                try
                {
                    await Task.Delay(Interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        });
    }

    public void StopMonitoring()
    {
        IsMonitoring = false;
        try
        {
            _monitorSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed.
        }

        _monitorSource?.Dispose();
        _monitorSource = null;
        _monitorTask = null;
    }

    public void Dispose()
    {
        StopMonitoring();
    }

    private void Raise(SensorSnapshot snapshot)
    {
        var handler = Sampled;
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler<SensorSnapshot> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, snapshot);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Sensor subscriber failed");
            }
        }
    }
}
