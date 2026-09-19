using System.Diagnostics;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Services;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Diagnostics;

/// <summary>
/// Base class for diagnostic modules. It provides the plumbing (timing, problem registration,
/// protocol output, status derivation) so that each module only contains real analysis.
/// </summary>
public abstract class DiagnosticModuleBase : IDiagnosticModule
{
    protected DiagnosticModuleBase(IClock clock)
    {
        Clock = clock;
    }

    protected IClock Clock { get; }

    public abstract string Id { get; }

    public abstract string DisplayNameKey { get; }

    public abstract ComponentCategory Category { get; }

    public virtual bool RequiresAdministrator => false;

    public virtual bool RequiresNetwork => false;

    /// <summary>Short token used by the live protocol, e.g. <c>CPU</c>.</summary>
    protected virtual string ProtocolModule => Category switch
    {
        ComponentCategory.Cpu => "CPU",
        ComponentCategory.Motherboard => "BOARD",
        ComponentCategory.Chipset => "CHIPSET",
        ComponentCategory.Bios => "BIOS",
        ComponentCategory.Firmware => "FIRMWARE",
        ComponentCategory.Memory => "RAM",
        ComponentCategory.Graphics => "GPU",
        ComponentCategory.Storage => "STORAGE",
        ComponentCategory.Network => "NET",
        ComponentCategory.Audio => "AUDIO",
        ComponentCategory.Monitor => "MONITOR",
        ComponentCategory.Printer => "PRINT",
        ComponentCategory.Battery => "BAT",
        ComponentCategory.Driver => "DRIVER",
        ComponentCategory.Windows => "WINDOWS",
        ComponentCategory.Security => "SECURITY",
        ComponentCategory.Maintenance => "MAINT",
        ComponentCategory.Sensor => "SENSOR",
        _ => "SYS",
    };

    public async Task<ModuleResult> RunAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var stopwatch = Stopwatch.StartNew();
        context.Protocol.Info(ProtocolModule, LocalizedText.Of("Protocol_ModuleStarted", LocalizedText.Of(DisplayNameKey)));

        ModuleBody body;
        try
        {
            body = await ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var detail = $"{ex.GetType().Name}: {ex.Message}";
            context.Protocol.Error(ProtocolModule, LocalizedText.Of("Protocol_ModuleFailed", LocalizedText.Of(DisplayNameKey)), detail);
            var failed = context.Problems.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(Category),
                Category = Category,
                Severity = Severity.Warning,
                Title = LocalizedText.Of("Problem_ModuleFailed_Title", LocalizedText.Of(DisplayNameKey)),
                Description = LocalizedText.Of("Problem_ModuleFailed_Description", ex.GetType().Name),
                Evidence = detail,
                Impact = LocalizedText.Of("Problem_ModuleFailed_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_ModuleFailed_Action"),
                References = new[] { Id },
            });

            return new ModuleResult
            {
                ModuleId = Id,
                DisplayNameKey = DisplayNameKey,
                Category = Category,
                Status = HealthStatus.Unknown,
                Problems = new[] { failed },
                Duration = stopwatch.Elapsed,
                ChecksExecuted = 0,
                Evidence = new[] { detail },
            };
        }

        stopwatch.Stop();

        var problems = body.Problems.Select(draft => context.Problems.Add(draft)).ToList();
        var status = body.StatusOverride ?? DeriveStatus(body, problems);

        foreach (var problem in problems.Where(p => p.Severity is Severity.Critical or Severity.Error))
        {
            context.Protocol.Publish(ProtocolModule, problem.Title, problem.Severity, problem.Evidence);
        }

        context.Protocol.Publish(
            ProtocolModule,
            LocalizedText.Of("Protocol_ModuleCompleted", LocalizedText.Of(DisplayNameKey), problems.Count),
            problems.Any(p => p.Severity == Severity.Critical) ? Severity.Critical
                : problems.Any(p => p.Severity == Severity.Error) ? Severity.Error
                : problems.Any(p => p.Severity == Severity.Warning) ? Severity.Warning
                : Severity.Success);

        return new ModuleResult
        {
            ModuleId = Id,
            DisplayNameKey = DisplayNameKey,
            Category = Category,
            Status = status,
            Components = body.Components,
            Problems = problems,
            Duration = stopwatch.Elapsed,
            ChecksExecuted = body.ChecksExecuted,
            Evidence = body.Evidence,
            WasSkipped = body.SkipReasonKey is not null,
            SkipReasonCode = body.SkipReasonCode,
            SkipReason = body.SkipReasonKey is null ? null : LocalizedText.Of(body.SkipReasonKey),
        };
    }

    protected abstract Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken);

    private static HealthStatus DeriveStatus(ModuleBody body, IReadOnlyList<Problem> problems)
    {
        if (body.SkipReasonKey is not null)
        {
            return HealthStatus.Unknown;
        }

        if (body.ChecksExecuted == 0)
        {
            // Nothing was actually checked: claiming "healthy" would be a fabricated result.
            return HealthStatus.Unknown;
        }

        return problems.Count == 0
            ? HealthStatus.Healthy
            : OverallStatusCalculator.Worst(problems.Select(p => OverallStatusCalculator.FromSeverity(p.Severity)));
    }

    /// <summary>Result of one module execution.</summary>
    protected sealed record ModuleBody
    {
        public IReadOnlyList<HardwareComponent> Components { get; init; } = Array.Empty<HardwareComponent>();

        public IReadOnlyList<ProblemDraft> Problems { get; init; } = Array.Empty<ProblemDraft>();

        /// <summary>Number of individual checks that were really executed.</summary>
        public int ChecksExecuted { get; init; }

        public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

        /// <summary>Set to override the derived status (for example when the module is skipped).</summary>
        public HealthStatus? StatusOverride { get; init; }

        public string? SkipReasonKey { get; init; }

        public string? SkipReasonCode { get; init; }
    }
}
