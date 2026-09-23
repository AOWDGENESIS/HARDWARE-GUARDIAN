using System.Globalization;
using System.Text;
using System.Text.Json;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Security;
using WindowsMaintenanceCenter.Infrastructure.Serialization;

namespace WindowsMaintenanceCenter.Infrastructure.Persistence;

/// <summary>
/// Audit persistence (spec section 27): JSON lines for machines, a readable text file for humans.
/// Both files are written for every entry; the text file is rendered in the language that was
/// active when the entry was created.
/// </summary>
public sealed class FileAuditSink : IAuditSink
{
    private readonly IPathProvider _paths;
    private readonly ILocalizer _localizer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _jsonPath;
    private readonly string _textPath;

    public FileAuditSink(IPathProvider paths, ILocalizer localizer)
    {
        _paths = paths;
        _localizer = localizer;
        _jsonPath = Path.Combine(paths.AuditDirectory, "audit.jsonl");
        _textPath = Path.Combine(paths.AuditDirectory, "audit.txt");
    }

    public string Location => _paths.AuditDirectory;

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // The audit file is the record a customer may have to hand over, so a secret must not reach
        // it (M38-S-001). The same shapes as in the technical log are removed; the entry that is
        // written and the entry that was passed in differ only in those places.
        var json = SensitiveDataGuard.Redact(JsonSerializer.Serialize(entry, JsonOptions.Compact));
        var text = SensitiveDataGuard.Redact(RenderText(entry));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await JsonFileStore.AppendLineAsync(_jsonPath, json, cancellationToken).ConfigureAwait(false);
            await JsonFileStore.AppendLineAsync(_textPath, text, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> ReadAsync(int maxEntries, CancellationToken cancellationToken)
    {
        var lines = await JsonFileStore.ReadLastLinesAsync(_jsonPath, Math.Max(1, maxEntries), cancellationToken).ConfigureAwait(false);
        var entries = new List<AuditEntry>();
        foreach (var line in lines)
        {
            try
            {
                var entry = JsonSerializer.Deserialize<AuditEntry>(line, JsonOptions.Compact);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Skip unreadable lines instead of failing the whole view.
            }
        }

        return entries;
    }

    private string RenderText(AuditEntry entry)
    {
        var builder = new StringBuilder();
        builder.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        builder.Append(" | ");
        builder.Append(entry.Id);
        builder.Append(" | user=").Append(entry.User);
        builder.Append(" | machine=").Append(entry.MachineName);
        builder.Append(" | app=").Append(entry.ApplicationVersion);
        builder.Append(" | op=").Append(entry.Operation).Append('(').Append(entry.OperationKey).Append(')');
        builder.Append(" | component=").Append(entry.ComponentId ?? "-");
        builder.Append(" | state=").Append(entry.OldState ?? "-").Append("->").Append(entry.NewState ?? "-");
        builder.Append(" | source=").Append(entry.Source.DisplayNameKey).Append('(').Append(entry.Source.Trust).Append(')');
        builder.Append(" | approval=").Append(entry.Approval?.Decision.ToString() ?? "-");
        builder.Append(" | result=").Append(entry.Result);

        if (!string.IsNullOrWhiteSpace(entry.Error))
        {
            builder.Append(" | error=").Append(entry.Error);
        }

        if (entry.Backup is not null)
        {
            builder.Append(" | backup=").Append(entry.Backup.Id);
        }

        if (entry.Rollback is not null)
        {
            builder.Append(" | rollback=").Append(entry.Rollback.Outcome);
        }

        if (entry.Evidence.Count > 0)
        {
            builder.Append(" | evidence=").Append(string.Join("; ", entry.Evidence));
        }

        // Keep the raw key chain visible next to the readable text: it is required for support.
        // OperationKey is a key, not a text: Resolve() takes LocalizedText, the indexer takes a key.
        builder.Append(" | text=").Append(_localizer[entry.OperationKey]);
        return builder.ToString();
    }
}
