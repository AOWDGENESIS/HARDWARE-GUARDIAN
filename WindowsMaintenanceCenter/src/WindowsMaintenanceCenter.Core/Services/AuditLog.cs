using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// Audit log (spec section 27). Entries are persisted through an <see cref="IAuditSink"/>
/// (JSON lines plus a readable text file) and kept in a bounded in-memory window for the UI.
/// </summary>
public sealed class AuditLogService : IAuditLog
{
    private readonly IAuditSink _sink;
    private readonly IClock _clock;
    private readonly IEnvironmentProbe _environment;
    private readonly IBuildInfoProvider? _buildInfo;
    private readonly object _gate = new();
    private readonly LinkedList<AuditEntry> _recent = new();
    private long _counter;

    public AuditLogService(
        IAuditSink sink,
        IClock clock,
        IEnvironmentProbe environment,
        IBuildInfoProvider? buildInfo = null)
    {
        _sink = sink;
        _clock = clock;
        _environment = environment;
        _buildInfo = buildInfo;
    }

    public string Location => _sink.Location;

    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _sink.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _recent.AddFirst(entry);
            while (_recent.Count > 500)
            {
                _recent.RemoveLast();
            }
        }
    }

    public async Task<AuditEntry> RecordAsync(
        OperationKind operation,
        string operationKey,
        ComponentCategory category,
        StageOutcome result,
        string? componentId = null,
        string? oldState = null,
        string? newState = null,
        ManufacturerSourceRef? source = null,
        ApprovalRecord? approval = null,
        BackupRecord? backup = null,
        RollbackResult? rollback = null,
        string? error = null,
        IReadOnlyList<string>? evidence = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry
        {
            Id = $"AUD-{_clock.Now:yyyyMMddHHmmss}-{Interlocked.Increment(ref _counter):D4}",
            Timestamp = _clock.Now,
            User = _environment.UserName,
            MachineName = _environment.MachineName,
            ApplicationVersion = _buildInfo?.Get().Version ?? "unknown",
            Operation = operation,
            OperationKey = operationKey,
            Category = category,
            ComponentId = componentId,
            OldState = oldState,
            NewState = newState,
            Source = source ?? ManufacturerSourceRef.Unknown(),
            Approval = approval,
            Result = result,
            Error = error,
            Backup = backup,
            Rollback = rollback,
            Evidence = evidence ?? Array.Empty<string>(),
        };

        await RecordAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public IReadOnlyList<AuditEntry> Recent(int maxEntries = 100)
    {
        lock (_gate)
        {
            return _recent.Take(Math.Max(1, maxEntries)).ToList();
        }
    }
}
