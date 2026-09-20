using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Diagnostics;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Core.Services;

namespace WindowsMaintenanceCenter.Diagnostics;

/// <summary>
/// Storage health (spec sections 25 and 29). Free space, SMART values and NVMe wear are reported as
/// measured or as unavailable - a missing SMART value is never treated as "healthy".
/// </summary>
public sealed class StorageHealthModule : DiagnosticModuleBase
{
    public StorageHealthModule(IClock clock) : base(clock)
    {
    }

    public override string Id => "STORAGE-HEALTH";

    public override string DisplayNameKey => "Module_Storage";

    public override ComponentCategory Category => ComponentCategory.Storage;

    protected override string ProtocolModule => "STORAGE";

    protected override async Task<ModuleBody> ExecuteAsync(DiagnosticContext context, CancellationToken cancellationToken)
    {
        var devices = await context.Hardware.GetStorageDevicesAsync(cancellationToken).ConfigureAwait(false);
        var reliability = await context.Hardware.GetStorageReliabilityAsync(cancellationToken).ConfigureAwait(false);

        var problems = new List<ProblemDraft>();
        var evidence = new List<string>();
        var checks = 0;

        if (devices.Count == 0)
        {
            return new ModuleBody
            {
                ChecksExecuted = 0,
                SkipReasonKey = "Module_Skipped_NoStorage",
                SkipReasonCode = "NO_STORAGE_DEVICE",
                Evidence = new[] { "the provider reported no storage device" },
            };
        }

        foreach (var device in devices)
        {
            var name = DeviceName(device);
            var counter = ReliabilityFor(device, reliability);

            checks++;
            evidence.Add($"{name}: bus={device.BusType.Display}; media={device.MediaType.Display}; size={device.SizeBytes.Display}; health={device.HealthStatus.Display}");
            evidence.Add($"{name}: smart={device.SmartAvailable}; wearIndicator={device.PercentageUsed.Display}; temperature={device.TemperatureCelsius.Display} C; powerOnHours={device.PowerOnHours.Display}");

            if (counter is not null)
            {
                checks++;
                evidence.Add($"{name}: mediaErrors={counter.MediaErrorsTotal.Display}; readErrors={counter.ReadErrorsTotal.Display}; writeErrors={counter.WriteErrorsTotal.Display}; wearLevel={counter.WearLevelPercent.Display}");
            }

            // The health value comes from the device firmware. Only a positive statement is repeated.
            if (Reports(device.HealthStatus, "fail"))
            {
                problems.Add(Problem(name, device, Severity.Critical, "Problem_StorageFailing_Title", "Problem_StorageFailing_Description", "Problem_StorageFailing_Impact", "Problem_StorageFailing_Action",
                    $"health={device.HealthStatus.Display}; smart={device.SmartAvailable}"));
                continue;
            }

            if (Reports(device.HealthStatus, "caution"))
            {
                problems.Add(Problem(name, device, Severity.Warning, "Problem_StorageCaution_Title", "Problem_StorageCaution_Description", "Problem_StorageCaution_Impact", "Problem_StorageCaution_Action",
                    $"health={device.HealthStatus.Display}"));
            }

            var wear = counter is not null && counter.WearLevelPercent.HasValue ? counter.WearLevelPercent : device.PercentageUsed;
            if (wear.HasValue && wear.Value is { } wearValue && wearValue >= 90)
            {
                problems.Add(Problem(name, device, Severity.Warning, "Problem_StorageWear_Title", "Problem_StorageWear_Description", "Problem_StorageWear_Impact", "Problem_StorageWear_Action",
                    $"wear={wearValue}; origin={wear.Origin.Token()}"));
            }

            foreach (var volume in device.Volumes)
            {
                if (!volume.SizeBytes.HasValue || volume.SizeBytes.Value is not > 0)
                {
                    continue;
                }

                checks++;
                var label = volume.DriveLetter.IsKnown ? volume.DriveLetter.Display : volume.Label.Display;
                var freePercent = volume.FreePercent.HasValue
                    ? volume.FreePercent.Value!.Value
                    : (byte)(100d * (volume.FreeBytes.Value ?? 0) / volume.SizeBytes.Value!.Value);
                evidence.Add($"{label}: free={volume.FreeBytes.Display} ({freePercent} %) of {volume.SizeBytes.Display}");

                if (freePercent < 5)
                {
                    problems.Add(Problem(name, device, Severity.Critical, "Problem_VolumeCritical_Title", "Problem_VolumeCritical_Description", "Problem_VolumeCritical_Impact", "Problem_VolumeCritical_Action",
                        $"{label}: {freePercent} % free"));
                }
                else if (freePercent < 10)
                {
                    problems.Add(Problem(name, device, Severity.Warning, "Problem_VolumeLow_Title", "Problem_VolumeLow_Description", "Problem_VolumeLow_Impact", "Problem_VolumeLow_Action",
                        $"{label}: {freePercent} % free"));
                }
            }

            if (!device.SmartAvailable)
            {
                // Not a defect of the device: the evidence is simply not available to this user.
                problems.Add(Problem(name, device, Severity.Info, "Problem_SmartUnavailable_Title", "Problem_SmartUnavailable_Description", "Problem_SmartUnavailable_Impact", "Problem_SmartUnavailable_Action",
                    $"smart={device.SmartAvailable}; elevated={context.Environment.IsElevated}"));
            }
        }

        return new ModuleBody
        {
            Problems = problems,
            ChecksExecuted = checks,
            Evidence = evidence,
        };
    }

    /// <summary>Display name of a device; the serial number is never used as an identifier here.</summary>
    private static string DeviceName(StorageDeviceInfo device) =>
        device.FriendlyName.IsKnown ? device.FriendlyName.Display
        : device.Model.IsKnown ? device.Model.Display
        : "UNKNOWN";

    /// <summary>
    /// Pairs a device with its reliability counter. Storage reliability counters are reported per
    /// device instance; when the identifier does not contain the known name, no counter is used
    /// instead of attaching the wrong one.
    /// </summary>
    private static StorageReliabilityCounter? ReliabilityFor(StorageDeviceInfo device, IReadOnlyList<StorageReliabilityCounter> counters)
    {
        var name = DeviceName(device);
        var byName = counters.FirstOrDefault(c => name != "UNKNOWN" && c.DeviceId.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        var model = device.Model.IsKnown ? device.Model.Display : null;
        return model is null ? null : counters.FirstOrDefault(c => c.DeviceId.Contains(model, StringComparison.OrdinalIgnoreCase));
    }

    private static bool Reports(TextInfo health, string token) =>
        health.IsKnown && (health.Value ?? string.Empty).Contains(token, StringComparison.OrdinalIgnoreCase);

    private static ProblemDraft Problem(
        string name,
        StorageDeviceInfo device,
        Severity severity,
        string titleKey,
        string descriptionKey,
        string impactKey,
        string actionKey,
        string evidence) => new()
    {
        IdPrefix = ProblemIdFactory.CategoryPrefix(ComponentCategory.Storage),
        Category = ComponentCategory.Storage,
        Severity = severity,
        Title = LocalizedText.Of(titleKey, name),
        Description = LocalizedText.Of(descriptionKey),
        Evidence = evidence,
        Impact = LocalizedText.Of(impactKey),
        RecommendedAction = LocalizedText.Of(actionKey),
        ComponentId = device.Model.IsKnown ? device.Model.Display : name,
        ComponentName = name,
        References = new[] { "STORAGE-HEALTH" },
    };
}
