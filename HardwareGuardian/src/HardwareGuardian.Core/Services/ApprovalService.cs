using System.Collections.Concurrent;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Events;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;
using Microsoft.Extensions.Logging;

namespace HardwareGuardian.Core.Services;

/// <summary>
/// Approval workflow (spec section 24). High and critical risk operations always require an
/// explicit confirmation - the policy can only make confirmations stricter, never laxer.
/// </summary>
public sealed class ApprovalService : IApprovalService
{
    private readonly IClock _clock;
    private readonly ISettingsService _settings;
    private readonly IEventBus? _events;
    private readonly ILogger<ApprovalService>? _logger;
    private readonly ConcurrentDictionary<string, PendingApproval> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ApprovalRecord> _history = new();
    private readonly object _gate = new();
    private long _counter;

    public ApprovalService(IClock clock, ISettingsService settings, IEventBus? events = null, ILogger<ApprovalService>? logger = null)
    {
        _clock = clock;
        _settings = settings;
        _events = events;
        _logger = logger;
    }

    public event EventHandler<ApprovalRequest>? ApprovalRequested;

    public event EventHandler<ApprovalRequest>? ApprovalDecided;

    public IReadOnlyList<ApprovalRecord> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToList();
            }
        }
    }

    public bool IsConfirmationRequired(RiskLevel risk)
    {
        // Safety rails that no policy may switch off.
        if (risk is RiskLevel.High or RiskLevel.Critical)
        {
            return true;
        }

        return _settings.Current.ConfirmationPolicy switch
        {
            ConfirmationPolicy.AlwaysConfirm => true,
            ConfirmationPolicy.ConfirmMediumAndAbove => risk >= RiskLevel.Medium,
            ConfirmationPolicy.DryRunOnly => true,
            _ => true,
        };
    }

    public Task<ApprovalRequest> CreateAsync(ApprovalRequestDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();

        if (draft.BlockedReasonCode is not null)
        {
            throw new OperationBlockedException(
                draft.BlockedReasonCode,
                draft.BlockedReason ?? LocalizedText.Of("Blocked_Unknown_Reason"),
                draft.Evidence.Count > 0 ? string.Join("; ", draft.Evidence) : null);
        }

        var operationId = string.IsNullOrWhiteSpace(draft.OperationId)
            ? $"OP-{_clock.Now:yyyyMMddHHmmss}-{Interlocked.Increment(ref _counter):D3}"
            : draft.OperationId;

        var request = new ApprovalRequest
        {
            RequestId = $"APR-{_clock.Now:yyyyMMddHHmmss}-{Interlocked.Increment(ref _counter):D3}",
            Draft = draft with { OperationId = operationId },
            Decision = ApprovalDecision.Pending,
            CreatedAt = _clock.Now,
        };

        var pending = new PendingApproval(request);
        _pending[request.RequestId] = pending;
        _logger?.LogInformation("Approval requested for {Operation} ({Risk})", operationId, draft.Risk);
        _events?.Publish(new ApprovalRequestedEvent(request));
        Raise(ApprovalRequested, request);
        return Task.FromResult(request);
    }

    public async Task<ApprovalRequest> WaitForDecisionAsync(string requestId, CancellationToken cancellationToken)
    {
        if (!_pending.TryGetValue(requestId, out var pending))
        {
            throw new InvalidOperationException($"Unknown approval request '{requestId}'.");
        }

        using var registration = cancellationToken.Register(() =>
        {
            if (!pending.Completion.Task.IsCompleted)
            {
                Decide(requestId, ApprovalDecision.Cancelled, "cancelled-by-caller");
            }
        });

        return await pending.Completion.Task.ConfigureAwait(false);
    }

    public void Decide(string requestId, ApprovalDecision decision, string? note = null)
    {
        if (!_pending.TryRemove(requestId, out var pending))
        {
            return;
        }

        var decided = pending.Request with
        {
            Decision = decision,
            DecidedAt = _clock.Now,
            Note = note,
        };

        var record = new ApprovalRecord
        {
            RequestId = decided.RequestId,
            OperationId = decided.Draft.OperationId,
            Decision = decision,
            DecidedAt = decided.DecidedAt ?? _clock.Now,
            Note = note,
            Risk = decided.Draft.Risk,
            WasRequired = IsConfirmationRequired(decided.Draft.Risk),
        };

        lock (_gate)
        {
            _history.Add(record);
            if (_history.Count > 500)
            {
                _history.RemoveAt(0);
            }
        }

        _logger?.LogInformation("Approval {RequestId} decided: {Decision}", requestId, decision);
        _events?.Publish(new ApprovalDecidedEvent(decided));
        Raise(ApprovalDecided, decided);
        pending.Completion.TrySetResult(decided);
    }

    public async Task<ApprovalRecord> EnsureApprovedAsync(ApprovalRequestDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.BlockedReasonCode is not null)
        {
            throw new OperationBlockedException(
                draft.BlockedReasonCode,
                draft.BlockedReason ?? LocalizedText.Of("Blocked_Unknown_Reason"),
                draft.Evidence.Count > 0 ? string.Join("; ", draft.Evidence) : null);
        }

        var request = await CreateAsync(draft, cancellationToken).ConfigureAwait(false);

        if (!IsConfirmationRequired(draft.Risk))
        {
            // Low risk with a permissive policy: still recorded, but decided automatically as approved.
            Decide(request.RequestId, ApprovalDecision.Approved, "policy: low risk operation");
        }

        var decided = await WaitForDecisionAsync(request.RequestId, cancellationToken).ConfigureAwait(false);

        if (decided.Decision != ApprovalDecision.Approved)
        {
            throw new OperationBlockedException(
                decided.Decision == ApprovalDecision.Rejected ? BlockReasons.UserRejected : BlockReasons.DryRunOnly,
                decided.Decision == ApprovalDecision.Rejected
                    ? LocalizedText.Of("Approval_Rejected")
                    : LocalizedText.Of("Approval_NotConfirmed"),
                decided.Note);
        }

        return new ApprovalRecord
        {
            RequestId = decided.RequestId,
            OperationId = decided.Draft.OperationId,
            Decision = ApprovalDecision.Approved,
            DecidedAt = decided.DecidedAt ?? _clock.Now,
            Note = decided.Note,
            Risk = decided.Draft.Risk,
            WasRequired = IsConfirmationRequired(decided.Draft.Risk),
        };
    }

    private void Raise(EventHandler<ApprovalRequest>? handler, ApprovalRequest request)
    {
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler<ApprovalRequest> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, request);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Approval subscriber failed.");
            }
        }
    }

    private sealed record PendingApproval
    {
        public PendingApproval(ApprovalRequest request) => Request = request;

        public ApprovalRequest Request { get; }

        public TaskCompletionSource<ApprovalRequest> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
