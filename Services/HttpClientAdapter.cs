using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// Concrete HTTP client implementation with browser-like default headers
/// to avoid being blocked by bot protection (Cloudflare, etc.).
/// </summary>
public class HttpClientAdapter : IHttpClient, IDisposable
{
    private readonly HttpClient _client;

    /// <summary>
    /// Default browser-like headers sent on all GET requests unless overridden.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultHeaders = new()
    {
        ["User-Agent"] = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
        ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
        ["Accept-Language"] = "en-US,en;q=0.9"
    };

    public HttpClientAdapter()
    {
        _client = new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <inheritdoc />
    public async Task<string> GetAsync(string url, CancellationToken ct = default)
        => await GetAsync(url, null, ct);

    /// <inheritdoc />
    public async Task<string> GetAsync(string url, Dictionary<string, string>? headers, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        // Apply default browser headers
        foreach (var (key, value) in DefaultHeaders)
        {
            if (!request.Headers.Contains(key))
                request.Headers.TryAddWithoutValidation(key, value);
        }

        // Apply caller-specified headers (override defaults)
        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                request.Headers.Remove(key);
                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        var response = await _client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <inheritdoc />
    public async Task<string> PostAsync(string url, string content, CancellationToken ct = default)
    {
        var response = await _client.PostAsync(url, new StringContent(content), ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}