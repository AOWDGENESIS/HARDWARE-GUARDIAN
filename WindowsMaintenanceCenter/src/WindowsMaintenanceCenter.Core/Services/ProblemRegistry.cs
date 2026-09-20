using System.Collections.Concurrent;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Events;
using WindowsMaintenanceCenter.Core.Models;
using Microsoft.Extensions.Logging;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// Assigns stable problem identifiers in the documented form (spec section 67):
/// <c>HW-CPU-001</c>, <c>HW-BOARD-002</c>, <c>DRV-NVIDIA-001</c>, <c>BIOS-GIGABYTE-001</c>.
/// </summary>
public static class ProblemIdFactory
{
    public static string CategoryPrefix(ComponentCategory category) => category switch
    {
        ComponentCategory.Cpu => "HW-CPU",
        ComponentCategory.Motherboard => "HW-BOARD",
        ComponentCategory.Chipset => "HW-CHIPSET",
        ComponentCategory.Bios => "BIOS",
        ComponentCategory.Firmware => "FW",
        ComponentCategory.Memory => "HW-RAM",
        ComponentCategory.Graphics => "HW-GPU",
        ComponentCategory.Storage => "HW-STORAGE",
        ComponentCategory.Network => "HW-NET",
        ComponentCategory.Audio => "HW-AUDIO",
        ComponentCategory.Usb => "HW-USB",
        ComponentCategory.Pci => "HW-PCI",
        ComponentCategory.Monitor => "HW-MON",
        ComponentCategory.Printer => "HW-PRINT",
        ComponentCategory.Battery => "HW-BAT",
        ComponentCategory.Sensor => "SENSOR",
        ComponentCategory.Driver => "DRV",
        ComponentCategory.Windows => "WIN",
        ComponentCategory.Update => "UPD",
        ComponentCategory.Maintenance => "MNT",
        ComponentCategory.Security => "SEC",
        ComponentCategory.System => "HW-SYS",
        _ => "GEN",
    };

    /// <summary>Manufacturer specific driver prefix, e.g. <c>DRV-NVIDIA</c>.</summary>
    public static string VendorPrefix(string adapterId)
    {
        if (string.IsNullOrWhiteSpace(adapterId))
        {
            return "DRV";
        }

        var cleaned = new string(adapterId.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return cleaned.Length == 0 ? "DRV" : $"DRV-{cleaned}";
    }
}

/// <summary>In memory problem registry that also publishes events for the dashboard.</summary>
public sealed class ProblemRegistry : IProblemRegistry
{
    private readonly IClock _clock;
    private readonly IEventBus? _events;
    private readonly ILogger<ProblemRegistry>? _logger;
    private readonly object _gate = new();
    private readonly List<Problem> _problems = new();
    private readonly ConcurrentDictionary<string, int> _counters = new(StringComparer.OrdinalIgnoreCase);

    public ProblemRegistry(IClock clock, IEventBus? events = null, ILogger<ProblemRegistry>? logger = null)
    {
        _clock = clock;
        _events = events;
        _logger = logger;
    }

    public event EventHandler<Problem>? ProblemRegistered;

    public event EventHandler<Problem>? ProblemUpdated;

    public event EventHandler? Cleared;

    public IReadOnlyList<Problem> All
    {
        get
        {
            lock (_gate)
            {
                return _problems.ToList();
            }
        }
    }

    public ProblemCounts Counts => ProblemCounts.From(All);

    public Problem Add(ProblemDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var prefix = string.IsNullOrWhiteSpace(draft.IdPrefix)
            ? ProblemIdFactory.CategoryPrefix(draft.Category)
            : draft.IdPrefix;

        Problem problem;
        lock (_gate)
        {
            var number = _counters.AddOrUpdate(prefix, 1, (_, current) => current + 1);
            problem = new Problem
            {
                Id = $"{prefix}-{number:D3}",
                Category = draft.Category,
                Severity = draft.Severity,
                Status = ProblemStatus.Open,
                Title = draft.Title,
                Description = draft.Description,
                Evidence = draft.Evidence,
                Impact = draft.Impact,
                RecommendedAction = draft.RecommendedAction,
                DetectedAt = _clock.Now,
                ComponentId = draft.ComponentId,
                ComponentName = draft.ComponentName,
                ActionId = draft.ActionId,
                RequiresAdministrator = draft.RequiresAdministrator,
                References = draft.References,
                BlockedOperations = draft.BlockedOperation is null
                    ? Array.Empty<BlockedOperation>()
                    : new[] { draft.BlockedOperation with { BlockedAt = _clock.Now } },
            };
            _problems.Add(problem);
        }

        Publish(problem);
        return problem;
    }

    public void AddRange(IEnumerable<ProblemDraft> drafts)
    {
        foreach (var draft in drafts)
        {
            Add(draft);
        }
    }

    public void RegisterRange(IEnumerable<Problem> problems)
    {
        foreach (var problem in problems)
        {
            Register(problem);
        }
    }

    public void Register(Problem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        lock (_gate)
        {
            if (_problems.Any(p => string.Equals(p.Id, problem.Id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _problems.Add(problem);
        }

        Publish(problem);
    }

    public void UpdateStatus(string problemId, ProblemStatus status, string? note = null)
    {
        Problem? updated = null;
        ProblemStatus oldStatus = ProblemStatus.Open;
        lock (_gate)
        {
            var index = _problems.FindIndex(p => string.Equals(p.Id, problemId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return;
            }

            oldStatus = _problems[index].Status;
            updated = _problems[index] with { Status = status };
            _problems[index] = updated;
        }

        _logger?.LogInformation("Problem {ProblemId} status {Old} -> {New} ({Note})", problemId, oldStatus, status, note ?? "-");
        _events?.Publish(new ProblemStatusChangedEvent(problemId, oldStatus, status));
        Raise(ProblemUpdated, updated);
    }

    public Problem? Find(string problemId) =>
        All.FirstOrDefault(p => string.Equals(p.Id, problemId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<Problem> ForComponent(string componentId) =>
        All.Where(p => string.Equals(p.ComponentId, componentId, StringComparison.OrdinalIgnoreCase)).ToList();

    public void Clear()
    {
        lock (_gate)
        {
            _problems.Clear();
            _counters.Clear();
        }

        Cleared?.Invoke(this, EventArgs.Empty);
    }

    private void Publish(Problem problem)
    {
        _events?.Publish(new ProblemDetectedEvent(problem));
        switch (problem.Severity)
        {
            case Severity.Critical:
                _events?.Publish(new CriticalErrorDetectedEvent(problem));
                break;
            case Severity.Error:
                _events?.Publish(new ErrorDetectedEvent(problem));
                break;
            case Severity.Warning:
                _events?.Publish(new WarningDetectedEvent(problem));
                break;
        }

        Raise(ProblemRegistered, problem);
    }

    private void Raise(EventHandler<Problem>? handler, Problem problem)
    {
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler<Problem> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, problem);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Problem subscriber failed.");
            }
        }
    }
}
