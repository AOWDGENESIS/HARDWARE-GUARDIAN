using WindowsMaintenanceCenter.Core.Values;

namespace WindowsMaintenanceCenter.Core.Models;

/// <summary>
/// Assessment of a possible BIOS/UEFI update. Windows Maintenance Center never flashes firmware
/// automatically (spec section 10): the best outcome of an automatic check is an informed,
/// blocked recommendation with a link to the official source.
/// </summary>
public sealed record FirmwareAssessment
{
    public UpdateStatus Status { get; init; } = UpdateStatus.Unknown;

    public LocalizedText Reason { get; init; } = LocalizedText.Of("Firmware_Reason_NoSource");

    public string? BlockedReasonCode { get; init; }

    public ManufacturerSourceRef Source { get; init; } = ManufacturerSourceRef.Unknown();

    public VersionInfo Current { get; init; } = new();

    public VersionInfo? Latest { get; init; }

    public LocalizedText? ReleaseNotes { get; init; }

    public LocalizedText? WhyItMatters { get; init; }

    public bool RevisionVerified { get; init; }

    /// <summary>True when the firmware can only be flashed from firmware setup / vendor tooling.</summary>
    public bool RequiresManualFlash { get; init; } = true;

    public bool IsSecurityRelevant { get; init; }

    public Freshness Freshness { get; init; } = Freshness.Unknown;

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public IReadOnlyList<LocalizedText> Prerequisites { get; init; } = Array.Empty<LocalizedText>();

    public IReadOnlyList<ChangePreview> Preview { get; init; } = Array.Empty<ChangePreview>();
}

/// <summary>
/// Result of inspecting a firmware file that the user selected locally. Nothing is executed:
/// the file is hashed, its signature is checked and its identity is compared with the board.
/// </summary>
public sealed record FirmwareFileAssessment
{
    public string FilePath { get; init; } = string.Empty;

    public HashResult Hash { get; init; } = new();

    public SignatureResult Signature { get; init; } = new();

    public VerificationLevel Verification { get; init; } = VerificationLevel.NotVerified;

    public TextInfo FileNameVersion { get; init; }

    public TextInfo IdentifiedManufacturer { get; init; }

    public TextInfo IdentifiedModel { get; init; }

    public bool FileNamePlausible { get; init; }

    public bool ManufacturerMatches { get; init; }

    public bool ModelMatches { get; init; }

    public bool VersionNewer { get; init; }

    public bool VersionMatchesKnownRelease { get; init; }

    public bool CompatibilityVerified { get; init; }

    public bool IsRecommended { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("FirmwareFile_Summary_NotChecked");

    public IReadOnlyList<LocalizedText> Findings { get; init; } = Array.Empty<LocalizedText>();

    public IReadOnlyList<string> TechnicalDetails { get; init; } = Array.Empty<string>();

    public DateTimeOffset InspectedAt { get; init; }
}
