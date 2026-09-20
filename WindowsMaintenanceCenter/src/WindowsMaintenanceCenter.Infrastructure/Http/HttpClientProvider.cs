using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using WindowsMaintenanceCenter.Core.Abstractions;

namespace WindowsMaintenanceCenter.Infrastructure.Http;

/// <summary>
/// Provides HTTP clients with explicit timeouts and no cookie handling. Redirects are not
/// followed automatically: every hop is validated against the source policy by the caller,
/// so a redirect can never leave the verified host unnoticed (spec sections 30 and 63).
/// </summary>
public sealed class HttpClientProvider : IHttpClientProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _productVersion;

    public HttpClientProvider(string productVersion)
    {
        _productVersion = productVersion;
    }

    public HttpClient GetClient(string name, TimeSpan timeout)
    {
        var key = $"{name}:{timeout.TotalSeconds}";
        return _clients.GetOrAdd(key, _ => Create(timeout));
    }

    private HttpClient Create(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = timeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 4,
            UseCookies = false,
            UseProxy = true,
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = timeout,
        };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WindowsMaintenanceCenter", _productVersion));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return client;
    }

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }

        _clients.Clear();
    }
}
