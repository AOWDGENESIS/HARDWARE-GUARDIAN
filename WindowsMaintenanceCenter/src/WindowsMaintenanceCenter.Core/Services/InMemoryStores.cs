using System.Collections.Concurrent;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// In memory implementations used by unit tests and as a safe fallback when the file system is
/// not writable. They never pretend to be persistent: <see cref="Location"/> states that clearly.
/// </summary>
public sealed class InMemoryHistoryStore : IHistoryStore
{
    private readonly List<HistoryEntry> _entries = new();
    private readonly object _gate = new();

    public string Location => "(in-memory)";

    public Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HistoryEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IEnumerable<HistoryEntry> result = _entries;
            if (!string.IsNullOrWhiteSpace(query.Text))
            {
                var text = query.Text.Trim();
                result = result.Where(e =>
                    e.OperationKey.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    e.ChangeSummary.Any(c => c.Contains(text, StringComparison.OrdinalIgnoreCase)) ||
                    e.ProblemIds.Any(p => p.Contains(text, StringComparison.OrdinalIgnoreCase)));
            }

            if (query.From.HasValue)
            {
                result = result.Where(e => e.Timestamp >= query.From.Value);
            }

            if (query.To.HasValue)
            {
                result = result.Where(e => e.Timestamp <= query.To.Value);
            }

            if (query.MinimumStatus.HasValue)
            {
                result = result.Where(e => e.OverallStatus >= query.MinimumStatus.Value);
            }

            return Task.FromResult<IReadOnlyList<HistoryEntry>>(
                result.OrderByDescending(e => e.Timestamp).Take(Math.Max(1, query.MaxResults)).ToList());
        }
    }

    public Task<HistoryEntry?> LatestAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_entries.OrderByDescending(e => e.Timestamp).FirstOrDefault());
        }
    }
}

public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly List<AuditEntry> _entries = new();
    private readonly object _gate = new();

    public string Location => "(in-memory)";

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> ReadAsync(int maxEntries, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AuditEntry>>(_entries.OrderByDescending(e => e.Timestamp).Take(maxEntries).ToList());
        }
    }
}

public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly List<(string Path, SystemSnapshot Snapshot)> _snapshots = new();
    private readonly object _gate = new();
    private long _counter;

    public string Location => "(in-memory)";

    public Task SaveAsync(SystemSnapshot snapshot, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _snapshots.Add(($"memory://snapshot-{Interlocked.Increment(ref _counter):D4}", snapshot));
        }

        return Task.CompletedTask;
    }

    public Task<SystemSnapshot?> LoadLatestAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_snapshots.OrderByDescending(s => s.Snapshot.CapturedAt).Select(s => s.Snapshot).FirstOrDefault());
        }
    }

    public Task<SystemSnapshot?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            // FirstOrDefault answers null when nothing matches; reading .Snapshot from it threw
            // before the nullability warning was ever looked at.
            // The list holds value tuples, so FirstOrDefault answers the default tuple instead of
            // null; a missing path therefore has to be recognised by the path itself.
            var match = _snapshots.FirstOrDefault(s => s.Path == path);
            return Task.FromResult<SystemSnapshot?>(match.Path == path ? match.Snapshot : null);
        }
    }

    public Task<IReadOnlyList<string>> ListAsync(int maxEntries, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<string>>(_snapshots.Select(s => s.Path).TakeLast(maxEntries).ToList());
        }
    }
}

public sealed class InMemorySourceCacheStore : ISourceCacheStore
{
    private readonly ConcurrentDictionary<string, SourceCacheEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public string Location => "(in-memory)";

    public Task<SourceCacheEntry?> GetAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_entries.TryGetValue(key, out var entry) ? entry : null);

    public Task SetAsync(SourceCacheEntry entry, CancellationToken cancellationToken)
    {
        _entries[entry.Key] = entry;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SourceCacheEntry>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SourceCacheEntry>>(_entries.Values.OrderBy(e => e.Key).ToList());

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        _entries.Clear();
        return Task.CompletedTask;
    }
}
