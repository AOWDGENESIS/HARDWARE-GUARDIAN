using System.Collections.Concurrent;
using WindowsMaintenanceCenter.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// Minimal thread safe event bus. Publisher and subscriber never see each other's exceptions:
/// a faulty subscriber is logged and skipped so that one view cannot break a scan.
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly ILogger<EventBus> _logger;
    private readonly object _gate = new();

    public EventBus(ILogger<EventBus> logger)
    {
        _logger = logger;
    }

    public int SubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _handlers.Values.Sum(list => list.Count);
            }
        }
    }

    public void Publish<TEvent>(TEvent @event) where TEvent : notnull
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var list))
        {
            return;
        }

        Delegate[] snapshot;
        lock (_gate)
        {
            snapshot = list.ToArray();
        }

        foreach (var handler in snapshot)
        {
            try
            {
                ((Action<TEvent>)handler)(@event);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event handler for {EventType} failed.", typeof(TEvent).Name);
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);

        var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Delegate>());
        lock (_gate)
        {
            list.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_gate)
            {
                if (_handlers.TryGetValue(typeof(TEvent), out var current))
                {
                    current.Remove(handler);
                }
            }
        });
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _dispose;

        public Subscription(Action dispose) => _dispose = dispose;

        public void Dispose()
        {
            var action = Interlocked.Exchange(ref _dispose, null);
            action?.Invoke();
        }
    }
}
