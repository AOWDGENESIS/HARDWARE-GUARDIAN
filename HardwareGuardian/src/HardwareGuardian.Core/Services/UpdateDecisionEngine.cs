using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Services;

/// <summary>Input for one update decision. Every field is evidence, not opinion.</summary>
public sealed record UpdateDecisionInput
{
    public string ComponentId { get; init; } = string.Empty;

    public TextInfo DeviceName { get; init; }

    public VersionInfo Installed { get; init; } = new();

    public VersionInfo Available { get; init; } = new();

    public ManufacturerSourceRef Source { get; init; } = ManufacturerSourceRef.Unknown();

    public SourceTrust Trust { get; init; } = SourceTrust.Unknown;

    public VerificationLevel Verification { get; init; } = VerificationLevel.NotVerified;

    /// <summary>True only when the manufacturer/hardware match was actually proven, not assumed.</summary>
    public bool IsHardwareMatchProven { get; init; }

    /// <summary><c>null</c> means "not verified", not "compatible".</summary>
    public bool? IsCompatible { get; init; }

    public bool IsSecurityRelevant { get; init; }

    public Freshness Freshness { get; init; } = Freshness.Unknown;

    public bool OfflineMode { get; init; }

    public bool SimulationMode { get; init; }

    public bool HasDownloadUrl { get; init; }

    public string? ExpectedSha256 { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The update comparison engine (spec sections 13, 14 and 49). It never recommends an update
/// because a version number is higher: source, trust, verification, hardware match and
/// compatibility must all be established, otherwise the result is BLOCKED or UNKNOWN.
/// </summary>
public sealed class UpdateDecisionEngine
{
    /// <summary>Sources at or above this trust class may be used for a recommendation.</summary>
    public const SourceTrust MinimumTrust = SourceTrust.VerifiedOem;

    /// <summary>Facts must at least be matched against the local hardware.</summary>
    public const VerificationLevel MinimumVerification = VerificationLevel.MetadataMatch;

    public UpdateAssessment Evaluate(UpdateDecisionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var evidence = input.Evidence.ToList();

        if (input.SimulationMode)
        {
            return Blocked(input, BlockReasons.SimulationMode, "Update_Reason_Simulation", evidence);
        }

        if (input.OfflineMode && !input.Source.IsKnown)
        {
            return Blocked(input, BlockReasons.OfflineMode, "Update_Reason_Offline", evidence);
        }

        if (!input.Source.IsKnown || string.IsNullOrWhiteSpace(input.Source.Url))
        {
            return Blocked(input, BlockReasons.ManufacturerSourceUnknown, "Update_Reason_SourceUnknown", evidence);
        }

        if (input.Trust < MinimumTrust)
        {
            evidence.Add($"trust={input.Trust} < required={MinimumTrust}");
            return Blocked(input, BlockReasons.ThirdPartySource, "Update_Reason_ThirdParty", evidence);
        }

        var effectiveVerification = input.Verification > input.Source.Verification ? input.Source.Verification : input.Verification;
        if (effectiveVerification < MinimumVerification)
        {
            evidence.Add($"verification={effectiveVerification} < required={MinimumVerification}");
            return Blocked(input, BlockReasons.ManufacturerSourceNotVerifiable, "Update_Reason_NotVerified", evidence);
        }

        if (!input.IsHardwareMatchProven)
        {
            return Blocked(input, BlockReasons.HardwareMatchNotProven, "Update_Reason_HardwareMismatch", evidence);
        }

        if (input.IsCompatible == false)
        {
            return new UpdateAssessment
            {
                ComponentId = input.ComponentId,
                DeviceName = input.DeviceName,
                Installed = input.Installed,
                Available = input.Available,
                Status = UpdateStatus.Incompatible,
                Source = input.Source,
                Freshness = input.Freshness,
                Reason = LocalizedText.Of("Update_Reason_Incompatible"),
                Evidence = evidence,
                CanDownload = false,
            };
        }

        var comparison = VersionComparer.Compare(input.Installed.Normalized.IsKnown ? input.Installed.Normalized.Value : input.Installed.Raw.Value,
            input.Available.Normalized.IsKnown ? input.Available.Normalized.Value : input.Available.Raw.Value);

        if (comparison == VersionComparison.Unknown)
        {
            return new UpdateAssessment
            {
                ComponentId = input.ComponentId,
                DeviceName = input.DeviceName,
                Installed = input.Installed,
                Available = input.Available,
                Status = UpdateStatus.Unknown,
                Source = input.Source,
                Freshness = input.Freshness,
                Reason = LocalizedText.Of("Update_Reason_VersionUnknown"),
                Evidence = evidence,
                CanDownload = false,
            };
        }

        if (comparison == VersionComparison.NotComparable)
        {
            return new UpdateAssessment
            {
                ComponentId = input.ComponentId,
                DeviceName = input.DeviceName,
                Installed = input.Installed,
                Available = input.Available,
                Status = UpdateStatus.Unknown,
                Source = input.Source,
                Freshness = input.Freshness,
                Reason = LocalizedText.Of("Update_Reason_NotComparable"),
                Evidence = evidence,
                CanDownload = false,
            };
        }

        if (comparison == VersionComparison.AvailableIsOlder || comparison == VersionComparison.Same)
        {
            return new UpdateAssessment
            {
                ComponentId = input.ComponentId,
                DeviceName = input.DeviceName,
                Installed = input.Installed,
                Available = input.Available,
                Status = UpdateStatus.Current,
                Source = input.Source,
                Freshness = input.Freshness,
                Reason = LocalizedText.Of("Update_Reason_UpToDate"),
                Evidence = evidence,
                CanDownload = false,
                IsSecurityRelevant = input.IsSecurityRelevant,
            };
        }

        // The candidate is newer. Compatibility must still be positively established.
        if (input.IsCompatible is null)
        {
            return new UpdateAssessment
            {
                ComponentId = input.ComponentId,
                DeviceName = input.DeviceName,
                Installed = input.Installed,
                Available = input.Available,
                Status = UpdateStatus.Warning,
                Source = input.Source,
                Freshness = input.Freshness,
                Reason = LocalizedText.Of("Update_Reason_CompatibilityUnproven"),
                Evidence = evidence,
                CanDownload = false,
                IsSecurityRelevant = input.IsSecurityRelevant,
                RequiresRefresh = input.Freshness is Freshness.Stale,
            };
        }

        var status = input.IsSecurityRelevant ? UpdateStatus.UpdateAvailable : UpdateStatus.Optional;

        // Freshness has no "Expired" member (that belongs to CacheDisposition): Stale means the
        // cached fact is too old to justify a decision, so it must be refreshed first.
        var stale = input.Freshness == Freshness.Stale;
        if (stale)
        {
            evidence.Add($"freshness={input.Freshness}; refresh required before any download");
            status = UpdateStatus.Unknown;
        }

        return new UpdateAssessment
        {
            ComponentId = input.ComponentId,
            DeviceName = input.DeviceName,
            Installed = input.Installed,
            Available = input.Available,
            Status = status,
            Source = input.Source,
            Freshness = input.Freshness,
            Reason = stale
                ? LocalizedText.Of("Update_Reason_Expired")
                : input.IsSecurityRelevant
                    ? LocalizedText.Of("Update_Reason_UpdateAvailable")
                    : LocalizedText.Of("Update_Reason_Optional"),
            Evidence = evidence,
            CanDownload = !stale && input.HasDownloadUrl,
            IsSecurityRelevant = input.IsSecurityRelevant,
            RequiresRefresh = stale,
            ExpectedSha256 = input.ExpectedSha256,
        };
    }

    private static UpdateAssessment Blocked(UpdateDecisionInput input, string reasonCode, string reasonKey, List<string> evidence) => new()
    {
        ComponentId = input.ComponentId,
        DeviceName = input.DeviceName,
        Installed = input.Installed,
        Available = input.Available,
        Status = UpdateStatus.Blocked,
        Source = input.Source,
        Freshness = input.Freshness,
        BlockedReasonCode = reasonCode,
        BlockedReason = LocalizedText.Of(reasonKey),
        Reason = LocalizedText.Of(reasonKey),
        Evidence = evidence,
        CanDownload = false,
        RequiresRefresh = false,
    };
}
