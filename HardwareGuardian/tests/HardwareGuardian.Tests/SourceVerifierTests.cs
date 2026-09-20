using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Infrastructure.Security;
using Xunit;

namespace HardwareGuardian.Tests;

/// <summary>
/// Source verification (spec sections 12, 30 and 48). The network is replaced by a handler that
/// returns prepared responses, so the test checks the real decision logic of
/// <see cref="SourceVerifier"/> and not a copy of it.
/// </summary>
public sealed class SourceVerifierTests
{
    [Fact]
    public async Task A_redirect_that_leaves_the_allow_list_is_blocked()
    {
        // The official page redirects to a third party. The first host is allowed, the target is
        // not, so the source must be BLOCKED instead of "verified".
        var handler = new StubHttpHandler(request => request.RequestUri!.Host switch
        {
            "www.gigabyte.com" => StubHttpHandler.Redirect("https://driver-portal.example.com/download"),
            _ => StubHttpHandler.Ok(),
        });

        var verifier = new SourceVerifier(new StubHttpClientProvider(handler), new FakeClock());

        var result = await verifier.VerifyAsync(
            "https://www.gigabyte.com/Support",
            Policy("www.gigabyte.com"),
            CancellationToken.None);

        Assert.False(result.Reachable);
        Assert.Equal(BlockReasons.ManufacturerSourceNotVerifiable, result.ErrorCode);
        Assert.Contains(result.Checks, check => check.Contains("driver-portal.example.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_redirect_inside_the_allow_list_is_followed()
    {
        var handler = new StubHttpHandler(request => request.RequestUri!.Host switch
        {
            "www.gigabyte.com" => StubHttpHandler.Redirect("https://download.gigabyte.com/support"),
            _ => StubHttpHandler.Ok(),
        });

        var verifier = new SourceVerifier(new StubHttpClientProvider(handler), new FakeClock());

        var result = await verifier.VerifyAsync(
            "https://www.gigabyte.com/Support",
            Policy("gigabyte.com"),
            CancellationToken.None);

        Assert.True(result.Reachable);
        Assert.Equal(VerificationLevel.SourceReachable, result.Verification);
        Assert.Equal("https://download.gigabyte.com/support", result.RedirectTarget);
    }

    [Fact]
    public async Task A_host_that_is_not_in_the_allow_list_is_blocked_before_any_request()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Ok());
        var verifier = new SourceVerifier(new StubHttpClientProvider(handler), new FakeClock());

        var result = await verifier.VerifyAsync(
            "https://driver-portal.example.com/download",
            Policy("www.gigabyte.com"),
            CancellationToken.None);

        Assert.False(result.Reachable);
        Assert.Equal(BlockReasons.ManufacturerSourceNotVerifiable, result.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task An_empty_allow_list_accepts_a_structurally_valid_host()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Ok());
        var verifier = new SourceVerifier(new StubHttpClientProvider(handler), new FakeClock());

        var result = await verifier.VerifyAsync("https://www.amd.com/support", new SourcePolicy(), CancellationToken.None);

        Assert.True(result.Reachable);
    }

    [Theory]
    [InlineData("http://www.gigabyte.com/Support")]
    [InlineData("https://user:secret@www.gigabyte.com/Support")]
    [InlineData("https://localhost/support")]
    [InlineData("https://127.0.0.1/support")]
    [InlineData("not a url")]
    public void Only_https_without_credentials_and_without_local_addresses_is_acceptable(string url)
    {
        var verifier = new SourceVerifier(new StubHttpClientProvider(new StubHttpHandler(_ => StubHttpHandler.Ok())), new FakeClock());

        Assert.False(verifier.IsAcceptableUrl(url, out var reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    private static SourcePolicy Policy(params string[] hosts) => new()
    {
        AllowedHosts = hosts,
        RequiredTrust = SourceTrust.Manufacturer,
        Timeout = TimeSpan.FromSeconds(5),
    };
}
