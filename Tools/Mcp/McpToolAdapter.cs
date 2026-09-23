using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Session;

namespace ECAssistant.Core.Tools.Mcp;

/// <summary>
/// Adapter that wraps an MCP tool descriptor as an EToolBase subclass.
/// The orchestrator and engine see this as any other built-in tool.
/// Translates EToolBase calls → MCP JSON-RPC calls → EToolResult.
/// </summary>
public sealed class McpToolAdapter : EToolBase
{
    private readonly IMcpClient _client;
    private readonly McpToolDescriptor _descriptor;
    private readonly string _serverName;

    public override string Name => _descriptor.Name;
    public override string Description => _descriptor.Description;
    public override string UsageExample => ""; // MCP tools use JSON Schema, not text examples

    public McpToolAdapter(IMcpClient client, McpToolDescriptor descriptor)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _serverName = client.ServerName;
    }

    /// <summary>MCP tools have real JSON Schema from the server — pass it through.</summary>
    public override string GetParameterSchema() => _descriptor.InputSchema;

    public override object GetConfigSection() => new { enabled = true };

    /// <summary>
    /// Truncate verbose MCP output for model context. The full output
    /// is still stored by AgentEngine for retrieval.
    /// </summary>
    public override string RenderForModel(string rawOutput)
    {
        const int maxChars = 4000;
        if (rawOutput.Length > maxChars)
            return rawOutput[..maxChars] + $"\n... [output truncated, {rawOutput.Length} chars total]";
        return rawOutput;
    }

    public override async Task<EToolResult> ExecuteAsync(
        Dictionary<string, string?> arguments,
        CancellationToken cancellationToken = default)
    {
        // Convert string args to JSON object matching the tool's input schema
        var jsonArgs = BuildJsonArguments(arguments);

        McpToolResult result;
        try
        {
            result = await _client.CallToolAsync(_descriptor.Name, jsonArgs, cancellationToken);
        }
        catch (Exception ex)
        {
            return EToolResult.Failure(_descriptor.Name, $"MCP call failed: {ex.Message}");
        }

        // Extract text content
        var textParts = new List<string>();
        var images = new List<ToolImageRef>();

        foreach (var item in result.Content)
        {
            switch (item.Type)
            {
                case "text":
                    if (item.Text != null)
                        textParts.Add(item.Text);
                    break;

                case "image":
                    if (item.Data != null && item.MimeType != null)
                        images.Add(new ToolImageRef(
                            item.Data,
                            item.MimeType,
                            $"mcp:{_serverName}:{_descriptor.Name}"));
                    break;

                // "resource" type — extract text if available, skip binary resources
                default:
                    if (item.Text != null)
                        textParts.Add(item.Text);
                    break;
            }
        }

        var output = string.Join("\n", textParts);

        if (result.IsError)
            return EToolResult.Failure(_descriptor.Name, output);

        return images.Count > 0
            ? EToolResult.Success(_descriptor.Name, output, images)
            : EToolResult.Success(_descriptor.Name, output);
    }

    /// <summary>
    /// Convert the string-keyed argument dictionary to a JSON object string.
    /// If the tool's schema defines typed properties, attempt to parse values
    /// as the correct type (number, boolean, array). Falls back to strings.
    /// </summary>
    private string BuildJsonArguments(Dictionary<string, string?> arguments)
    {
        if (arguments.Count == 0)
            return "{}";

        // Try to parse schema for type-aware conversion
        JsonElement? schemaRoot = null;
        try
        {
            schemaRoot = JsonSerializer.Deserialize<JsonElement>(_descriptor.InputSchema);
        }
        catch { }

        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in arguments)
        {
            if (value == null)
            {
                result[key] = null;
                continue;
            }

            // Check schema for expected type
            var expectedType = GetSchemaPropertyType(schemaRoot, key);

            result[key] = expectedType switch
            {
                "number" or "integer" => TryParseNumber(value),
                "boolean" => bool.TryParse(value, out var b) ? b : value,
                "array" => TryParseArray(value),
                _ => value // string or unknown
            };
        }

        return JsonSerializer.Serialize(result);
    }

    private static string? GetSchemaPropertyType(JsonElement? schema, string propName)
    {
        if (schema == null) return null;
        if (!schema.Value.TryGetProperty("properties", out var props)) return null;
        if (!props.TryGetProperty(propName, out var prop)) return null;
        if (prop.TryGetProperty("type", out var type))
            return type.GetString();
        return null;
    }

    private static object TryParseNumber(string value)
    {
        if (int.TryParse(value, out var i)) return i;
        if (double.TryParse(value, out var d)) return d;
        return value;
    }

    private static object TryParseArray(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(value);
        }
        catch
        {
            return value;
        }
    }
}