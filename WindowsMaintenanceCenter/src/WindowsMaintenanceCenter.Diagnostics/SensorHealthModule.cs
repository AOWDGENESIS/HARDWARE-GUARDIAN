using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Diagnostics;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Core.Services;

namespace WindowsMaintenanceCenter.Diagnostics;

/// <summary>
/// Sensor monitoring (spec sections 30 and 34). Two rules drive this module:
/// a sensor reading states where it came from and how good it is, and an ACPI temperature is never
/// presented as a precise core temperature.
/// </summary>
public sealed class SensorHealthModule : DiagnosticModuleBase
{
    public SensorHealthModule(IClock clock) : base(clock)
    {
    }

    public override string Id => "SENSORS";

    public override string DisplayNameKey => "Module_Sensor";

    public override ComponentCategory Category => ComponentCategory.Sensor;

    protected override string ProtocolModule => "SENSOR";

    protected override async Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var sensors = context.GetRequiredService<ISensorService>();
        var problems = new List<ProblemDraft>();
        var evidence = new List<string>();

        var snapshot = await sensors.SampleAsync(cancellationToken).ConfigureAwait(false);
        evidence.Add($"providers={snapshot.Providers.Count}; readings={snapshot.Readings.Count}");

        foreach (var provider in snapshot.Providers)
        {
            evidence.Add($"provider={provider.Id}; available={provider.IsAvailable}; maxQuality={provider.MaxQuality}; admin={provider.RequiresAdministrator}"
                + (provider.UnavailableReason is null ? string.Empty : $"; reason={provider.UnavailableReason}"));
        }

        var provided = snapshot.Readings.Where(r => r.Value.HasValue).ToList();
        var missing = snapshot.Readings.Where(r => !r.Value.HasValue).ToList();
        evidence.Add($"values={provided.Count}; withoutValue={missing.Count}");

        foreach (var reading in provided)
        {
            evidence.Add($"{reading.MeasurementPointKey}={reading.Value.Display}{reading.Unit} ({reading.Quality}, {reading.Origin.Token()})");
        }

        // Only providers that really delivered a value count; an unavailable provider is not a check.
        var available = snapshot.Providers.Count(p => p.IsAvailable);
        var checks = provided.Count + available;

        if (snapshot.Providers.Count == 0)
        {
            return new ModuleBody
            {
                ChecksExecuted = 0,
                SkipReasonKey = "Module_Skipped_NoSensorProvider",
                SkipReasonCode = "NO_SENSOR_PROVIDER",
                Evidence = evidence,
            };
        }

        // Temperature readings that come from an ACPI thermal zone are a zone value, not a core
        // sensor: they are allowed to inform, but they may never look like a precise core reading.
        if (provided.Count == 0)
        {
            problems.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Sensor),
                Category = ComponentCategory.Sensor,
                Severity = Severity.Info,
                Title = LocalizedText.Of("Problem_SensorUnavailable_Title"),
                Description = LocalizedText.Of("Problem_SensorUnavailable_Description"),
                Evidence = string.Join("; ", snapshot.Providers.Select(pr => $"{pr.Id}: available={pr.IsAvailable}; reason={pr.UnavailableReason ?? "none"}")),
                Impact = LocalizedText.Of("Problem_SensorUnavailable_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_SensorUnavailable_Action"),
                References = new[] { Id },
            });
        }

        var acpiOnly = provided.Count > 0 && provided
            .Where(r => string.Equals(r.Unit, "°C", StringComparison.Ordinal))
            .All(r => r.Quality == SensorQuality.Limited || r.MeasurementPointKey.Contains("Acpi", StringComparison.OrdinalIgnoreCase))
            && provided.Any(r => string.Equals(r.Unit, "°C", StringComparison.Ordinal));
        if (acpiOnly)
        {
            // Explicit, because the number looks precise while it is not.
            problems.Add(new ProblemDraft
            {
                IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Sensor) + "-THERMAL",
                Category = ComponentCategory.Sensor,
                Severity = Severity.Info,
                Title = LocalizedText.Of("Problem_AcpiTemperature_Title"),
                Description = LocalizedText.Of("Problem_AcpiTemperature_Description"),
                Evidence = string.Join("; ", provided.Select(r => $"{r.MeasurementPointKey}={r.Value.Display}{r.Unit} ({r.Quality})")),
                Impact = LocalizedText.Of("Problem_AcpiTemperature_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_AcpiTemperature_Action"),
                References = new[] { Id },
            });
        }

        var hottest = provided
            .Where(r => string.Equals(r.Unit, "°C", StringComparison.Ordinal) && r.Quality != SensorQuality.Limited)
            .OrderByDescending(r => r.Value.Value)
            .FirstOrDefault();

        if (hottest is not null)
        {
            var value = hottest.Value.Value!.Value;
            var critical = value >= 95;
            var warning = value >= 85 && !critical;
            if (critical || warning)
            {
                problems.Add(new ProblemDraft
                {
                    IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Sensor) + "-THERMAL",
                    Category = ComponentCategory.Sensor,
                    Severity = critical ? Severity.Critical : Severity.Warning,
                    Title = LocalizedText.Of(critical ? "Problem_TemperatureCritical_Title" : "Problem_TemperatureHigh_Title", value, hottest.MeasurementPointKey),
                    Description = LocalizedText.Of("Problem_Temperature_Description", hottest.Quality.ToString(), hottest.Origin.Token()),
                    Evidence = $"{hottest.MeasurementPointKey}={value}{hottest.Unit}; quality={hottest.Quality}; origin={hottest.Origin.Token()}",
                    Impact = LocalizedText.Of("Problem_Temperature_Impact"),
                    RecommendedAction = LocalizedText.Of("Problem_Temperature_Action"),
                    References = new[] { Id },
                });
            }
        }

        return new ModuleBody
        {
            Problems = problems,
            ChecksExecuted = checks,
            Evidence = evidence,
        };
    }
}
