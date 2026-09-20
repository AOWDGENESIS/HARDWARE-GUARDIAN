using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>Version information for a driver, BIOS or package, including where it was read from.</summary>
public sealed record VersionInfo
{
    public TextInfo Raw { get; init; }

    public TextInfo Normalized { get; init; }

    public TextInfo ReleaseDate { get; init; }

    public TextInfo Architecture { get; init; }

    public TextInfo HardwareId { get; init; }
}

/// <summary>
/// One update candidate produced by a source check. Every candidate carries source, trust and
/// verification state, so that the UI can never claim more certainty than actually exists.
/// </summary>
public sealed record UpdateCandidate
{
    public string Id { get; init; } = string.Empty;

    public UpdateKind Kind { get; init; } = UpdateKind.Driver;

    public ComponentCategory Category { get; init; } = ComponentCategory.Driver;

    public TextInfo DeviceName { get; init; }

    public TextInfo ComponentId { get; init; }

    public VersionInfo Installed { get; init; } = new();

    public VersionInfo Available { get; init; } = new();

    public ManufacturerSourceRef Source { get; init; } = ManufacturerSourceRef.Unknown();

    public SourceTrust Trust { get; init; } = SourceTrust.Unknown;

    public VerificationLevel Verification { get; init; } = VerificationLevel.NotVerified;

    public Freshness Freshness { get; init; } = Freshness.Unknown;

    public DateTimeOffset? RetrievedAt { get; init; }

    public UpdateStatus Status { get; init; } = UpdateStatus.Unknown;

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Update_Unknown_Summary");

    public LocalizedText? ReleaseNotes { get; init; }

    public LocalizedText? WhyItMatters { get; init; }

    public bool IsSecurityRelevant { get; init; }

    public bool? IsCompatible { get; init; }

    public string? DownloadUrl { get; init; }

    public string? ExpectedSha256 { get; init; }

    public string? BlockedReasonCode { get; init; }

    public LocalizedText? BlockedReason { get; init; }

    public RiskLevel Risk { get; init; } = RiskLevel.Medium;

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>Result of one source check, including how the source itself was verified.</summary>
public sealed record SourceCheckResult
{
    public string AdapterId { get; init; } = string.Empty;

    public string DisplayNameKey { get; init; } = "Source_Unknown";

    public SourceTrust Trust { get; init; } = SourceTrust.Unknown;

    public VerificationLevel Verification { get; init; } = VerificationLevel.NotVerified;

    public IReadOnlyList<UpdateCandidate> Candidates { get; init; } = Array.Empty<UpdateCandidate>();

    public IReadOnlyList<Problem> Problems { get; init; } = Array.Empty<Problem>();

    public bool Succeeded { get; init; }

    public Freshness Freshness { get; init; } = Freshness.Unknown;

    public DateTimeOffset RetrievedAt { get; init; }

    public string? ErrorDetail { get; init; }

    public LocalizedText? Note { get; init; }

    public static SourceCheckResult NotConfigured(string adapterId, string displayNameKey, LocalizedText note) => new()
    {
        AdapterId = adapterId,
        DisplayNameKey = displayNameKey,
        Trust = SourceTrust.Unknown,
        Verification = VerificationLevel.NotVerified,
        Succeeded = false,
        Note = note,
        Freshness = Freshness.Unknown,
    };
}

/// <summary>Cached manufacturer fact with expiry and provenance (spec section 64).</summary>
public sealed record SourceCacheEntry
{
    public string Key { get; init; } = string.Empty;

    public string AdapterId { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public VerificationLevel Verification { get; init; } = VerificationLevel.NotVerified;

    public SourceTrust Trust { get; init; } = SourceTrust.Unknown;

    public DateTimeOffset RetrievedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Payload version used by the adapter that wrote the entry.</summary>
    public string PayloadVersion { get; init; } = "1";

    public string PayloadJson { get; init; } = "{}";

    public CacheDisposition Disposition(DateTimeOffset now) =>
        now >= ExpiresAt ? CacheDisposition.Expired
        : now >= ExpiresAt.AddHours(-24) ? CacheDisposition.Stale
        : CacheDisposition.Usable;
}

/// <summary>Everything the update centre shows for one device.</summary>
public sealed record UpdateAssessment
{
    public string ComponentId { get; init; } = string.Empty;

    public TextInfo DeviceName { get; init; }

    public VersionInfo Installed { get; init; } = new();

    public VersionInfo Available { get; init; } = new();

    public UpdateStatus Status { get; init; } = UpdateStatus.Unknown;

    public ManufacturerSourceRef Source { get; init; } = ManufacturerSourceRef.Unknown();

    public Freshness Freshness { get; init; } = Freshness.Unknown;

    public string? BlockedReasonCode { get; init; }

    public LocalizedText? BlockedReason { get; init; }

    public LocalizedText Reason { get; init; } = LocalizedText.Of("Update_Reason_NoSource");

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public bool CanDownload { get; init; }

    /// <summary>True when the underlying data is stale and must be refreshed before any download.</summary>
    public bool RequiresRefresh { get; init; }

    public bool IsSecurityRelevant { get; init; }

    public string? ExpectedSha256 { get; init; }

    public bool CanInstall => CanDownload
        && Status == UpdateStatus.UpdateAvailable
        && BlockedReasonCode is null
        && !RequiresRefresh
        && Freshness is Freshness.Live or Freshness.Recent;
}
