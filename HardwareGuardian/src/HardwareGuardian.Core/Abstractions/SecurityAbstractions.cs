using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;

namespace HardwareGuardian.Core.Abstractions;

/// <summary>Provides configured HTTP clients with explicit timeouts (spec section 63).</summary>
public interface IHttpClientProvider
{
    HttpClient GetClient(string name, TimeSpan timeout);
}

/// <summary>Hash computation for verification (spec sections 30 and 60).</summary>
public interface IHashService
{
    Task<HashResult> ComputeFileHashAsync(string path, string algorithm = "SHA256", IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    string ComputeStringHash(string content, string algorithm = "SHA256");

    bool IsValidHashFormat(string? hash, string algorithm = "SHA256");
}

/// <summary>
/// Authenticode / WinVerifyTrust based verification. A file that could not be verified is
/// reported with <c>IsSigned = null</c> and <see cref="VerificationLevel.NotVerified"/>.
/// </summary>
public interface ISignatureVerifier
{
    SignatureResult VerifyFile(string path, CancellationToken cancellationToken = default);
}

/// <summary>How a remote source may be used at all (HTTPS only, no certificate error tolerance).</summary>
public sealed record SourcePolicy
{
    /// <summary>Host names that are allowed for this adapter. Empty means "no allow list configured".</summary>
    public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();

    public bool RequireHttps { get; init; } = true;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Trust class that the source must have to be usable for an update recommendation.</summary>
    public SourceTrust RequiredTrust { get; init; } = SourceTrust.Manufacturer;

    /// <summary>When false, a source that cannot be verified leads to BLOCKED instead of a warning.</summary>
    public bool AllowUnverifiedSources { get; init; }
}

public interface ISourceVerifier
{
    /// <summary>Performs a HEAD/GET request and evaluates HTTPS, certificate and metadata checks.</summary>
    Task<SourceVerificationResult> VerifyAsync(string url, SourcePolicy policy, CancellationToken cancellationToken);

    /// <summary>True when the URL is structurally usable (HTTPS, no credentials, no local addresses).</summary>
    bool IsAcceptableUrl(string url, out string? reason);
}

public sealed record DownloadRequest
{
    public string Url { get; init; } = string.Empty;

    public string TargetFileName { get; init; } = string.Empty;

    public string? ExpectedSha256 { get; init; }

    /// <summary>
    /// When true, an artefact whose hash could not be compared with a published value is not
    /// released for approval. Default: true (fail closed). A source that publishes no hash must be
    /// handled explicitly, not by a default that happens to be permissive.
    /// </summary>
    public bool RequireHashVerification { get; init; } = true;

    /// <summary>
    /// When true, an artefact without a verified signature is not released for approval.
    /// Default: true (fail closed, spec sections 30 and 60).
    /// </summary>
    public bool RequireSignatureVerification { get; init; } = true;

    public long MaxSizeBytes { get; init; } = 4L * 1024 * 1024 * 1024;

    public SourcePolicy Policy { get; init; } = new();

    public string CandidateId { get; init; } = string.Empty;
}

public sealed record DownloadProgress
{
    public long BytesReceived { get; init; }

    public long? TotalBytes { get; init; }

    public double? Fraction => TotalBytes.HasValue && TotalBytes.Value > 0 ? (double)BytesReceived / TotalBytes.Value : null;

    public TimeSpan Elapsed { get; init; }

    public TimeSpan? EstimatedRemaining { get; init; }

    public string StageKey { get; init; } = "Download_Stage_Download";
}

/// <summary>
/// Download pipeline: DOWNLOAD -&gt; VERIFY -&gt; HASH -&gt; SIGNATURE -&gt; COMPATIBILITY (spec section 30).
/// Installation is a separate, explicitly approved step.
/// </summary>
public interface IDownloadService
{
    Task<DownloadArtifact> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// Guards destructive file operations: deletes may only happen inside explicitly allowed roots,
/// reparse points are never followed and protected locations are refused (spec sections 77, 80).
/// </summary>
public interface IPathGuard
{
    PathGuardDecision Evaluate(string path, PathGuardIntent intent, IEnumerable<string> allowedRoots);

    bool TryNormalise(string path, out string normalised, out string? reason);
}

public enum PathGuardIntent
{
    Read,
    Measure,
    Delete,
}

public sealed record PathGuardDecision
{
    public bool Allowed { get; init; }

    public bool IsProtected { get; init; }

    public string? Path { get; init; }

    public string? ReasonCode { get; init; }

    public LocalizedText Reason { get; init; } = LocalizedText.Of("PathGuard_Unknown");

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>Backup levels and creation (spec section 25).</summary>
public sealed record BackupRequest
{
    public string OperationId { get; init; } = string.Empty;

    public OperationKind Kind { get; init; } = OperationKind.Backup;

    public RiskLevel Risk { get; init; } = RiskLevel.Medium;

    public ComponentCategory Category { get; init; } = ComponentCategory.Unknown;

    public string? ComponentId { get; init; }

    public LocalizedText Reason { get; init; } = LocalizedText.Of("Backup_Reason_Unknown");

    /// <summary>Registry keys that must be exported before the change. Empty means none.</summary>
    public IReadOnlyList<string> RegistryKeys { get; init; } = Array.Empty<string>();

    /// <summary>Files or directories that are copied to the backup folder. Empty means none.</summary>
    public IReadOnlyList<string> Paths { get; init; } = Array.Empty<string>();

    /// <summary>True when a driver/device state snapshot must be captured.</summary>
    public bool CaptureDriverState { get; init; }
}

public interface IBackupService
{
    string Location { get; }

    Task<BackupAvailability> CheckAvailabilityAsync(RiskLevel risk, CancellationToken cancellationToken);

    Task<BackupRecord> CreateAsync(BackupRequest request, IProgress<ProgressSnapshot>? progress, CancellationToken cancellationToken);

    Task<BackupRecord?> FindAsync(string recordId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BackupRecord>> ListAsync(CancellationToken cancellationToken);
}

public sealed record RollbackRequest
{
    public string BackupRecordId { get; init; } = string.Empty;

    public OperationKind Operation { get; init; } = OperationKind.Rollback;

    public string OperationKey { get; init; } = "Rollback_Operation_Default";

    public LocalizedText Reason { get; init; } = LocalizedText.Of("Rollback_Reason_Unknown");

    public ApprovalRecord? Approval { get; init; }
}

/// <summary>Rollback of a failed change. Success is only reported after verification (spec section 26).</summary>
public interface IRollbackService
{
    IReadOnlyList<BackupRecord> Available { get; }

    Task<bool> CanRollbackAsync(string backupRecordId, CancellationToken cancellationToken);

    Task<RollbackResult> RollbackAsync(RollbackRequest request, IProgress<ProgressSnapshot>? progress, CancellationToken cancellationToken);
}
