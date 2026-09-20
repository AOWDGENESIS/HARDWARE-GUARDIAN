using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;

namespace WindowsMaintenanceCenter.Core.Services;

/// <summary>
/// Progress reporting with an honest time estimate (spec section 21).
/// An remaining time is only published when the total step count is known, at least three
/// steps have completed, and the observed step durations are reasonably stable. Otherwise
/// the estimate stays <c>null</c> and the UI shows the elapsed time only.
/// </summary>
public sealed class ProgressReporter : IProgressReporter
{
    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly List<TimeSpan> _stepDurations = new();
    private ProgressSnapshot _current = ProgressSnapshot.Idle;
    private DateTimeOffset _lastStepAt = DateTimeOffset.MinValue;
    private DateTimeOffset _startedAt = DateTimeOffset.MinValue;

    public ProgressReporter(IClock clock)
    {
        _clock = clock;
    }

    public event EventHandler<ProgressSnapshot>? ProgressChanged;

    public ProgressSnapshot Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Start(string operationKey, string module, int? totalSteps)
    {
        ProgressSnapshot snapshot;
        lock (_gate)
        {
            _startedAt = _clock.Now;
            _lastStepAt = _startedAt;
            _stepDurations.Clear();
            _current = new ProgressSnapshot
            {
                OperationKey = operationKey,
                Module = module,
                IsRunning = true,
                TotalSteps = totalSteps,
                CompletedSteps = 0,
                Fraction = totalSteps is > 0 ? 0d : null,
                StartedAt = _startedAt,
                UpdatedAt = _startedAt,
                Elapsed = TimeSpan.Zero,
            };
            snapshot = _current;
        }

        Raise(snapshot);
    }

    public void ReportStep(string stepKey, string? detail = null)
    {
        ProgressSnapshot snapshot;
        lock (_gate)
        {
            EnsureStarted();
            var now = _clock.Now;
            if (_lastStepAt != DateTimeOffset.MinValue)
            {
                var duration = now - _lastStepAt;
                if (duration > TimeSpan.Zero && duration < TimeSpan.FromHours(1))
                {
                    _stepDurations.Add(duration);
                }
            }

            _lastStepAt = now;
            var completed = _current.CompletedSteps + 1;
            var total = _current.TotalSteps;
            var fraction = total is > 0 ? Math.Min(1d, (double)completed / total.Value) : _current.Fraction;

            snapshot = _current with
            {
                CompletedSteps = completed,
                Fraction = fraction,
                StepKey = stepKey,
                StepDetail = detail,
                UpdatedAt = now,
                Elapsed = now - _startedAt,
                EstimatedRemaining = EstimateRemaining(completed, total, now),
            };
            _current = snapshot;
        }

        Raise(snapshot);
    }

    public void ReportFraction(double fraction, string stepKey, string? detail = null)
    {
        ProgressSnapshot snapshot;
        lock (_gate)
        {
            EnsureStarted();
            var now = _clock.Now;
            var clamped = Math.Clamp(fraction, 0d, 1d);
            snapshot = _current with
            {
                Fraction = clamped,
                StepKey = stepKey,
                StepDetail = detail,
                UpdatedAt = now,
                Elapsed = now - _startedAt,
                EstimatedRemaining = _current.EstimatedRemaining,
            };
            _current = snapshot;
        }

        Raise(snapshot);
    }

    public void Complete(bool success)
    {
        ProgressSnapshot snapshot;
        lock (_gate)
        {
            var now = _clock.Now;
            snapshot = _current with
            {
                IsRunning = false,
                Success = success,
                Fraction = success ? 1d : _current.Fraction,
                CompletedSteps = _current.TotalSteps ?? _current.CompletedSteps,
                UpdatedAt = now,
                Elapsed = _startedAt == DateTimeOffset.MinValue ? TimeSpan.Zero : now - _startedAt,
                EstimatedRemaining = TimeSpan.Zero,
            };
            _current = snapshot;
        }

        Raise(snapshot);
    }

    public void Reset()
    {
        lock (_gate)
        {
            _current = ProgressSnapshot.Idle;
            _stepDurations.Clear();
            _startedAt = DateTimeOffset.MinValue;
            _lastStepAt = DateTimeOffset.MinValue;
        }

        Raise(ProgressSnapshot.Idle);
    }

    public IProgressScope BeginScope(string operationKey, string module, int? totalSteps)
    {
        Start(operationKey, module, totalSteps);
        return new Scope(this);
    }

    private void EnsureStarted()
    {
        if (_startedAt == DateTimeOffset.MinValue)
        {
            _startedAt = _clock.Now;
            _lastStepAt = _startedAt;
            _current = _current with
            {
                IsRunning = true,
                StartedAt = _startedAt,
                UpdatedAt = _startedAt,
            };
        }
    }

    private TimeSpan? EstimateRemaining(int completed, int? total, DateTimeOffset now)
    {
        if (total is not > 0 || completed < 3 || _stepDurations.Count < 3)
        {
            return null;
        }

        var recent = _stepDurations.Skip(Math.Max(0, _stepDurations.Count - 5)).ToList();
        var minimum = recent.Min();
        var maximum = recent.Max();
        if (minimum <= TimeSpan.Zero || maximum > minimum * 4)
        {
            // The observed durations vary too much for a defensible estimate.
            return null;
        }

        var average = TimeSpan.FromTicks((long)recent.Average(d => d.Ticks));
        var remaining = Math.Max(0, total.Value - completed);
        var estimate = TimeSpan.FromTicks(average.Ticks * remaining);
        return estimate > TimeSpan.FromHours(24) ? null : estimate;
    }

    private void Raise(ProgressSnapshot snapshot)
    {
        var handler = ProgressChanged;
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler<ProgressSnapshot> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, snapshot);
            }
            catch (Exception)
            {
                // Progress must never break the operation it reports on.
            }
        }
    }

    private sealed class Scope : IProgressScope
    {
        private readonly ProgressReporter _reporter;
        private bool _completed;

        public Scope(ProgressReporter reporter) => _reporter = reporter;

        public void Step(string stepKey, string? detail = null) => _reporter.ReportStep(stepKey, detail);

        public void Complete(bool success)
        {
            _completed = true;
            _reporter.Complete(success);
        }

        public void Dispose()
        {
            if (!_completed)
            {
                _reporter.Complete(false);
            }
        }
    }
}
