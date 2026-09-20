using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Services;
using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Manufacturer;

/// <summary>
/// Update centre (spec sections 11 - 13, 36, 49). It checks the official sources in the mandated
/// order and lets <see cref="UpdateDecisionEngine"/> decide: a higher version number alone never
/// becomes a recommendation. The Windows Update channel is handled by
/// <see cref="IWindowsHealthService"/>; this service covers the manufacturer channel and the
/// per-component assessment shown in the hardware view.
/// </summary>
public sealed class UpdateCenterService : IUpdateCenter
{
    private const string ModuleKey = "UPD";

    private readonly IManufacturerResolver _resolver;
    private readonly UpdateDecisionEngine _decision;
    private readonly IEnvironmentProbe _environment;
    private readonly ISettingsService _settings;
    private readonly ILiveProtocol _protocol;
    private readonly IClock _clock;
    private readonly List<SourceCheckResult> _lastResults = new();

    public UpdateCenterService(
        IManufacturerResolver resolver,
        UpdateDecisionEngine decision,
        IEnvironmentProbe environment,
        ISettingsService settings,
        ILiveProtocol protocol,
        IClock clock)
    {
        _resolver = resolver;
        _decision = decision;
        _environment = environment;
        _settings = settings;
        _protocol = protocol;
        _clock = clock;
    }

    public IReadOnlyList<SourceCheckResult> LastResults => _lastResults.ToList();

    public event EventHandler<UpdateAssessment>? AssessmentCompleted;

    public async Task<IReadOnlyList<UpdateAssessment>> EvaluateAsync(SystemSnapshot snapshot, IProgressReporter progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var components = CollectComponents(snapshot);
        var assessments = new List<UpdateAssessment>();

        progress.Start("Progress_Updates", ModuleKey, components.Count);
        _protocol.Info(ModuleKey, LocalizedText.Of("Updates_Check_Started", components.Count));

        var index = 0;
        foreach (var component in components)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progress.ReportFraction((double)index / Math.Max(1, components.Count), "Progress_Updates_Component", component.Name.Display);

            var assessment = await EvaluateComponentAsync(component, snapshot, cancellationToken).ConfigureAwait(false);
            assessments.Add(assessment);
            Raise(assessment);
        }

        progress.Complete(true);

        var blocked = assessments.Count(a => a.Status == UpdateStatus.Blocked);
        _protocol.Report(
            ModuleKey,
            LocalizedText.Of("Updates_Check_Completed", assessments.Count, blocked),
            blocked > 0 ? Severity.Warning : Severity.Success);

        return assessments;
    }

    public async Task<UpdateAssessment> EvaluateComponentAsync(HardwareComponent component, SystemSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(component);
        ArgumentNullException.ThrowIfNull(snapshot);

        var adapter = _resolver.Resolve(
            component.Manufacturer.Value ?? string.Empty,
            component.Model.Value ?? component.Name.Value,
            component.Category);

        var installed = InstalledVersionFor(component, snapshot);
        var sourceRef = _resolver.DescribeSource(adapter);

        if (adapter is null)
        {
            return Unavailable(component, installed, ManufacturerSourceRef.Unknown("no adapter matched the detected hardware"),
                "Updates_Reason_NoAdapter",
                BlockReasons.ManufacturerSourceUnknown);
        }

        if (!_settings.Current.ManufacturerSourcesEnabled)
        {
            return Unavailable(component, installed, sourceRef, "Updates_Reason_SourcesDisabled", BlockReasons.ManufacturerSourceUnknown);
        }

        if (snapshot.IsSimulation)
        {
            return Unavailable(component, installed, sourceRef, "Updates_Reason_Simulation", BlockReasons.SimulationMode);
        }

        if (_environment.IsOfflineRequested)
        {
            return Unavailable(component, installed, sourceRef, "Updates_Reason_Offline", BlockReasons.OfflineMode);
        }

        if (!adapter.SupportsAutomatedCheck)
        {
            // Honest result: the vendor publishes no machine readable data. The UI shows the
            // official page and the manual comparison steps instead of pretending an update exists.
            return new UpdateAssessment
            {
                ComponentId = component.Id,
                DeviceName = component.Name,
                Installed = installed,
                Status = UpdateStatus.Unknown,
                Source = sourceRef,
                Freshness = Freshness.Unknown,
                Reason = LocalizedText.Of("Updates_Reason_ManualCheck", adapter.DisplayNameKey),
                Evidence = new[]
                {
                    $"adapter={adapter.Id}",
                    $"supportsAutomatedCheck=false",
                    $"verificationMethod={adapter.Source?.Method}",
                    $"source={(string.IsNullOrWhiteSpace(sourceRef.Url) ? "not configured" : sourceRef.Url)}",
                },
                CanDownload = false,
                RequiresRefresh = false,
            };
        }

        var result = await adapter.CheckAsync(BuildQuery(component, snapshot), cancellationToken).ConfigureAwait(false);
        Remember(result);

        if (!result.Succeeded || result.Candidates.Count == 0)
        {
            return new UpdateAssessment
            {
                ComponentId = component.Id,
                DeviceName = component.Name,
                Installed = installed,
                Status = UpdateStatus.Unknown,
                Source = sourceRef with { Verification = result.Verification },
                Freshness = result.Freshness,
                BlockedReasonCode = result.ErrorDetail is null ? null : BlockReasons.OfficialSourceUnreachable,
                BlockedReason = result.ErrorDetail is null ? null : LocalizedText.Of("Updates_Reason_SourceUnavailable", adapter.DisplayNameKey),
                Reason = LocalizedText.Of("Updates_Reason_SourceUnavailable", adapter.DisplayNameKey),
                Evidence = new[] { $"adapter={adapter.Id}", $"error={result.ErrorDetail ?? "no candidates returned"}" },
                CanDownload = false,
            };
        }

        var candidate = result.Candidates[0];
        var input = new UpdateDecisionInput
        {
            ComponentId = component.Id,
            DeviceName = component.Name,
            Installed = installed,
            Available = candidate.Available,
            Source = sourceRef with { Verification = result.Verification, RetrievedAt = result.RetrievedAt, Url = candidate.DownloadUrl ?? sourceRef.Url },
            Trust = result.Trust,
            Verification = result.Verification,
            IsHardwareMatchProven = IsHardwareMatchProven(component, candidate, snapshot),
            IsCompatible = candidate.IsCompatible,
            IsSecurityRelevant = candidate.IsSecurityRelevant,
            Freshness = result.Freshness,
            OfflineMode = _environment.IsOfflineRequested,
            SimulationMode = snapshot.IsSimulation,
            HasDownloadUrl = !string.IsNullOrWhiteSpace(candidate.DownloadUrl),
            ExpectedSha256 = candidate.ExpectedSha256,
            Evidence = new List<string>(candidate.Evidence)
            {
                $"adapter={adapter.Id}",
                $"trust={result.Trust}",
                $"verification={result.Verification}",
                $"kind={candidate.Kind}",
            },
        };

        return _decision.Evaluate(input);
    }

    public async Task<SourceCheckResult> CheckAdapterAsync(string adapterId, SystemSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var adapter = _resolver.Adapters.FirstOrDefault(a => string.Equals(a.Id, adapterId, StringComparison.OrdinalIgnoreCase));
        if (adapter is null)
        {
            var unknown = SourceCheckResult.NotConfigured(adapterId, "Source_Unknown", LocalizedText.Of("Source_Check_UnknownAdapter", adapterId));
            Remember(unknown);
            return unknown;
        }

        if (_environment.IsOfflineRequested)
        {
            var offline = new SourceCheckResult
            {
                AdapterId = adapter.Id,
                DisplayNameKey = adapter.DisplayNameKey,
                Trust = adapter.Trust,
                Succeeded = false,
                ErrorDetail = BlockReasons.OfflineMode,
                Note = LocalizedText.Of("Source_Check_Offline"),
                Freshness = Freshness.Unknown,
                RetrievedAt = _clock.Now,
            };
            Remember(offline);
            return offline;
        }

        var result = await adapter.CheckAsync(new ManufacturerQuery
        {
            Category = adapter.Categories.FirstOrDefault(ComponentCategory.Driver),
            Manufacturer = snapshot.System.Manufacturer.Value,
            Model = snapshot.System.ComputerModel.Value,
            Motherboard = snapshot.Motherboard,
            Bios = snapshot.Bios,
            Offline = false,
        }, cancellationToken).ConfigureAwait(false);

        Remember(result);
        return result;
    }

    /// <summary>
    /// The components the update check looks at. The list comes from the snapshot; where the
    /// orchestration did not build a component yet, one is derived from the collected hardware.
    /// </summary>
    private static IReadOnlyList<HardwareComponent> CollectComponents(SystemSnapshot snapshot)
    {
        var components = new List<HardwareComponent>();

        components.AddRange(snapshot.Components.Where(c => c.Category is ComponentCategory.Bios or ComponentCategory.Motherboard
            or ComponentCategory.Graphics or ComponentCategory.Storage or ComponentCategory.Cpu or ComponentCategory.Chipset
            or ComponentCategory.Network or ComponentCategory.Firmware));

        if (components.All(c => c.Category != ComponentCategory.Bios))
        {
            components.Add(new HardwareComponent
            {
                Id = "bios",
                Category = ComponentCategory.Bios,
                CategoryKey = "Component_Bios",
                Name = TextInfo.From(snapshot.Bios.Version.Value, snapshot.Bios.Version.Origin, "BIOS version not reported"),
                Manufacturer = snapshot.Bios.Manufacturer,
                Model = snapshot.Motherboard.Product,
                Status = HealthStatus.Unknown,
                SortOrder = 10,
            });
        }

        foreach (var gpu in snapshot.Graphics.Where(g => g.Name.IsKnown))
        {
            if (components.Any(c => c.Category == ComponentCategory.Graphics && string.Equals(c.Name.Display, gpu.Name.Display, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            components.Add(new HardwareComponent
            {
                Id = $"gpu:{gpu.PnpDeviceId.Display}",
                Category = ComponentCategory.Graphics,
                CategoryKey = "Component_Graphics",
                Name = gpu.Name,
                Manufacturer = gpu.Manufacturer,
                Model = gpu.Name,
                Status = HealthStatus.Unknown,
                DeviceInstanceId = gpu.PnpDeviceId,
                SortOrder = 20,
            });
        }

        foreach (var device in snapshot.Storage.Where(d => d.FriendlyName.IsKnown))
        {
            if (components.Any(c => c.Category == ComponentCategory.Storage && string.Equals(c.Name.Display, device.FriendlyName.Display, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            components.Add(new HardwareComponent
            {
                Id = $"storage:{device.SerialNumber.Display}",
                Category = ComponentCategory.Storage,
                CategoryKey = "Component_Storage",
                Name = device.FriendlyName,
                Manufacturer = device.Manufacturer,
                Model = device.Model,
                Status = HealthStatus.Unknown,
                SortOrder = 30,
            });
        }

        return components;
    }

    private static VersionInfo InstalledVersionFor(HardwareComponent component, SystemSnapshot snapshot) => component.Category switch
    {
        ComponentCategory.Bios => new VersionInfo
        {
            Raw = snapshot.Bios.Version,
            Normalized = snapshot.Bios.Version,
            ReleaseDate = snapshot.Bios.ReleaseDate,
        },
        ComponentCategory.Graphics => new VersionInfo
        {
            Raw = component.Driver?.Version ?? TextInfo.Unknown(ValueOrigin.Unknown, "driver version is not reported for this adapter"),
            HardwareId = component.HardwareIds.FirstOrDefault() is { } hardwareId
                ? TextInfo.Known(hardwareId, ValueOrigin.Create(DataSource.Pnp, SensorQuality.High, DateTimeOffset.Now, "HardwareID"))
                : TextInfo.Unknown(),
        },
        ComponentCategory.Storage => new VersionInfo
        {
            Raw = component.Driver?.Version ?? TextInfo.Unknown(ValueOrigin.Unknown, "firmware revision is not reported for this device"),
        },
        _ => new VersionInfo
        {
            Raw = component.Driver?.Version ?? TextInfo.Unknown(ValueOrigin.Unknown, "version is not reported for this component"),
        },
    };

    /// <summary>
    /// A hardware match is only "proven" when an unambiguous local identifier matches the
    /// candidate: the board model plus a verified revision for firmware, or a hardware id whose
    /// vendor matches for other components. Anything weaker stays unproven.
    /// </summary>
    private static bool IsHardwareMatchProven(HardwareComponent component, UpdateCandidate candidate, SystemSnapshot snapshot)
    {
        switch (component.Category)
        {
            case ComponentCategory.Bios:
            {
                var model = snapshot.Motherboard.Product.Value;
                var manufacturer = snapshot.Motherboard.Manufacturer.Value;
                if (string.IsNullOrWhiteSpace(model) || !snapshot.Motherboard.RevisionVerified)
                {
                    return false;
                }

                var statesModel = candidate.Evidence.Any(e => e.Contains(model, StringComparison.OrdinalIgnoreCase));
                var statesManufacturer = string.IsNullOrWhiteSpace(manufacturer)
                    || candidate.Evidence.Any(e => e.Contains(manufacturer, StringComparison.OrdinalIgnoreCase));

                return statesModel && statesManufacturer;
            }

            case ComponentCategory.Graphics:
            case ComponentCategory.Storage:
            case ComponentCategory.Network:
            {
                var hardwareId = component.HardwareIds.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(hardwareId))
                {
                    return false;
                }

                var vendor = ManufacturerResolver.ExtractVendorId(hardwareId);
                return vendor is not null && candidate.Evidence.Any(e => e.Contains(vendor, StringComparison.OrdinalIgnoreCase));
            }

            default:
                return false;
        }
    }

    private static ManufacturerQuery BuildQuery(HardwareComponent component, SystemSnapshot snapshot) => new()
    {
        Category = component.Category,
        Component = component,
        Processor = snapshot.Processors.FirstOrDefault(),
        Graphics = snapshot.Graphics.FirstOrDefault(),
        Storage = snapshot.Storage.FirstOrDefault(),
        Motherboard = snapshot.Motherboard,
        Bios = snapshot.Bios,
        Manufacturer = component.Manufacturer.Value,
        Model = component.Model.Value ?? component.Name.Value,
        HardwareId = component.HardwareIds.FirstOrDefault(),
        Offline = false,
    };

    private UpdateAssessment Unavailable(HardwareComponent component, VersionInfo installed, ManufacturerSourceRef source, string reasonKey, string? reasonCode) => new()
    {
        ComponentId = component.Id,
        DeviceName = component.Name,
        Installed = installed,
        Status = reasonCode is null ? UpdateStatus.Unknown : UpdateStatus.Blocked,
        Source = source,
        Freshness = Freshness.Unknown,
        BlockedReasonCode = reasonCode,
        BlockedReason = reasonCode is null ? null : LocalizedText.Of(reasonKey),
        Reason = LocalizedText.Of(reasonKey),
        Evidence = new[] { $"component={component.Id}", $"category={component.Category}", $"source={source.AdapterId}" },
        CanDownload = false,
    };

    private void Remember(SourceCheckResult result)
    {
        _lastResults.RemoveAll(entry => string.Equals(entry.AdapterId, result.AdapterId, StringComparison.OrdinalIgnoreCase));
        _lastResults.Add(result);
    }

    private void Raise(UpdateAssessment assessment)
    {
        var handler = AssessmentCompleted;
        if (handler is null)
        {
            return;
        }

        foreach (EventHandler<UpdateAssessment> subscriber in handler.GetInvocationList())
        {
            try
            {
                subscriber(this, assessment);
            }
            catch (Exception)
            {
                // A failing subscriber must not abort the update check.
            }
        }
    }
}
