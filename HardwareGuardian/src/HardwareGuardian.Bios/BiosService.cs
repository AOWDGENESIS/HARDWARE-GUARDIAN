using System.Text.RegularExpressions;
using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Services;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Bios;

/// <summary>
/// BIOS/UEFI engine (spec section 10). Hardware Guardian never flashes firmware. The best
/// automatic outcome is an informed assessment; without a verified revision and a verified
/// official source the result is BLOCKED with a reason.
/// </summary>
public sealed class BiosService : IBiosService
{
    private readonly IManufacturerResolver _resolver;
    private readonly IHashService _hash;
    private readonly ISignatureVerifier _signatures;
    private readonly IClock _clock;

    public BiosService(IManufacturerResolver resolver, IHashService hash, ISignatureVerifier signatures, IClock clock)
    {
        _resolver = resolver;
        _hash = hash;
        _signatures = signatures;
        _clock = clock;
    }

    public async Task<FirmwareAssessment> AssessUpdateAsync(SystemSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var board = snapshot.Motherboard;
        var bios = snapshot.Bios;
        var current = new VersionInfo
        {
            Raw = bios.Version,
            Normalized = bios.Version,
            ReleaseDate = bios.ReleaseDate,
        };

        var adapter = _resolver.Resolve(board.Manufacturer.Value, board.Product.Value, ComponentCategory.Bios)
            ?? _resolver.Resolve(bios.Manufacturer.Value, board.Product.Value, ComponentCategory.Bios);

        var source = _resolver.DescribeSource(adapter);
        var evidence = new List<string>
        {
            $"board={board.Manufacturer.Display} {board.Product.Display} (revision {board.Version.Display})",
            $"bios={bios.Manufacturer.Display} {bios.Version.Display} ({bios.ReleaseDate.Display})",
            $"adapter={adapter?.Id ?? "none"}",
            $"source={(string.IsNullOrWhiteSpace(source.Url) ? "not configured" : source.Url)}",
            $"revisionVerified={board.RevisionVerified}",
        };

        // Gate 1: the exact board revision must be known, otherwise every flash advice is unsafe.
        if (!board.RevisionVerified)
        {
            return Blocked(BlockReasons.BoardRevisionUnknown, "Firmware_Reason_RevisionUnknown",
                "Revision of the mainboard could not be verified safely.", current, source, evidence);
        }

        // Gate 2: an official source must be configured and it must be reachable.
        if (adapter?.Source is null || !adapter.Source.IsConfigured)
        {
            return Blocked(BlockReasons.ManufacturerSourceUnknown, "Firmware_Reason_NoSource",
                "No official BIOS source is configured for this board.", current, source, evidence);
        }

        var online = !snapshot.IsSimulation;
        evidence.Add($"supportsAutomatedCheck={adapter.SupportsAutomatedCheck}");

        if (!adapter.SupportsAutomatedCheck)
        {
            // Honest result: the manufacturer does not publish machine readable data.
            return new FirmwareAssessment
            {
                Status = UpdateStatus.Unknown,
                Reason = LocalizedText.Of("Firmware_Reason_ManualOnly", source.DisplayNameKey),
                Source = source,
                Current = current,
                RevisionVerified = true,
                RequiresManualFlash = true,
                Freshness = Freshness.Unknown,
                Evidence = evidence,
                Prerequisites = new[]
                {
                    LocalizedText.Of("Firmware_Prereq_ManualCheck", source.DisplayNameKey),
                    LocalizedText.Of("Firmware_Prereq_PowerStable"),
                    LocalizedText.Of("Firmware_Prereq_Backup"),
                },
                Preview = BuildPreview(current, null, source, LocalizedText.Of("Firmware_Reason_ManualOnly", source.DisplayNameKey)),
            };
        }

        if (!online)
        {
            return Blocked(BlockReasons.OfflineMode, "Firmware_Reason_Offline", "The application is running offline.", current, source, evidence);
        }

        var result = await adapter.CheckAsync(new ManufacturerQuery
        {
            Category = ComponentCategory.Bios,
            Motherboard = board,
            Bios = bios,
            Manufacturer = board.Manufacturer.Value ?? bios.Manufacturer.Value,
            Model = board.Product.Value,
            Offline = false,
        }, cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded || result.Candidates.Count == 0)
        {
            evidence.Add($"check-failed={(result.ErrorDetail ?? "no candidates returned")}");
            return new FirmwareAssessment
            {
                Status = UpdateStatus.Unknown,
                Reason = LocalizedText.Of("Firmware_Reason_CheckUnavailable", source.DisplayNameKey),
                BlockedReasonCode = BlockReasons.OfficialSourceUnreachable,
                Source = source,
                Current = current,
                RevisionVerified = true,
                Freshness = result.Freshness,
                Evidence = evidence,
                RequiresManualFlash = true,
            };
        }

        var latest = result.Candidates.OrderByDescending(c => c.Available.Normalized.Display, StringComparer.OrdinalIgnoreCase).First();
        var comparison = VersionComparer.Compare(current.Raw.Value, latest.Available.Raw.Value);
        evidence.Add($"comparison={comparison}");

        if (comparison == VersionComparison.AvailableIsNewer && result.Verification >= VerificationLevel.MetadataMatch)
        {
            return new FirmwareAssessment
            {
                Status = UpdateStatus.UpdateAvailable,
                Reason = LocalizedText.Of("Firmware_Reason_UpdateAvailable", latest.Available.Raw.Display, source.DisplayNameKey),
                Source = source,
                Current = current,
                Latest = latest.Available,
                ReleaseNotes = latest.ReleaseNotes,
                WhyItMatters = latest.WhyItMatters,
                RevisionVerified = true,
                IsSecurityRelevant = latest.IsSecurityRelevant,
                Freshness = result.Freshness,
                Evidence = evidence,
                RequiresManualFlash = true,
                Prerequisites = new[]
                {
                    LocalizedText.Of("Firmware_Prereq_OfficialTool", source.DisplayNameKey),
                    LocalizedText.Of("Firmware_Prereq_PowerStable"),
                    LocalizedText.Of("Firmware_Prereq_Backup"),
                    LocalizedText.Of("Firmware_Prereq_NoThirdParty"),
                },
                Preview = BuildPreview(current, latest.Available, source, latest.Summary),
            };
        }

        return new FirmwareAssessment
        {
            Status = comparison == VersionComparison.Same ? UpdateStatus.Current : UpdateStatus.Unknown,
            Reason = comparison == VersionComparison.Same
                ? LocalizedText.Of("Firmware_Reason_UpToDate")
                : LocalizedText.Of("Firmware_Reason_NotComparable"),
            Source = source,
            Current = current,
            Latest = latest.Available,
            RevisionVerified = true,
            Freshness = result.Freshness,
            Evidence = evidence,
            RequiresManualFlash = true,
        };
    }

    public async Task<FirmwareFileAssessment> InspectFirmwareFileAsync(string filePath, SystemSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var findings = new List<LocalizedText>();
        var details = new List<string>();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new FirmwareFileAssessment
            {
                FilePath = filePath ?? string.Empty,
                Summary = LocalizedText.Of("FirmwareFile_Summary_Missing"),
                InspectedAt = _clock.Now,
            };
        }

        var hash = await _hash.ComputeFileHashAsync(filePath, "SHA256", null, cancellationToken).ConfigureAwait(false);
        var signature = _signatures.VerifyFile(filePath, cancellationToken);
        details.Add($"hash={hash.Hash ?? "unavailable"}");
        details.Add($"signature={signature.IsSigned?.ToString() ?? "unverified"} ({signature.Verification})");

        var fileName = Path.GetFileName(filePath);
        var fileNameVersion = ExtractVersion(fileName);
        var board = snapshot.Motherboard;
        var boardProduct = board.Product.Value ?? string.Empty;
        var boardManufacturer = board.Manufacturer.Value ?? string.Empty;

        var modelMatches = !string.IsNullOrWhiteSpace(boardProduct)
            && fileName.Contains(NormaliseToken(boardProduct), StringComparison.OrdinalIgnoreCase);
        var manufacturerMatches = !string.IsNullOrWhiteSpace(boardManufacturer)
            && fileName.Contains(NormaliseToken(boardManufacturer), StringComparison.OrdinalIgnoreCase);
        var versionNewer = fileNameVersion.IsKnown && VersionComparer.IsNewer(snapshot.Bios.Version.Value, fileNameVersion.Value);

        if (!fileNameVersion.IsKnown)
        {
            findings.Add(LocalizedText.Of("FirmwareFile_Finding_NoVersion"));
        }

        if (!modelMatches)
        {
            findings.Add(LocalizedText.Of("FirmwareFile_Finding_ModelNotInName", boardProduct));
        }

        if (!manufacturerMatches)
        {
            findings.Add(LocalizedText.Of("FirmwareFile_Finding_ManufacturerNotInName", boardManufacturer));
        }

        if (signature.Verification != VerificationLevel.SignatureVerified)
        {
            findings.Add(LocalizedText.Of("FirmwareFile_Finding_NoSignature"));
        }

        if (!board.RevisionVerified)
        {
            findings.Add(LocalizedText.Of("FirmwareFile_Finding_RevisionUnknown"));
        }

        if (versionNewer)
        {
            findings.Add(LocalizedText.Of("FirmwareFile_Finding_NewerVersion", fileNameVersion.Value ?? string.Empty, snapshot.Bios.Version.Display));
        }

        var compatible = modelMatches && board.RevisionVerified;
        var recommended = compatible && customChecksPass(signature, hash);

        return new FirmwareFileAssessment
        {
            FilePath = filePath,
            Hash = hash,
            Signature = signature,
            Verification = signature.Verification,
            FileNameVersion = fileNameVersion,
            IdentifiedManufacturer = TextInfo.From(manufacturerMatches ? boardManufacturer : null, ValueOrigin.LocalFile(_clock.Now, fileName), "manufacturer name not found in file name"),
            IdentifiedModel = TextInfo.From(modelMatches ? boardProduct : null, ValueOrigin.LocalFile(_clock.Now, fileName), "board model not found in file name"),
            FileNamePlausible = fileNameVersion.IsKnown,
            ManufacturerMatches = manufacturerMatches,
            ModelMatches = modelMatches,
            VersionNewer = versionNewer,
            VersionMatchesKnownRelease = false,
            CompatibilityVerified = compatible,
            IsRecommended = recommended,
            Findings = findings,
            TechnicalDetails = details,
            InspectedAt = _clock.Now,
            Summary = recommended
                ? LocalizedText.Of("FirmwareFile_Summary_Plausible")
                : LocalizedText.Of("FirmwareFile_Summary_NotVerified"),
        };

        static bool customChecksPass(SignatureResult signature, HashResult hash) => hash.Succeeded && signature.Verification >= VerificationLevel.MetadataMatch;
    }

    private static IReadOnlyList<ChangePreview> BuildPreview(VersionInfo current, VersionInfo? latest, ManufacturerSourceRef source, LocalizedText reason) => new[]
    {
        new ChangePreview
        {
            LabelKey = "Preview_BiosVersion",
            OldValue = current.Raw.Display,
            NewValue = latest?.Raw.Display,
            Reason = reason,
            SourceToken = source.Trust.ToString(),
            Risk = RiskLevel.Critical,
        },
    };

    private static FirmwareAssessment Blocked(string code, string reasonKey, string detail, VersionInfo current, ManufacturerSourceRef source, List<string> evidence)
    {
        evidence.Add($"blocked={code}; {detail}");
        return new FirmwareAssessment
        {
            Status = UpdateStatus.Blocked,
            Reason = LocalizedText.Of(reasonKey),
            BlockedReasonCode = code,
            Source = source,
            Current = current,
            RevisionVerified = false,
            RequiresManualFlash = true,
            Evidence = evidence,
        };
    }

    private static TextInfo ExtractVersion(string fileName)
    {
        var match = Regex.Match(fileName, @"[_\-\s]?([A-Z]{1,3}\d{1,4}[A-Z]?|\d+\.\d+(\.\d+)*)", RegexOptions.IgnoreCase);
        return match.Success
            ? TextInfo.Known(match.Groups[1].Value, ValueOrigin.LocalFile(DateTimeOffset.Now, fileName))
            : TextInfo.Unknown(ValueOrigin.LocalFile(DateTimeOffset.Now, fileName), "no version pattern found in the file name");
    }

    private static string NormaliseToken(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());
}
