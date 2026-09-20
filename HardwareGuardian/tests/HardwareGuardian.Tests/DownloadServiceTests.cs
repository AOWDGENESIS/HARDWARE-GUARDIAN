using System.Net;
using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;
using HardwareGuardian.Infrastructure.Security;
using Xunit;

namespace HardwareGuardian.Tests;

/// <summary>
/// Download pipeline (spec sections 30 and 60). The transport is a stub handler, so the test checks
/// the real rules of <see cref="DownloadService"/>: nothing is released for approval unless the
/// hash and the signature were verified, and a redirect is never followed silently.
/// </summary>
public sealed class DownloadServiceTests
{
    private const string Payload = "synthetic-artefact-content";

    [Fact]
    public async Task An_artefact_without_a_published_hash_is_not_released_for_approval()
    {
        using var paths = new TempPathProvider();
        var service = CreateService(paths, new StubSignatureVerifier(signed: true));

        // No ExpectedSha256: the default must stay fail closed instead of handing the file out.
        var artifact = await service.DownloadAsync(Request(), null, CancellationToken.None);

        Assert.False(artifact.IsReadyForApproval);
        Assert.Null(artifact.HashMatchesExpectation);
    }

    [Fact]
    public async Task An_artefact_whose_signature_is_not_verified_is_not_released_for_approval()
    {
        using var paths = new TempPathProvider();
        var service = CreateService(paths, new StubSignatureVerifier(signed: null));
        var hash = new HashService(new FakeClock()).ComputeStringHash(Payload, "SHA256");

        var artifact = await service.DownloadAsync(
            Request() with { ExpectedSha256 = hash },
            null,
            CancellationToken.None);

        Assert.True(artifact.HashMatchesExpectation);
        Assert.False(artifact.IsReadyForApproval);
    }

    [Fact]
    public async Task A_matching_hash_and_a_verified_signature_release_the_artefact()
    {
        using var paths = new TempPathProvider();
        var service = CreateService(paths, new StubSignatureVerifier(signed: true));
        var hash = new HashService(new FakeClock()).ComputeStringHash(Payload, "SHA256");

        var artifact = await service.DownloadAsync(
            Request() with { ExpectedSha256 = hash },
            null,
            CancellationToken.None);

        Assert.True(artifact.IsReadyForApproval);
        Assert.True(File.Exists(artifact.LocalPath));
        Assert.Equal(Payload.Length, artifact.SizeBytes.Value);
    }

    [Fact]
    public async Task A_download_that_does_not_match_the_published_hash_is_not_released()
    {
        using var paths = new TempPathProvider();
        var service = CreateService(paths, new StubSignatureVerifier(signed: true));

        var artifact = await service.DownloadAsync(
            Request() with { ExpectedSha256 = new string('a', 64) },
            null,
            CancellationToken.None);

        Assert.False(artifact.HashMatchesExpectation);
        Assert.False(artifact.IsReadyForApproval);
        Assert.False(artifact.Compatible);
    }

    [Fact]
    public async Task A_redirect_is_refused_instead_of_being_followed_silently()
    {
        using var paths = new TempPathProvider();
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Found));
        var service = CreateService(paths, new StubSignatureVerifier(signed: true), handler);

        var artifact = await service.DownloadAsync(Request(), null, CancellationToken.None);

        Assert.False(artifact.IsReadyForApproval);
        Assert.False(File.Exists(artifact.LocalPath));
        Assert.Contains("download-redirect-refused", artifact.CheckLog);
    }

    private static DownloadRequest Request() => new()
    {
        Url = "https://www.gigabyte.com/files/bios.bin",
        TargetFileName = "bios.bin",
        CandidateId = "CAND-1",
        MaxSizeBytes = 1024 * 1024,
        Policy = new SourcePolicy { AllowedHosts = new[] { "gigabyte.com" } },
    };

    private static DownloadService CreateService(
        TempPathProvider paths,
        ISignatureVerifier signatures,
        HttpMessageHandler? handler = null)
    {
        handler ??= new StubHttpHandler(_ => StubHttpHandler.Ok(Payload));

        return new DownloadService(
            new StubHttpClientProvider(handler),
            new HashService(new FakeClock()),
            signatures,
            new AllowAllSourceVerifier(),
            paths,
            new FakeClock());
    }

    /// <summary>Answers every verification as reachable but keeps the URL rules of the real verifier.</summary>
    private sealed class AllowAllSourceVerifier : ISourceVerifier
    {
        private readonly SourceVerifier _inner = new(
            new StubHttpClientProvider(new StubHttpHandler(_ => StubHttpHandler.Ok())),
            new FakeClock());

        public bool IsAcceptableUrl(string url, out string? reason) => _inner.IsAcceptableUrl(url, out reason);

        public Task<SourceVerificationResult> VerifyAsync(string url, SourcePolicy policy, CancellationToken cancellationToken) =>
            Task.FromResult(new SourceVerificationResult
            {
                Url = url,
                Reachable = true,
                IsHttps = true,
                CertificateValid = true,
                Verification = VerificationLevel.SourceReachable,
                Trust = policy.RequiredTrust,
                HttpStatusCode = 200,
            });
    }

    private sealed class StubSignatureVerifier : ISignatureVerifier
    {
        private readonly bool? _signed;

        public StubSignatureVerifier(bool? signed) => _signed = signed;

        public SignatureResult VerifyFile(string path, CancellationToken cancellationToken = default) => new()
        {
            Path = path,
            IsSigned = _signed,
            IsTrusted = _signed == true,
            Signer = _signed == true ? TextInfo.Known("Test Signer", ValueOrigin.LocalFile(DateTimeOffset.UnixEpoch, path)) : TextInfo.Unknown(ValueOrigin.Unknown, "not checked"),
            Verification = _signed == true ? VerificationLevel.SignatureVerified : VerificationLevel.NotVerified,
        };
    }

}
