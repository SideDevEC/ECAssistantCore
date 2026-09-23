using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Tools.Mcp;

/// <summary>
/// MCP client over stdio (local subprocess). Spawns the server process,
/// communicates via JSON-RPC 2.0 over stdin/stdout pipes.
/// One instance per server. Thread-safe after InitializeAsync.
/// </summary>
public sealed class McpStdioClient : IMcpClient
{
    private readonly McpServerConfig _config;
    private readonly ILogger _logger;
    private readonly string _serverName;
    private readonly Dictionary<string, string> _resolvedEnv;
    private Process? _process;
    private int _nextRequestId = 1;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _processCts = new();
    private bool _disposed;

    public string ServerName => _serverName;

    /// <summary>
    /// Create a stdio MCP client.
    /// </summary>
    /// <param name="serverName">Config key name for this server.</param>
    /// <param name="config">Server entry from MCP config.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="resolvedEnv">Environment variables with keychain templates already resolved.</param>
    public McpStdioClient(
        string serverName,
        McpServerConfig config,
        ILogger logger,
        Dictionary<string, string>? resolvedEnv = null)
    {
        _serverName = serverName;
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolvedEnv = resolvedEnv ?? config.Env;
    }

    public async Task<McpServerInfo> InitializeAsync(CancellationToken ct = default)
    {
        if (_process != null)
            throw new InvalidOperationException("Already initialized.");

        EnsureCommand();

        var startInfo = new ProcessStartInfo
        {
            FileName = _config.Command!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in _config.Args)
            startInfo.ArgumentList.Add(arg);

        foreach (var (key, value) in _resolvedEnv)
            startInfo.Environment[key] = value;

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start MCP server '{_serverName}': {_config.Command}");

        // Start reading stdout in background — routes JSON-RPC responses to pending requests
        _ = Task.Run(() => ReadStdoutLoop(_processCts.Token), _processCts.Token);

        // Start reading stderr for diagnostics
        _ = Task.Run(() => ReadStderrLoop(_processCts.Token), _processCts.Token);

        // Send initialize request
        var initParams = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "ECAssistant", version = "1.0" }
        };

        var response = await SendRequestAsync("initialize", initParams, ct);
        var serverInfo = response.Deserialize<McpServerInfoJson>()
            ?? throw new InvalidOperationException($"MCP server '{_serverName}' returned empty serverInfo.");

        // Send initialized notification (no response expected)
        await SendNotificationAsync("notifications/initialized", new { }, ct);

        _logger.Info("McpStdioClient", $"Initialized '{_serverName}' — {serverInfo.Name} v{serverInfo.Version}");
        return new McpServerInfo(serverInfo.Name, serverInfo.Version);
    }

    public async Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken ct = default)
    {
        EnsureInitialized();
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

        _logger.Info("McpStdioClient", $"'{_serverName}' exposes {tools.Count} tool(s)");
        return tools;
    }

    public async Task<McpToolResult> CallToolAsync(string name, string jsonArguments, CancellationToken ct = default)
    {
        EnsureInitialized();

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
                var text = item.TryGetProperty("text", out var tx) ? tx.GetString() : null;
                var data = item.TryGetProperty("data", out var d) ? d.GetString() : null;
                var mimeType = item.TryGetProperty("mimeType", out var m) ? m.GetString() : null;
                contentItems.Add(new McpContentItem(type, text, data, mimeType));
            }
        }

        return new McpToolResult(isError, contentItems);
    }

    // ── JSON-RPC transport ───────────────────────────

    private async Task<JsonElement> SendRequestAsync(string method, object parameters, CancellationToken ct)
    {
        EnsureInitialized();
        var id = Interlocked.Increment(ref _nextRequestId);

        var message = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(message);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_pending)
            _pending[id] = tcs;

        await _sendLock.WaitAsync(ct);
        try
        {
            await _process!.StandardInput.WriteLineAsync(json);
            await _process.StandardInput.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }

        // Timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_config.TimeoutSeconds));

        try
        {
            using var reg = timeoutCts.Token.Register(() =>
            {
                lock (_pending)
                    _pending.Remove(id);
                tcs.TrySetException(new TimeoutException(
                    $"MCP request '{method}' to '{_serverName}' timed out after {_config.TimeoutSeconds}s."));
            });

            return await tcs.Task;
        }
        catch
        {
            lock (_pending)
                _pending.Remove(id);
            throw;
        }
    }

    private async Task SendNotificationAsync(string method, object parameters, CancellationToken ct)
    {
        EnsureInitialized();
        var message = new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters
        };

        var json = JsonSerializer.Serialize(message);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _process!.StandardInput.WriteLineAsync(json);
            await _process.StandardInput.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReadStdoutLoop(CancellationToken ct)
    {
        var reader = _process!.StandardOutput;
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line == null)
                break; // Process exited

            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonElement doc;
            try
            {
                doc = JsonSerializer.Deserialize<JsonElement>(line);
            }
            catch (JsonException ex)
            {
                _logger.Warn("McpStdioClient", $"'{_serverName}' sent invalid JSON: {ex.Message}");
                continue;
            }

            // Route response to pending request
            if (doc.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
            {
                var id = idProp.GetInt32();
                TaskCompletionSource<JsonElement>? tcs;
                lock (_pending)
                    _pending.TryGetValue(id, out tcs);

                if (tcs != null)
                {
                    if (doc.TryGetProperty("error", out var error))
                    {
                        var msg = error.TryGetProperty("message", out var m) ? m.GetString() ?? "Unknown error" : "Unknown error";
                        tcs.TrySetException(new InvalidOperationException($"MCP error from '{_serverName}': {msg}"));
                    }
                    else if (doc.TryGetProperty("result", out var result))
                    {
                        tcs.TrySetResult(result);
                    }
                    else
                    {
                        tcs.TrySetException(new InvalidOperationException($"MCP response from '{_serverName}' has no result or error."));
                    }

                    lock (_pending)
                        _pending.Remove(id);
                }
            }
            // Notifications from server (no id) — we don't handle any currently
        }
    }

    private async Task ReadStderrLoop(CancellationToken ct)
    {
        var reader = _process!.StandardError;
        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line == null)
                break;

            _logger.Debug("McpStdioClient", $"[{_serverName}] stderr: {line}");
        }
    }

    private void EnsureCommand()
    {
        if (string.IsNullOrWhiteSpace(_config.Command))
            throw new InvalidOperationException($"MCP server '{_serverName}' has no command configured.");
    }

    private void EnsureInitialized()
    {
        if (_process == null)
            throw new InvalidOperationException($"MCP client '{_serverName}' is not initialized. Call InitializeAsync first.");
        if (_process.HasExited)
            throw new InvalidOperationException($"MCP server '{_serverName}' process has exited (code {_process.ExitCode}).");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _processCts.Cancel();

        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.StandardInput.Close();
                _process.WaitForExit(5000);
                if (!_process.HasExited)
                    _process.Kill();
            }
            catch { }
        }

        _process?.Dispose();
        _processCts.Dispose();
        _sendLock.Dispose();

        await ValueTask.CompletedTask;
    }

    // ── Internal JSON model for initialize response ──

    private sealed class McpServerInfoJson
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";
    }
}