using System.Diagnostics;
using System.Net;
using WindowsMaintenanceCenter.Core;
using WindowsMaintenanceCenter.Core.Abstractions;
using WindowsMaintenanceCenter.Core.Models;
using WindowsMaintenanceCenter.Core.Values;
using WindowsMaintenanceCenter.Infrastructure.Http;

namespace WindowsMaintenanceCenter.Infrastructure.Security;

/// <summary>
/// Download pipeline (spec section 30): DOWNLOAD -> VERIFY -> HASH -> SIGNATURE -> COMPATIBILITY.
/// The artefact is stored in a quarantine folder with a metadata sidecar and is never executed
/// or installed by this service.
/// </summary>
public sealed class DownloadService : IDownloadService
{
    private readonly IHttpClientProvider _http;
    private readonly IHashService _hash;
    private readonly ISignatureVerifier _signatures;
    private readonly ISourceVerifier _sourceVerifier;
    private readonly IPathProvider _paths;
    private readonly IClock _clock;

    public DownloadService(
        IHttpClientProvider http,
        IHashService hash,
        ISignatureVerifier signatures,
        ISourceVerifier sourceVerifier,
        IPathProvider paths,
        IClock clock)
    {
        _http = http;
        _hash = hash;
        _signatures = signatures;
        _sourceVerifier = sourceVerifier;
        _paths = paths;
        _clock = clock;
    }

    public async Task<DownloadArtifact> DownloadAsync(DownloadRequest request, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var checkLog = new List<string>();
        var stopwatch = Stopwatch.StartNew();

        if (!_sourceVerifier.IsAcceptableUrl(request.Url, out var urlReason))
        {
            checkLog.Add($"url-rejected: {urlReason}");
            return Blocked(request, checkLog, $"url rejected: {urlReason}");
        }

        var verification = await _sourceVerifier.VerifyAsync(request.Url, request.Policy, cancellationToken).ConfigureAwait(false);
        checkLog.Add($"source: reachable={verification.Reachable} status={verification.HttpStatusCode}");
        if (!verification.Reachable)
        {
            return Blocked(request, checkLog, $"source not reachable: {verification.ErrorCode}");
        }

        var targetDirectory = _paths.EnsureDirectory(_paths.DownloadDirectory);
        var fileName = SanitiseFileName(request.TargetFileName);
        var targetPath = Path.Combine(targetDirectory, fileName);
        var temporaryPath = targetPath + ".part";

        long bytesReceived = 0;
        long? totalBytes = null;

        try
        {
            using var client = _http.GetClient("downloads", request.Policy.Timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(30));
            using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, request.Url), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                checkLog.Add("download-redirect-refused");
                return Blocked(request, checkLog, "the download URL redirects; the final official URL must be resolved by the adapter first");
            }

            if (!response.IsSuccessStatusCode)
            {
                checkLog.Add($"http={(int)response.StatusCode}");
                return Blocked(request, checkLog, $"HTTP {(int)response.StatusCode}");
            }

            totalBytes = response.Content.Headers.ContentLength;
            if (totalBytes.HasValue && totalBytes.Value > request.MaxSizeBytes)
            {
                checkLog.Add($"size={totalBytes.Value} exceeds limit={request.MaxSizeBytes}");
                return Blocked(request, checkLog, "artefact exceeds the configured maximum size");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                var buffer = new byte[1024 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    bytesReceived += read;
                    if (bytesReceived > request.MaxSizeBytes)
                    {
                        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                        destination.Close();
                        File.Delete(temporaryPath);
                        checkLog.Add("size-limit-exceeded-during-transfer");
                        return Blocked(request, checkLog, "artefact exceeded the maximum size during transfer");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    progress?.Report(new DownloadProgress
                    {
                        BytesReceived = bytesReceived,
                        TotalBytes = totalBytes,
                        Elapsed = stopwatch.Elapsed,
                        StageKey = "Download_Stage_Download",
                    });
                }
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporaryPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(temporaryPath);
            checkLog.Add($"transport-error: {ex.GetType().Name}: {ex.Message}");
            return Blocked(request, checkLog, $"{ex.GetType().Name}: {ex.Message}");
        }

        progress?.Report(new DownloadProgress
        {
            BytesReceived = bytesReceived,
            TotalBytes = totalBytes ?? bytesReceived,
            Elapsed = stopwatch.Elapsed,
            StageKey = "Download_Stage_Verify",
        });

        var hash = await _hash.ComputeFileHashAsync(targetPath, "SHA256", null, cancellationToken).ConfigureAwait(false);
        bool? hashMatches = null;
        if (!string.IsNullOrWhiteSpace(request.ExpectedSha256))
        {
            hashMatches = string.Equals(hash.Hash, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase);
            checkLog.Add($"hash-expected={request.ExpectedSha256}");
            checkLog.Add($"hash-actual={hash.Hash}");
        }
        else
        {
            checkLog.Add("hash-expected=none (the source did not publish one)");
        }

        var signature = _signatures.VerifyFile(targetPath, cancellationToken);
        checkLog.Add($"signature: signed={signature.IsSigned?.ToString() ?? "unknown"} trusted={signature.IsTrusted} verification={signature.Verification}");

        var compatible = hashMatches != false;
        var ready = hash.Succeeded
            && (hashMatches ?? !request.RequireHashVerification)
            && (!request.RequireSignatureVerification || signature.Verification == VerificationLevel.SignatureVerified)
            && compatible;

        var artifact = new DownloadArtifact
        {
            Id = $"DL-{_clock.Now:yyyyMMddHHmmss}",
            CandidateId = request.CandidateId,
            Url = request.Url,
            LocalPath = targetPath,
            SizeBytes = Measured<long>.Known(bytesReceived, ValueOrigin.LocalFile(_clock.Now, targetPath)),
            Hash = hash,
            HashMatchesExpectation = hashMatches,
            Signature = signature,
            DownloadedAt = _clock.Now,
            SourceTrust = verification.Trust,
            Compatible = compatible,
            CheckLog = checkLog,
            IsReadyForApproval = ready,
        };

        try
        {
            await JsonFileStoreWriteAsync(targetPath + ".json", artifact, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The sidecar is informative; the download itself stays valid.
        }

        return artifact;
    }

    private static Task JsonFileStoreWriteAsync(string path, DownloadArtifact artifact, CancellationToken cancellationToken) =>
        WindowsMaintenanceCenter.Infrastructure.Persistence.JsonFileStore.WriteAsync(path, artifact, cancellationToken);

    private static DownloadArtifact Blocked(DownloadRequest request, IReadOnlyList<string> checkLog, string reason) => new()
    {
        Id = $"DL-BLOCKED-{DateTimeOffset.Now:yyyyMMddHHmmss}",
        CandidateId = request.CandidateId,
        Url = request.Url,
        LocalPath = string.Empty,
        DownloadedAt = DateTimeOffset.Now,
        CheckLog = checkLog.Append($"blocked: {reason}").ToList(),
        IsReadyForApproval = false,
        Hash = HashResult.Failed(string.Empty, "SHA256", reason),
    };

    private static string SanitiseFileName(string name)
    {
        var cleaned = new string((name ?? string.Empty).Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "download.bin" : cleaned;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Ignore.
        }
    }
}
