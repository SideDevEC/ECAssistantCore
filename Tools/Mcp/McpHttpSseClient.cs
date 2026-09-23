using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Tools.Mcp;

/// <summary>
/// MCP client over HTTP/SSE (remote server). Communicates via JSON-RPC 2.0
/// over HTTP POST. One instance per server. Thread-safe after InitializeAsync.
/// </summary>
public sealed class McpHttpSseClient : IMcpClient
{
    private readonly McpServerConfig _config;
    private readonly ILogger _logger;
    private readonly string _serverName;
    private readonly Dictionary<string, string> _resolvedHeaders;
    private readonly HttpClient _httpClient;
    private int _nextRequestId = 1;
    private bool _disposed;

    public string ServerName => _serverName;

    /// <summary>
    /// Create an HTTP/SSE MCP client.
    /// </summary>
    /// <param name="serverName">Config key name for this server.</param>
    /// <param name="config">Server entry from MCP config.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="resolvedHeaders">Headers with keychain templates already resolved.</param>
    public McpHttpSseClient(
        string serverName,
        McpServerConfig config,
        ILogger logger,
        Dictionary<string, string>? resolvedHeaders = null)
    {
        _serverName = serverName;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolvedHeaders = resolvedHeaders ?? config.Headers;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds)
        };
    }

    public async Task<McpServerInfo> InitializeAsync(CancellationToken ct = default)
    {
        EnsureUrl();

        var initParams = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "ECAssistant", version = "1.0" }
        };

        var response = await SendRequestAsync("initialize", initParams, ct);
        var serverInfo = response.Deserialize<McpServerInfoJson>()
            ?? throw new InvalidOperationException($"MCP server '{_serverName}' returned empty serverInfo.");

        // Send initialized notification
        await SendNotificationAsync("notifications/initialized", new { }, ct);

        _logger.Info("McpHttpSseClient", $"Initialized '{_serverName}' — {serverInfo.Name} v{serverInfo.Version}");
        return new McpServerInfo(serverInfo.Name, serverInfo.Version);
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken ct = default)
    {
        EnsureUrl();
        var response = await SendRequestAsync("tools/list", new { }, ct);

        var toolsJson = response.GetProperty("tools");
        var tools = new List<McpToolDescriptor>();
        foreach (var tool in toolsJson.EnumerateArray())
        {
            var name = tool.GetProperty("name").GetString() ?? "";
            var description = tool.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
            var schema = tool.TryGetProperty("inputSchema", out var s) ? s.GetRawText() : "{}";
            tools.Add(new McpToolDescriptor(name, description, schema));
        }

        _logger.Info("McpHttpSseClient", $"'{_serverName}' exposes {tools.Count} tool(s)");
        return tools;
    }

    public async Task<McpToolResult> CallToolAsync(string name, string jsonArguments, CancellationToken ct = default)
    {
        EnsureUrl();

        var args = JsonSerializer.Deserialize<JsonElement>(jsonArguments);
        var parameters = new { name, arguments = args };
        var response = await SendRequestAsync("tools/call", parameters, ct);

        var isError = response.TryGetProperty("isError", out var err) && err.GetBoolean();
        var contentItems = new List<McpContentItem>();

        if (response.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                var type = item.TryGetProperty("type", out var t) ? t.GetString() ?? "text" : "text";
                var textVal = item.TryGetProperty("text", out var tx) ? tx.GetString() : null;
                var dataVal = item.TryGetProperty("data", out var d) ? d.GetString() : null;
                var mimeVal = item.TryGetProperty("mimeType", out var m) ? m.GetString() : null;
                contentItems.Add(new McpContentItem(type, textVal, dataVal, mimeVal));
            }
        }

        return new McpToolResult(isError, contentItems);
    }

    // ── JSON-RPC over HTTP ───────────────────────────

    private async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken ct)
    {
        EnsureUrl();
        var id = Interlocked.Increment(ref _nextRequestId);

        var message = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(message);
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.Url!);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        foreach (var (key, value) in _resolvedHeaders)
            request.Headers.TryAddWithoutValidation(key, value);

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonSerializer.Deserialize<JsonElement>(body);

        if (doc.TryGetProperty("error", out var error))
        {
            var msg = error.TryGetProperty("message", out var m) ? m.GetString() ?? "Unknown error" : "Unknown error";
            throw new InvalidOperationException($"MCP error from '{_serverName}': {msg}");
        }

        if (doc.TryGetProperty("result", out var result))
            return result;

        throw new InvalidOperationException($"MCP response from '{_serverName}' has no result or error.");
    }

    private async Task SendNotificationAsync(string method, object parameters, CancellationToken ct)
    {
        EnsureUrl();

        var message = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(message);
        using var request = new HttpRequestMessage(HttpMethod.Post, _config.Url!);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        foreach (var (key, value) in _resolvedHeaders)
            request.Headers.TryAddWithoutValidation(key, value);

        // Notifications don't require a response — fire and forget
        await _httpClient.SendAsync(request, ct);
    }

    private void EnsureUrl()
    {
        if (string.IsNullOrWhiteSpace(_config.Url))
            throw new InvalidOperationException($"MCP server '{_serverName}' has no URL configured.");
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class McpServerInfoJson
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";
    }
}