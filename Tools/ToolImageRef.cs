namespace ECAssistant.Core.Tools;

/// <summary>
/// Image reference returned by tools (e.g. MCP image content, vision tools).
/// Carries base64 image data + MIME type + source identifier.
/// Fed into the vision pipeline via AgentEngine.AddToolResult.
/// </summary>
public sealed record ToolImageRef(string Base64Data, string MimeType, string Source)
{
    /// <summary>Convert to a data URI suitable for HTTP multipart or inline display.</summary>
    public string ToDataUri() => $"data:{MimeType};base64,{Base64Data}";
}