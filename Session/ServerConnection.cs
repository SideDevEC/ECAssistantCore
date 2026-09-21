using System.Text.Json;

namespace ECAssistant.Core.Session;

/// <summary>Capabilities an LLM backend advertises at connect time. v13c.</summary>
[Flags]
public enum ServerCapabilities
{
    None = 0,
    /// <summary>ECAssistant private protocol: client registration, heartbeat, KV sessions, shutdown.</summary>
    EcaExtensions = 1,
    /// <summary>Server-side grammar-constrained decision decoding (`structured: true`).</summary>
    StructuredDecoding = 2,
    /// <summary>At least one loaded model supports vision.</summary>
    Vision = 4,
    /// <summary>OpenAI-native function calling (`tools` / `tool_calls`). Remote providers + modern local servers.</summary>
    NativeTools = 8,
}

/// <summary>
/// v13c: ONE connection model for every LLM backend. Local ECAssistantLLM and remote
/// OpenAI-compatible providers are the same thing — an endpoint with a capability set
/// discovered by probing at connect. Local = a remote server with superpowers.
/// Stateless — instances are per-connection state only.
/// </summary>
public sealed class ServerConnection
{
    // Probe client — independent from the session client (different auth/headers).
    private static readonly HttpClient ProbeClient = CreateProbeClient();

    private static HttpClient CreateProbeClient()
    {
        var c = new HttpClient();
        // Remote providers can be slow on cold model-list responses (OpenRouter
        // observed >10s) — a 5s probe falsely reports "endpoint unreachable".
        c.Timeout = TimeSpan.FromSeconds(15);
        return c;
    }

    public string Endpoint { get; private set; } = "";
    public ServerCapabilities Capabilities { get; private set; } = ServerCapabilities.None;

    public bool Has(ServerCapabilities cap) => Capabilities.HasFlag(cap);
    public bool HasEcaExtensions => Has(ServerCapabilities.EcaExtensions);

    /// <summary>
    /// Discover what the endpoint is and what it can do:
    /// 1. ECAssistant health probe → ECA extensions + advertised capabilities.
    /// 2. OpenAI models probe → plain OpenAI-compatible server (native tools assumed).
    /// Throws when the endpoint answers neither.
    /// </summary>
    public async Task<ServerConnection> ConnectAsync(string endpoint, CancellationToken ct = default)
    {
        // Normalize so users can enter endpoints with or without a trailing "/v1":
        // all subsequent probes and clients append versioned paths themselves.
        Endpoint = Transport.EndpointNormalizer.NormalizeBaseUrl(endpoint);
        Capabilities = ServerCapabilities.None;

        // 1. ECAssistant server?
        var (ok, body) = await TryGetAsync(ProbeClient, $"{Endpoint}/eca/health", ct);
        if (ok)
        {
            Capabilities |= ServerCapabilities.EcaExtensions;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var cap in caps.EnumerateArray())
                    {
                        var name = cap.GetString();
                        if (name == "structured-decoding") Capabilities |= ServerCapabilities.StructuredDecoding;
                        else if (name == "vision") Capabilities |= ServerCapabilities.Vision;
                    }
                }
            }
            catch (JsonException) { /* health body malformed — extensions still on */ }
            return this;
        }

        // 2. Plain OpenAI-compatible server?
        (ok, _) = await TryGetAsync(ProbeClient, $"{Endpoint}/v1/models", ct);
        if (ok)
        {
            Capabilities |= ServerCapabilities.NativeTools;
            return this;
        }

        throw new InvalidOperationException($"LLM endpoint unreachable: {Endpoint}");
    }

    private static async Task<(bool ok, string body)> TryGetAsync(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));
            var response = await client.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode) return (false, "");
            return (true, await response.Content.ReadAsStringAsync(cts.Token));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return (false, "");
        }
    }
}
