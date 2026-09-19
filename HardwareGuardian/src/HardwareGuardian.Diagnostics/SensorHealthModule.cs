using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Diagnostics;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Services;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Diagnostics;

/// <summary>
/// Sensor readings with their measurement point and accuracy (spec sections 35 and 36).
///
/// ACPI thermal zones are never presented as precise core temperatures: the reading is emitted with
/// its measurement point key and its quality, and the module reports the limitation instead of
/// inventing a core temperature.
/// </summary>
public sealed class SensorHealthModule : DiagnosticModuleBase
{
    public SensorHealthModule(IClock clock)
        : base(clock)
    {
    }

    public override string Id => "SENSORS";

    public override string DisplayNameKey => "Module_Sensor";

    public override ComponentCategory Category => ComponentCategory.Sensor;

    protected override async Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var sensors = context.GetRequiredService<ISensorService>();
        var snapshot = await sensors.SampleAsync(cancellationToken).ConfigureAwait(false);

        var evidence = new List<string> { $"sampledAt={snapshot.SampledAt:O}", $"readings={snapshot.Readings.Count}" };

        foreach (var provider in snapshot.Providers)
        {
            evidence.Add($"provider={provider.Id} available={provider.IsAvailable} maxQuality={provider.MaxQuality} reason={provider.UnavailableReason ?? "n/a"}");
        }

        foreach (var reading in snapshot.Readings)
        {
            var value = reading.Value.HasValue
                ? $"{reading.Value.Value!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}{reading.Unit}"
                : $"UNKNOWN({reading.Value.UnknownReason ?? "no reason reported"})";
            evidence.Add($"reading={reading.Id} value={value} quality={reading.Quality} point={reading.MeasurementPointKey} origin={reading.Origin.Token()}");
        }

        var drafts = new List<ProblemDraft>();
        if (snapshot.Readings.Count == 0)
        {
            drafts.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Sensor),
                Category = ComponentCategory.Sensor,
                Severity = Severity.Info,
                Title = LocalizedText.Of("Problem_SensorUnavailable_Title"),
                Description = LocalizedText.Of("Problem_SensorUnavailable_Description"),
                Impact = LocalizedText.Of("Problem_SensorUnavailable_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_SensorUnavailable_Action"),
                Evidence = string.Join("; ", evidence),
                References = new[] { Id },
            });
        }

        return new ModuleBody
        {
            Problems = drafts,
            ChecksExecuted = snapshot.Providers.Count,
            Evidence = evidence,
        };
    }
}
