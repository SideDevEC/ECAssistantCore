using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Session;

namespace ECAssistant.Core.Tools.Mcp;

/// <summary>
/// Manages MCP server lifecycle: create clients, initialize, discover tools,
/// filter per config, register as McpToolAdapter instances on the session.
/// DisposeAsync shuts down all spawned subprocesses and HTTP connections.
/// </summary>
public sealed class McpServerRegistrar : IAsyncDisposable
{
    private readonly List<IMcpClient> _clients = new();
    private readonly List<McpToolAdapter> _registeredAdapters = new();
    private bool _disposed;

    /// <summary>
    /// Connect to all configured MCP servers and register their tools on the session.
    /// Servers that fail to initialize are logged and skipped — one broken server
    /// doesn't block the rest.
    /// </summary>
    /// <param name="session">Target session to register tools on.</param>
    /// <param name="mcpConfig">MCP config section from AppConfig.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="keyStore">Optional secure key store for {{keychain:name}} template resolution.</param>
    public async Task RegisterAsync(
        AgentSession session,
        McpConfig mcpConfig,
        ILogger logger,
        ISecureKeyStore? keyStore = null,
        ECAssistant.Core.Tools.ToolPolicy? toolPolicy = null)
    {
        foreach (var (serverName, serverConfig) in mcpConfig.Servers)
        {
            if (string.IsNullOrWhiteSpace(serverName))
                continue;

            IMcpClient? client = null;
            try
            {
                client = CreateClient(serverName, serverConfig, logger, keyStore);
                await client.InitializeAsync();
                _clients.Add(client);

                var tools = await client.ListToolsAsync();
                var filtered = ApplyToolFilter(tools, serverConfig);

                foreach (var descriptor in filtered)
                {
                    var adapter = new McpToolAdapter(client, descriptor);
                    _registeredAdapters.Add(adapter);
                    session.RegisterTool(adapter);

                    // Apply server-level approval policy to ToolPolicy.
                    // Per-tool overrides in tool_permissions config already win
                    // because LoadFromConfig runs before MCP registration.
                    if (toolPolicy != null && serverConfig.ApprovalRequired == true)
                    {
                        // Only set if not already explicitly configured per-tool
                        if (!toolPolicy.HasExplicitPermission(descriptor.Name))
                        {
                            toolPolicy.SetPermission(
                                descriptor.Name,
                                approvalRequired: true,
                                $"MCP server '{serverName}' requires approval");
                        }
                    }
                }

                logger.Info("McpServerRegistrar",
                    $"Registered {filtered.Count} tool(s) from MCP server '{serverName}'.");
            }
            catch (Exception ex)
            {
                logger.Warn("McpServerRegistrar",
                    $"Failed to initialize MCP server '{serverName}': {ex.Message}");
                // Clean up the failed client
                if (client != null)
                {
                    try { await client.DisposeAsync(); } catch { }
                }
            }
        }
    }

    private IMcpClient CreateClient(
        string serverName,
        McpServerConfig config,
        ILogger logger,
        ISecureKeyStore? keyStore)
    {
        if (config.IsStdio)
        {
            var resolvedEnv = ResolveTemplates(config.Env, keyStore);
            return new McpStdioClient(serverName, config, logger, resolvedEnv);
        }

        if (config.IsHttp)
        {
            var resolvedHeaders = ResolveTemplates(config.Headers, keyStore);
            return new McpHttpSseClient(serverName, config, logger, resolvedHeaders);
        }

        throw new InvalidOperationException(
            $"MCP server '{serverName}' has neither command nor url configured.");
    }

    /// <summary>
    /// Filter tools per config: whitelist (tools list) or blacklist (exclude=true).
    /// Empty tools list = register all.
    /// </summary>
    private static IReadOnlyList<McpToolDescriptor> ApplyToolFilter(
        IReadOnlyList<McpToolDescriptor> tools,
        McpServerConfig config)
    {
        if (config.Tools.Count == 0)
            return tools;

        if (config.Exclude)
        {
            // Blacklist: register everything EXCEPT listed tools
            return tools
                .Where(t => !config.Tools.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        // Whitelist: register only listed tools
        return tools
            .Where(t => config.Tools.Contains(t.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Resolve {{keychain:name}} templates in string values using the secure key store.
    /// Values without the template prefix pass through unchanged.
    /// </summary>
    private static Dictionary<string, string> ResolveTemplates(
        Dictionary<string, string> source,
        ISecureKeyStore? keyStore)
    {
        if (keyStore == null)
            return source;

        const string prefix = "{{keychain:";
        const string suffix = "}}";

        var result = new Dictionary<string, string>(source.Count);
        foreach (var (key, value) in source)
        {
            if (value.StartsWith(prefix) && value.EndsWith(suffix))
            {
                var keyName = value[prefix.Length..^suffix.Length].Trim();
                result[key] = keyStore.GetKey(keyName) ?? value;
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var client in _clients)
        {
            try { await client.DisposeAsync(); }
            catch { }
        }
        _clients.Clear();
        _registeredAdapters.Clear();
    }
}