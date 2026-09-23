using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// MCP server configuration section in appsettings.json.
/// Supports two transport types: stdio (local subprocess) and HTTP/SSE (remote).
/// </summary>
public class McpConfig
{
    [JsonPropertyName("servers")]
    public Dictionary<string, McpServerConfig> Servers { get; set; } = new();
}

/// <summary>
/// One MCP server entry. Either <see cref="Command"/> (stdio) or <see cref="Url"/> (HTTP/SSE).
/// </summary>
public class McpServerConfig
{
    /// <summary>Stdio transport: command to execute (e.g. "npx", "python3", "/path/to/binary").</summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>Stdio transport: arguments passed to the command.</summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = new();

    /// <summary>Stdio transport: environment variables for the subprocess.
    /// Values support <c>{{keychain:name}}</c> template resolution via SecureKeyStore.</summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = new();

    /// <summary>HTTP/SSE transport: server URL (e.g. "https://mcp.example.com/sse").</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>HTTP/SSE transport: headers sent with each request.
    /// Values support <c>{{keychain:name}}</c> template resolution via SecureKeyStore.</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>Tool whitelist: only register these tool names. Empty = register all.</summary>
    [JsonPropertyName("tools")]
    public List<string> Tools { get; set; } = new();

    /// <summary>If true, <see cref="Tools"/> is treated as an exclusion list (register all EXCEPT these).</summary>
    [JsonPropertyName("exclude")]
    public bool Exclude { get; set; }

    /// <summary>Startup timeout in seconds (default 30). Applies to stdio initialize handshake.</summary>
    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Default approval policy for ALL tools from this server.
    /// false (default) = allowed (read-only assumption).
    /// true = every tool from this server requires user approval.
    /// Per-tool overrides in tool_permissions config win over this.
    /// </summary>
    [JsonPropertyName("approval_required")]
    public bool? ApprovalRequired { get; set; }

    /// <summary>True when this entry uses stdio transport (Command is set).</summary>
    [JsonIgnore]
    public bool IsStdio => !string.IsNullOrWhiteSpace(Command);

    /// <summary>True when this entry uses HTTP/SSE transport (Url is set).</summary>
    [JsonIgnore]
    public bool IsHttp => !string.IsNullOrWhiteSpace(Url);
}