using System.Globalization;
using System.Text.Json;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Infrastructure.Serialization;

namespace HardwareGuardian.Infrastructure.Persistence;

/// <summary>Stores raw snapshots as individual JSON files so reports can be rebuilt later.</summary>
public sealed class FileSnapshotStore : ISnapshotStore
{
    private readonly IPathProvider _paths;
    private readonly int _retention;

    public FileSnapshotStore(IPathProvider paths, int retention = 50)
    {
        _paths = paths;
        _retention = Math.Clamp(retention, 5, 2000);
    }

    public string Location => _paths.SnapshotDirectory;

    public async Task SaveAsync(SystemSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _paths.EnsureDirectory(_paths.SnapshotDirectory);
        var fileName = $"snapshot-{snapshot.CapturedAt.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Sanitise(snapshot.Id)}.json";
        var path = Path.Combine(_paths.SnapshotDirectory, fileName);
        await JsonFileStore.WriteAsync(path, snapshot, cancellationToken).ConfigureAwait(false);
        PruneOldSnapshots();
    }

    public async Task<SystemSnapshot?> LoadLatestAsync(CancellationToken cancellationToken)
    {
        var files = await ListAsync(1, cancellationToken).ConfigureAwait(false);
        var latest = files.FirstOrDefault();
        return latest is null ? null : await LoadAsync(latest, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SystemSnapshot?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonFileStore.ReadAsync<SystemSnapshot>(path, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task<IReadOnlyList<string>> ListAsync(int maxEntries, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.SnapshotDirectory))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var files = Directory.EnumerateFiles(_paths.SnapshotDirectory, "snapshot-*.json")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxEntries))
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    private void PruneOldSnapshots()
    {
        try
        {
            var files = Directory.EnumerateFiles(_paths.SnapshotDirectory, "snapshot-*.json")
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                .Skip(_retention)
                .ToList();

            foreach (var file in files)
            {
                File.Delete(file);
            }
        }
        catch (IOException)
        {
            // Pruning is best effort; it must never fail a scan.
        }
    }

    private static string Sanitise(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>
/// File based cache for manufacturer data with explicit expiry, source and retrieval time
/// (spec sections 64 and 65).
/// </summary>
public sealed class FileSourceCacheStore : ISourceCacheStore
{
    private readonly IPathProvider _paths;

    public FileSourceCacheStore(IPathProvider paths)
    {
        _paths = paths;
    }

    public string Location => _paths.CacheDirectory;

    public async Task<SourceCacheEntry?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        try
        {
            return await JsonFileStore.ReadAsync<SourceCacheEntry>(path, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task SetAsync(SourceCacheEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _paths.EnsureDirectory(_paths.CacheDirectory);
        await JsonFileStore.WriteAsync(PathFor(entry.Key), entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SourceCacheEntry>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_paths.CacheDirectory))
        {
            return Array.Empty<SourceCacheEntry>();
        }

        var entries = new List<SourceCacheEntry>();
        foreach (var file in Directory.EnumerateFiles(_paths.CacheDirectory, "manufacturer-*.json"))
        {
            try
            {
                var entry = await JsonFileStore.ReadAsync<SourceCacheEntry>(file, cancellationToken).ConfigureAwait(false);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Ignore corrupt cache files; they are recreated on the next check.
            }
        }

        return entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        if (Directory.Exists(_paths.CacheDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(_paths.CacheDirectory, "manufacturer-*.json"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // Best effort.
                }
            }
        }

        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        var safe = new string(key.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
        if (safe.Length == 0)
        {
            safe = "unknown";
        }

        return Path.Combine(_paths.CacheDirectory, $"manufacturer-{safe}.json");
    }
}
