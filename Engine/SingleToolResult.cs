using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Engine;

/// <summary>Result of a single tool execution within a batch.</summary>
public class SingleToolResult
{
    public ToolCallRequest ToolCall { get; set; } = null!;   // Must be set by caller
    public bool Succeeded { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public long ElapsedMs { get; set; }
}
