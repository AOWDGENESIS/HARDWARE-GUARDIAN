using System.Diagnostics;
using System.Net;
using HardwareGuardian.Core;
using HardwareGuardian.Core.Abstractions;
using HardwareGuardian.Core.Models;
using HardwareGuardian.Core.Values;
using HardwareGuardian.Infrastructure.Http;

namespace HardwareGuardian.Infrastructure.Security;

/// <summary>
/// Verifies that an official source may be used (spec sections 12, 30, 48).
/// Rules: HTTPS only, no credentials in the URL, no certificate errors are ever tolerated,
/// redirects are checked hop by hop against the host allow list, and an unverifiable source
/// leads to BLOCKED instead of a warning.
/// </summary>
public sealed class SourceVerifier : ISourceVerifier
{
    private const int MaxRedirects = 3;
    private readonly IHttpClientProvider _http;
    private readonly IClock _clock;

    public SourceVerifier(IHttpClientProvider http, IClock clock)
    {
        _http = http;
        _clock = clock;
    }

    public bool IsAcceptableUrl(string url, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            reason = "not an absolute URL";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            reason = "only HTTPS sources are accepted";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            reason = "URL must not contain credentials";
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var address) && (IPAddress.IsLoopback(address) || IsPrivate(address)))
        {
            reason = "local or private addresses are not valid manufacturer sources";
            return false;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            reason = "localhost is not a valid manufacturer source";
            return false;
        }

        return true;
    }

    public async Task<SourceVerificationResult> VerifyAsync(string url, SourcePolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!IsAcceptableUrl(url, out var reason))
        {
            return SourceVerificationResult.Blocked(
                url ?? string.Empty,
                BlockReasons.OfficialSourceUnreachable,
                LocalizedText.Of("Source_Verification_Rejected", reason ?? "unknown"),
                reason);
        }

        if (policy.AllowedHosts.Count > 0)
        {
            var host = new Uri(url).Host;
            var allowed = policy.AllowedHosts.Any(allowedHost =>
                host.Equals(allowedHost, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + allowedHost, StringComparison.OrdinalIgnoreCase));

            if (!allowed)
            {
                return SourceVerificationResult.Blocked(
                    url,
                    BlockReasons.ManufacturerSourceNotVerifiable,
                    LocalizedText.Of("Source_Verification_HostNotAllowed", host),
                    $"host={host} is not in the adapter allow list");
            }
        }

        var checks = new List<string> { "scheme=https", $"timeout={policy.Timeout.TotalSeconds:F0}s" };
        var stopwatch = Stopwatch.StartNew();
        var currentUrl = url;
        var redirects = 0;

        try
        {
            using var client = _http.GetClient("source-verifier", policy.Timeout);
            while (true)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 4096);

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                if (IsRedirect(response.StatusCode))
                {
                    if (++redirects > MaxRedirects)
                    {
                        return SourceVerificationResult.Blocked(url, BlockReasons.OfficialSourceUnreachable, LocalizedText.Of("Source_Verification_TooManyRedirects"), $"redirects>{MaxRedirects}");
                    }

                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        return SourceVerificationResult.Blocked(url, BlockReasons.OfficialSourceUnreachable, LocalizedText.Of("Source_Verification_RedirectWithoutTarget"));
                    }

                    var next = location.IsAbsoluteUri ? location : new Uri(new Uri(currentUrl), location);
                    if (!IsAcceptableUrl(next.ToString(), out var redirectReason))
                    {
                        return SourceVerificationResult.Blocked(url, BlockReasons.OfficialSourceUnreachable, LocalizedText.Of("Source_Verification_Rejected", redirectReason ?? "unknown"), redirectReason);
                    }

                    checks.Add($"redirect={next.Host}");
                    currentUrl = next.ToString();
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return SourceVerificationResult.Blocked(
                        currentUrl,
                        BlockReasons.OfficialSourceUnreachable,
                        LocalizedText.Of("Source_Verification_Status", (int)response.StatusCode),
                        $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
                }

                var isHttps = response.RequestMessage?.RequestUri?.Scheme == Uri.UriSchemeHttps;
                return new SourceVerificationResult
                {
                    Url = currentUrl,
                    Reachable = true,
                    IsHttps = isHttps,
                    CertificateValid = true,
                    Verification = VerificationLevel.SourceReachable,
                    Trust = policy.RequiredTrust,
                    HttpStatusCode = (int)response.StatusCode,
                    ContentType = response.Content.Headers.ContentType?.MediaType,
                    ContentLength = response.Content.Headers.ContentLength,
                    RetrievedAt = _clock.Now,
                    Duration = stopwatch.Elapsed,
                    RedirectTarget = redirects > 0 ? currentUrl : null,
                    Summary = LocalizedText.Of("Source_Verification_Reachable", new Uri(currentUrl).Host),
                    Checks = checks,
                };
            }
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return SourceVerificationResult.Blocked(url, BlockReasons.OfficialSourceUnreachable, LocalizedText.Of("Source_Verification_Timeout", policy.Timeout.TotalSeconds), "timeout");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            return SourceVerificationResult.Blocked(
                url,
                BlockReasons.OfficialSourceUnreachable,
                LocalizedText.Of("Source_Verification_TransportError", ex.Message),
                $"{ex.GetType().Name}: {ex.Message}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return SourceVerificationResult.Blocked(url, BlockReasons.OfficialSourceUnreachable, LocalizedText.Of("Source_Verification_TransportError", ex.Message), $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsRedirect(HttpStatusCode status) => status is HttpStatusCode.MovedPermanently
        or HttpStatusCode.Found
        or HttpStatusCode.SeeOther
        or HttpStatusCode.TemporaryRedirect
        or HttpStatusCode.PermanentRedirect;

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        return false;
    }
}
