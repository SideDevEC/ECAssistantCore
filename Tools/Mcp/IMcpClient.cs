using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ECAssistant.Core.Tools.Mcp;

/// <summary>
/// Client for communicating with an MCP (Model Context Protocol) server.
/// Two implementations: stdio (local subprocess) and HTTP/SSE (remote).
/// Lifecycle: InitializeAsync → ListToolsAsync → CallToolAsync(s) → DisposeAsync.
/// </summary>
public interface IMcpClient : IAsyncDisposable
{
    /// <summary>Server name from config (for logging and tool source identification).</summary>
    string ServerName { get; }

    /// <summary>Perform the MCP initialize handshake. Returns server capabilities info.</summary>
    Task<McpServerInfo> InitializeAsync(CancellationToken ct = default);

    /// <summary>Discover tools exposed by the server.</summary>
    Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken ct = default);

    /// <summary>Invoke a tool by name with JSON arguments. Returns content array.</summary>
    Task<McpToolResult> CallToolAsync(string name, string jsonArguments, CancellationToken ct = default);
}

/// <summary>Server identity returned by the initialize handshake.</summary>
public sealed record McpServerInfo(string Name, string Version);

/// <summary>Tool descriptor from tools/list — name, description, JSON Schema.</summary>
public sealed record McpToolDescriptor(string Name, string Description, string InputSchema);

/// <summary>One content item in a tool result.</summary>
public sealed record McpContentItem(string Type, string? Text, string? Data, string? MimeType);

/// <summary>Result of a tools/call invocation.</summary>
public sealed record McpToolResult(bool IsError, IReadOnlyList<McpContentItem> Content);