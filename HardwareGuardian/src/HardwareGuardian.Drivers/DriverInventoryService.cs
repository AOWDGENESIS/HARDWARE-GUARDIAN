using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Services;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Drivers;

/// <summary>
/// Driver inventory and health analysis (spec sections 14 and 15). Rules:
/// a device that is not present is not an error, a newer version number alone is no reason to
/// install anything, and a discrepancy between local and manufacturer data is reported instead
/// of being resolved silently.
/// </summary>
public sealed class DriverInventoryService : IDriverInventoryService
{
    private const uint ProblemDisabled = 22;
    private const uint ProblemNotStarted = 10;
    private const uint ProblemFailedInstall = 28;

    private readonly IHardwareProvider _hardware;
    private readonly IClock _clock;

    public DriverInventoryService(IHardwareProvider hardware, IClock clock)
    {
        _hardware = hardware;
        _clock = clock;
    }

    public async Task<DriverStateSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        var drivers = await _hardware.GetDriversAsync(cancellationToken).ConfigureAwait(false);
        var devices = await _hardware.GetPnpDevicesAsync(cancellationToken).ConfigureAwait(false);

        return new DriverStateSnapshot
        {
            Id = $"DRV-{_clock.Now:yyyyMMddHHmmss}",
            CapturedAt = _clock.Now,
            Drivers = drivers,
            Devices = devices,
        };
    }

    public Task<IReadOnlyList<Problem>> AnalyzeAsync(DriverStateSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var analyzedAt = _clock.Now;
        var problems = new List<Problem>();
        var counter = 0;

        // 1. Devices that are present and report a problem code.
        foreach (var device in snapshot.Devices.Where(d => d.IsPresent != false && d.ProblemCode.HasValue))
        {
            var code = device.ProblemCode.Value!.Value;
            if (code == 0)
            {
                continue;
            }

            var severity = code switch
            {
                ProblemDisabled => Severity.Warning,
                ProblemNotStarted => Severity.Warning,
                ProblemFailedInstall => Severity.Error,
                _ => Severity.Warning,
            };

            var deviceName = device.Name.Display;
            var id = $"{ProblemIdFactory.VendorPrefix(device.Class.Value ?? "drv")}-{++counter:D3}";
            problems.Add(new Problem
            {
                Id = id,
                Category = ComponentCategory.Driver,
                Severity = severity,
                Status = ProblemStatus.Open,
                Title = LocalizedText.Of("Problem_DeviceProblemCode_Title", deviceName),
                Description = LocalizedText.Of("Problem_DeviceProblemCode_Description", code, ProblemCodeText(code)),
                Evidence = $"PNP={device.DeviceInstanceId.Display}; ConfigManagerErrorCode={code}; present={device.IsPresent}",
                Impact = LocalizedText.Of("Problem_DeviceProblemCode_Impact"),
                RecommendedAction = LocalizedText.Of(code == ProblemDisabled ? "Problem_DeviceProblemCode_Action_Disabled" : "Problem_DeviceProblemCode_Action_Driver"),
                DetectedAt = analyzedAt,
                ComponentId = device.DeviceInstanceId.Display,
                ComponentName = deviceName,
                RequiresAdministrator = code != ProblemDisabled,
                References = device.HardwareIds.Take(3).ToList(),
            });
        }

        // 2. Drivers that the platform reports as not signed. Unverified stays unverified.
        foreach (var driver in snapshot.Drivers.Where(d => d.IsSigned == false))
        {
            var deviceName = driver.DeviceName.Display;
            var id = $"{ProblemIdFactory.VendorPrefix(driver.DriverProvider.Value ?? "drv")}-{++counter:D3}";
            problems.Add(new Problem
            {
                Id = id,
                Category = ComponentCategory.Driver,
                Severity = Severity.Warning,
                Status = ProblemStatus.Open,
                Title = LocalizedText.Of("Problem_DriverUnsigned_Title", deviceName),
                Description = LocalizedText.Of("Problem_DriverUnsigned_Description"),
                Evidence = $"Provider={driver.DriverProvider.Display}; Version={driver.DriverVersion.Display}; INF={driver.InfName.Display}; IsSigned=False",
                Impact = LocalizedText.Of("Problem_DriverUnsigned_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_DriverUnsigned_Action"),
                DetectedAt = analyzedAt,
                ComponentId = driver.DeviceInstanceId.Display,
                ComponentName = deviceName,
                References = new[] { driver.DeviceInstanceId.Display },
            });
        }

        // 3. Devices without any driver binding at all (present, no driver record).
        var driverInstances = snapshot.Drivers.Select(d => d.DeviceInstanceId.Display).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var device in snapshot.Devices.Where(d => d.IsPresent == true && !driverInstances.Contains(d.DeviceInstanceId.Display)))
        {
            if (device.Category is ComponentCategory.Unknown or ComponentCategory.System)
            {
                continue;
            }

            var deviceName = device.Name.Display;
            var id = $"{ProblemIdFactory.VendorPrefix(device.Class.Value ?? "drv")}-{++counter:D3}";
            problems.Add(new Problem
            {
                Id = id,
                Category = ComponentCategory.Driver,
                Severity = Severity.Info,
                Status = ProblemStatus.Open,
                Title = LocalizedText.Of("Problem_DriverMissing_Title", deviceName),
                Description = LocalizedText.Of("Problem_DriverMissing_Description"),
                Evidence = $"PNP={device.DeviceInstanceId.Display}; class={device.Class.Display}; no matching entry in Win32_PnPSignedDriver",
                Impact = LocalizedText.Of("Problem_DriverMissing_Impact"),
                RecommendedAction = LocalizedText.Of("Problem_DriverMissing_Action"),
                DetectedAt = analyzedAt,
                ComponentId = device.DeviceInstanceId.Display,
                ComponentName = deviceName,
                References = device.HardwareIds.Take(3).ToList(),
            });
        }

        return Task.FromResult<IReadOnlyList<Problem>>(problems);
    }

    public Task<IReadOnlyList<HardwareComponent>> BuildComponentsAsync(DriverStateSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var origin = ValueOrigin.Wmi(_clock.Now, "Win32_PnPSignedDriver", SensorQuality.High);
        var components = new List<HardwareComponent>();
        var index = 0;

        foreach (var driver in snapshot.Drivers.OrderBy(d => d.DeviceClass.Display, StringComparer.CurrentCultureIgnoreCase).ThenBy(d => d.DeviceName.Display, StringComparer.CurrentCultureIgnoreCase))
        {
            var device = snapshot.Devices.FirstOrDefault(d => string.Equals(d.DeviceInstanceId.Display, driver.DeviceInstanceId.Display, StringComparison.OrdinalIgnoreCase));
            var verified = driver.IsSigned.HasValue ? driver.IsSigned.Value : (bool?)null;

            components.Add(new HardwareComponent
            {
                Id = $"driver-{index++}",
                Category = ComponentCategory.Driver,
                CategoryKey = "Component_Driver",
                Name = driver.DeviceName.IsKnown ? driver.DeviceName : TextInfo.Unknown(origin, "device name not reported"),
                Manufacturer = driver.DriverProvider,
                Model = TextInfo.From(driver.DeviceClass.Value, origin),
                Status = device?.ProblemCode.HasValue == true && device.ProblemCode.Value!.Value != 0
                    ? HealthStatus.Warning
                    : verified == false ? HealthStatus.Attention : HealthStatus.Healthy,
                DeviceInstanceId = driver.DeviceInstanceId,
                Driver = new DriverInfo
                {
                    Provider = driver.DriverProvider,
                    Version = driver.DriverVersion,
                    Date = driver.DriverDate,
                    DeviceClass = driver.DeviceClass,
                    InfName = driver.InfName,
                    FileName = driver.DriverFileName,
                    Signer = driver.Signer,
                    Status = driver.Status,
                    IsSignatureVerified = verified,
                    IsInbox = driver.IsInboxDriver,
                    IsGenericFallback = driver.IsGenericFallback,
                    SignatureVerification = verified == true ? VerificationLevel.MetadataMatch : VerificationLevel.NotVerified,
                    HardwareId = driver.HardwareId,
                    ProblemCode = driver.ProblemCode,
                },
                HardwareIds = device?.HardwareIds ?? Array.Empty<string>(),
                CompatibleIds = device?.CompatibleIds ?? Array.Empty<string>(),
                SortOrder = 200,
            });
        }

        return Task.FromResult<IReadOnlyList<HardwareComponent>>(components);
    }

    /// <summary>Compares two snapshots and reports what actually changed (spec section 66).</summary>
    public IReadOnlyList<DetectedChange> Compare(DriverStateSnapshot older, DriverStateSnapshot newer)
    {
        ArgumentNullException.ThrowIfNull(older);
        ArgumentNullException.ThrowIfNull(newer);

        var changes = new List<DetectedChange>();
        var olderDrivers = older.Drivers.ToDictionary(d => d.DeviceInstanceId.Display, StringComparer.OrdinalIgnoreCase);
        var counter = 0;

        foreach (var driver in newer.Drivers)
        {
            if (olderDrivers.TryGetValue(driver.DeviceInstanceId.Display, out var previous))
            {
                if (!string.Equals(previous.DriverVersion.Display, driver.DriverVersion.Display, StringComparison.OrdinalIgnoreCase))
                {
                    changes.Add(new DetectedChange
                    {
                        Id = $"CHG-DRV-{++counter:D3}",
                        Category = ComponentCategory.Driver,
                        Description = LocalizedText.Of("Change_DriverVersion", driver.DeviceName.Display),
                        OldValue = previous.DriverVersion.Display,
                        NewValue = driver.DriverVersion.Display,
                        Severity = Severity.Warning,
                        DetectedAt = newer.CapturedAt,
                    });
                }
            }
            else
            {
                changes.Add(new DetectedChange
                {
                    Id = $"CHG-DRV-{++counter:D3}",
                    Category = ComponentCategory.Driver,
                    Description = LocalizedText.Of("Change_DriverAdded", driver.DeviceName.Display),
                    OldValue = null,
                    NewValue = driver.DriverVersion.Display,
                    Severity = Severity.Info,
                    DetectedAt = newer.CapturedAt,
                });
            }
        }

        foreach (var previous in older.Drivers)
        {
            if (!newer.Drivers.Any(d => string.Equals(d.DeviceInstanceId.Display, previous.DeviceInstanceId.Display, StringComparison.OrdinalIgnoreCase)))
            {
                changes.Add(new DetectedChange
                {
                    Id = $"CHG-DRV-{++counter:D3}",
                    Category = ComponentCategory.Driver,
                    Description = LocalizedText.Of("Change_DriverRemoved", previous.DeviceName.Display),
                    OldValue = previous.DriverVersion.Display,
                    NewValue = null,
                    Severity = Severity.Warning,
                    DetectedAt = newer.CapturedAt,
                });
            }
        }

        return changes;
    }

    public static string ProblemCodeText(uint code) => code switch
    {
        1 => "Device is not configured correctly",
        3 => "Driver is corrupted",
        10 => "Device cannot start",
        12 => "Not enough free resources",
        14 => "Device cannot work properly until the computer is restarted",
        16 => "Resources are not fully identified",
        18 => "Drivers must be reinstalled",
        19 => "Registry is corrupted",
        21 => "Windows is removing the device",
        22 => "Device is disabled",
        24 => "Device is not present, not working properly or does not have all its drivers installed",
        28 => "Drivers are not installed",
        29 => "Device is disabled because the firmware did not give it the required resources",
        31 => "Device is not working properly because Windows cannot load the required drivers",
        43 => "Windows stopped the device because it reported problems",
        45 => "Device is not connected",
        _ => "See the Windows device problem code documentation",
    };
}
