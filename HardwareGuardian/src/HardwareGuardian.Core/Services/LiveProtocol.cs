using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Services;

/// <summary>
/// Ring buffer implementation of the live protocol (spec section 20). The buffer is bounded so
/// that a long running session cannot grow without limit; the durable record is the audit log.
/// </summary>
public sealed class LiveProtocol : ILiveProtocol
{
    private readonly object _gate = new();
    private readonly LinkedList<ProtocolEntry> _entries = new();
    private readonly IClock _clock;
    private long _sequence;
    private int _maxEntries = 2000;

    public LiveProtocol(IClock clock)
    {
        _clock = clock;
    }

    public event EventHandler<ProtocolEntry>? EntryAdded;

    public int MaxEntries
    {
        get => _maxEntries;
        set => _maxEntries = Math.Clamp(value, 100, 50_000);
    }

    public long Sequence => Interlocked.Read(ref _sequence);

    public IReadOnlyList<ProtocolEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }

    public ProtocolEntry Publish(string module, LocalizedText action, Severity severity, string? detail = null)
    {
        var entry = new ProtocolEntry
        {
            Timestamp = _clock.Now,
            Module = string.IsNullOrWhiteSpace(module) ? "SYS" : module.Trim().ToUpperInvariant(),
            Action = action,
            Severity = severity,
            Detail = detail,
            Sequence = Interlocked.Increment(ref _sequence),
        };

        lock (_gate)
        {
            _entries.AddLast(entry);
            while (_entries.Count > _maxEntries && _entries.First is not null)
            {
                _entries.RemoveFirst();
            }
        }

        var handler = EntryAdded;
        if (handler is not null)
        {
            foreach (EventHandler<ProtocolEntry> subscriber in handler.GetInvocationList())
            {
                try
                {
                    subscriber(this, entry);
                }
                catch (Exception)
                {
                    // The live protocol must never fail because a subscriber failed.
                }
            }
        }

        return entry;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
