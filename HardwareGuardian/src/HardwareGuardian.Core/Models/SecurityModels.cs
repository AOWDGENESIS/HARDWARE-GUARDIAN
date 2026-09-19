using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Models;

/// <summary>Result of an Authenticode style signature verification.</summary>
public sealed record SignatureResult
{
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Three state result. <c>null</c> is used when verification was not possible -
    /// an unverified file is never reported as "unsigned" (spec section 1.3).
    /// </summary>
    public bool? IsSigned { get; init; }

    public bool IsTrusted { get; init; }

    public TextInfo Signer { get; init; }

    public TextInfo Issuer { get; init; }

    public TextInfo SubjectCommonName { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }

    public VerificationLevel Verification { get; init; } = VerificationLevel.NotVerified;

    /// <summary>Non-localised technical detail / Win32 error code.</summary>
    public string? Detail { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Signature_NotChecked");

    public static SignatureResult NotChecked(string path, string detail, LocalizedText summary) => new()
    {
        Path = path,
        IsSigned = null,
        Verification = VerificationLevel.NotVerified,
        Detail = detail,
        Summary = summary,
    };
}

/// <summary>Hash of a file or artefact, with the algorithm that was actually used.</summary>
public sealed record HashResult
{
    public string Path { get; init; } = string.Empty;

    public string Algorithm { get; init; } = "SHA256";

    public string? Hash { get; init; }

    public Measured<long> SizeBytes { get; init; } = Measured<long>.NotAvailable("size not read");

    public DateTimeOffset ComputedAt { get; init; }

    public string? Error { get; init; }

    public bool Succeeded => !string.IsNullOrWhiteSpace(Hash);

    public static HashResult Failed(string path, string algorithm, string error) => new()
    {
        Path = path,
        Algorithm = algorithm,
        Hash = null,
        Error = error,
    };
}

/// <summary>How the authenticity of a remote source was established.</summary>
public sealed record SourceVerificationResult
{
    public string Url { get; init; } = string.Empty;

    public bool Reachable { get; init; }

    public bool IsHttps { get; init; }

    public bool CertificateValid { get; init; }

    public VerificationLevel Verification { get; init; } = VerificationLevel.NotVerified;

    public SourceTrust Trust { get; init; } = SourceTrust.Unknown;

    public int? HttpStatusCode { get; init; }

    public string? ContentType { get; init; }

    public long? ContentLength { get; init; }

    public DateTimeOffset RetrievedAt { get; init; }

    public TimeSpan Duration { get; init; }

    public string? RedirectTarget { get; init; }

    public string? ErrorCode { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Source_Verification_NotPerformed");

    public IReadOnlyList<string> Checks { get; init; } = Array.Empty<string>();

    public static SourceVerificationResult Blocked(string url, string reasonCode, LocalizedText summary, string? detail = null) => new()
    {
        Url = url,
        Reachable = false,
        Verification = VerificationLevel.NotVerified,
        Trust = SourceTrust.Unknown,
        ErrorCode = reasonCode,
        Summary = summary,
        Checks = detail is null ? Array.Empty<string>() : new[] { detail },
    };
}

/// <summary>A downloaded artefact. It is never installed automatically (spec section 30).</summary>
public sealed record DownloadArtifact
{
    public string Id { get; init; } = string.Empty;

    public string CandidateId { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string LocalPath { get; init; } = string.Empty;

    public Measured<long> SizeBytes { get; init; } = Measured<long>.NotAvailable("size not reported");

    public HashResult Hash { get; init; } = new();

    /// <summary>Result of comparing the downloaded hash with the expected hash, when one exists.</summary>
    public bool? HashMatchesExpectation { get; init; }

    public SignatureResult Signature { get; init; } = new();

    public DateTimeOffset DownloadedAt { get; init; }

    public SourceTrust SourceTrust { get; init; } = SourceTrust.Unknown;

    public bool Compatible { get; init; }

    public IReadOnlyList<string> CheckLog { get; init; } = Array.Empty<string>();

    /// <summary>True only when download, hash and (where required) signature checks all passed.</summary>
    public bool IsReadyForApproval { get; init; }
}

/// <summary>Snapshot of a driver state, used for the before/after comparison and rollback.</summary>
public sealed record DriverStateSnapshot
{
    public string Id { get; init; } = string.Empty;

    public DateTimeOffset CapturedAt { get; init; }

    public IReadOnlyList<DriverRecord> Drivers { get; init; } = Array.Empty<DriverRecord>();

    public IReadOnlyList<PnpDeviceInfo> Devices { get; init; } = Array.Empty<PnpDeviceInfo>();

    public string? FilePath { get; init; }
}

/// <summary>What kind of rollback material exists for an operation (spec section 25).</summary>
public sealed record BackupAvailability
{
    public RiskLevel RequiredLevel { get; init; } = RiskLevel.Medium;

    public bool RestorePointPossible { get; init; }

    public bool RestorePointAvailable { get; init; }

    public bool ConfigurationBackupAvailable { get; init; }

    public bool RegistryBackupAvailable { get; init; }

    public bool DriverSnapshotAvailable { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Backup_Availability_Unknown");

    public IReadOnlyList<string> Details { get; init; } = Array.Empty<string>();

    public bool IsSufficientFor(RiskLevel level) => level switch
    {
        RiskLevel.Low => true,
        RiskLevel.Medium => ConfigurationBackupAvailable || RestorePointAvailable || RegistryBackupAvailable,
        RiskLevel.High => (RestorePointAvailable || RegistryBackupAvailable) && ConfigurationBackupAvailable,
        RiskLevel.Critical => RestorePointAvailable && ConfigurationBackupAvailable,
        _ => false,
    };
}

/// <summary>A backup or rollback record, always with the artefact that can be restored.</summary>
public sealed record BackupRecord
{
    public string Id { get; init; } = string.Empty;

    public string OperationId { get; init; } = string.Empty;

    public OperationKind Kind { get; init; } = OperationKind.Backup;

    public RiskLevel Risk { get; init; } = RiskLevel.Medium;

    public DateTimeOffset CreatedAt { get; init; }

    public string? ArtifactPath { get; init; }

    public string? ManifestPath { get; init; }

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Backup_Record_Summary");

    public IReadOnlyList<string> RestoreSteps { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}

/// <summary>Result of a rollback attempt. Success is only claimed after verification.</summary>
public sealed record RollbackResult
{
    public string BackupRecordId { get; init; } = string.Empty;

    public bool Attempted { get; init; }

    public bool Verified { get; init; }

    public StageOutcome Outcome { get; init; } = StageOutcome.NotRun;

    public LocalizedText Summary { get; init; } = LocalizedText.Of("Rollback_NotAttempted");

    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();

    public string? ErrorDetail { get; init; }

    public static RollbackResult NotAvailable(string recordId, LocalizedText summary) => new()
    {
        BackupRecordId = recordId,
        Attempted = false,
        Verified = false,
        Outcome = StageOutcome.NotRun,
        Summary = summary,
    };
}
