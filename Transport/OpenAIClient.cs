using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace ECAssistant.Core.Transport;

/// <summary>
/// HttpClient wrapper for OpenAI-compatible API calls.
/// Handles JSON serialization, headers (X-Client-Id for local, Bearer for remote), and SSE streaming.
/// </summary>
public sealed class OpenAIClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string? _clientId;
    private readonly string? _apiKey;
    private bool _disposed;

    /// <summary>Base URL of the server (e.g. http://localhost:8420 or https://api.openai.com).</summary>
    public string BaseUrl => _baseUrl;

    /// <summary>
    /// Create with endpoint, optional client ID (local ECAssistantLLM), and optional API key (remote).
    /// When apiKey is set, requests include "Authorization: Bearer {apiKey}".
    /// When clientId is set, requests include "X-Client-Id: {clientId}".
    /// </summary>
    public OpenAIClient(string baseUrl, string? clientId = null, string? apiKey = null, TimeSpan? timeout = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _clientId = clientId;
        _apiKey = apiKey;
        _http = new HttpClient { Timeout = timeout ?? TimeSpan.FromMinutes(10) };
    }

    /// <summary>
    /// Send a POST request and return the raw JSON response body.
    /// </summary>
    public async Task<string> PostJsonAsync(string path, string jsonBody, CancellationToken ct = default)
    {
        using var req = CreateRequest(HttpMethod.Post, path, jsonBody);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Send a GET request and return the raw JSON response body.
    /// </summary>
    public async Task<string> GetJsonAsync(string path, CancellationToken ct = default)
    {
        using var req = CreateRequest(HttpMethod.Get, path);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Send a DELETE request.
    /// </summary>
    public async Task<string> DeleteJsonAsync(string path, CancellationToken ct = default)
    {
        using var req = CreateRequest(HttpMethod.Delete, path);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    /// <summary>
    /// Send a POST and get the response stream (for SSE streaming).
    /// Returns the HttpResponseMessage — caller must read the stream.
    /// </summary>
    public async Task<HttpResponseMessage> PostStreamAsync(string path, string jsonBody, CancellationToken ct = default)
    {
        // NOTE: no `using` here — disposing the HttpRequestMessage while the response
        // stream is still being consumed can abort streaming. Request disposal is
        // unnecessary (GC handles it; HttpClient does not require it).
        var req = CreateRequest(HttpMethod.Post, path, jsonBody);
        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return resp;
    }

    /// <summary>
    /// Ping the server health endpoint. Only works with ECAssistantLLM (local mode).
    /// Returns false for remote APIs that don't have /eca/health.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var json = await GetJsonAsync("/eca/health", cts.Token);
            return json.Contains("\"status\":\"ok\"") || json.Contains("\"status\": \"ok\"");
        }
        catch
        {
            return false;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string? jsonBody = null)
    {
        var url = $"{_baseUrl}{path}";
        var req = new HttpRequestMessage(method, url);

        // Remote mode: Bearer token auth
        if (_apiKey != null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        // Local mode: client identification
        if (_clientId != null)
            req.Headers.Add("X-Client-Id", _clientId);

        if (jsonBody != null)
        {
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return req;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}