using System.Text.Json;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;

namespace HardwareGuardian.Infrastructure.Persistence;

/// <summary>
/// Appends history entries as JSON lines (spec section 66). The file is compacted when it grows
/// beyond the configured retention so that a long lived installation stays small.
/// </summary>
public sealed class FileHistoryStore : IHistoryStore
{
    private readonly IPathProvider _paths;
    private readonly int _retentionEntries;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _filePath;

    public FileHistoryStore(IPathProvider paths, int retentionEntries = 5000)
    {
        _paths = paths;
        _retentionEntries = Math.Clamp(retentionEntries, 100, 100_000);
        _filePath = Path.Combine(paths.HistoryDirectory, "history.jsonl");
    }

    public string Location => _paths.HistoryDirectory;

    public async Task AppendAsync(HistoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var line = JsonSerializer.Serialize(entry, HardwareGuardian.Infrastructure.Serialization.JsonOptions.Compact);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await JsonFileStore.AppendLineAsync(_filePath, line, cancellationToken).ConfigureAwait(false);
            await CompactIfRequiredAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var lines = await JsonFileStore.ReadLastLinesAsync(_filePath, Math.Max(query.MaxResults * 4, 500), cancellationToken).ConfigureAwait(false);

        var entries = new List<HistoryEntry>();
        foreach (var line in lines)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<HistoryEntry>(line, HardwareGuardian.Infrastructure.Serialization.JsonOptions.Compact);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // A single unreadable line must not break the history view.
            }
        }

        IEnumerable<HistoryEntry> filtered = entries;
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            filtered = filtered.Where(e =>
                e.OperationKey.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                e.ChangeSummary.Any(c => c.Contains(text, StringComparison.OrdinalIgnoreCase)) ||
                e.ProblemIds.Any(p => p.Contains(text, StringComparison.OrdinalIgnoreCase)));
        }

        if (query.From.HasValue)
        {
            filtered = filtered.Where(e => e.Timestamp >= query.From.Value);
        }

        if (query.To.HasValue)
        {
            filtered = filtered.Where(e => e.Timestamp <= query.To.Value);
        }

        if (query.MinimumStatus.HasValue)
        {
            filtered = filtered.Where(e => e.OverallStatus >= query.MinimumStatus.Value);
        }

        return filtered.OrderByDescending(e => e.Timestamp).Take(Math.Max(1, query.MaxResults)).ToList();
    }

    public async Task<HistoryEntry?> LatestAsync(CancellationToken cancellationToken)
    {
        var entries = await QueryAsync(new HistoryQuery { MaxResults = 1 }, cancellationToken).ConfigureAwait(false);
        return entries.FirstOrDefault();
    }

    private async Task CompactIfRequiredAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var info = new FileInfo(_filePath);
        if (info.Length < 2 * 1024 * 1024)
        {
            return;
        }

        var lines = await JsonFileStore.ReadLastLinesAsync(_filePath, _retentionEntries, cancellationToken).ConfigureAwait(false);
        var temporary = _filePath + ".compact";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)))
        {
            foreach (var line in lines)
            {
                await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        File.Move(temporary, _filePath, overwrite: true);
    }
}
