namespace ECAssistant.Core.Tools;

/// <summary>
/// Standardized tool call result that flows from any Tool back to the Agent.
/// This is the contract all tools must return through.
/// </summary>
public class EToolResult
{
    /// <summary>Tool name that produced this result</summary>
    public string ToolName { get; init; } = "";

    /// <summary>Success/failure status</summary>
    public bool Succeeded { get; init; }

    /// <summary>Human-readable output for the LLM / user</summary>
    public string Output { get; init; } = string.Empty;

    /// <summary>Error message (empty when Succeeded)</summary>
    public string Error { get; init; } = string.Empty;

    /// <summary>Additional metadata (null if not applicable)</summary>
    public Dictionary<string, string>? Metadata { get; init; }

    public EToolResult() { }

    /// <summary>Create a successful tool result</summary>
    // Stateless factory — immutable data class
    public static EToolResult Success(string toolName, string output, Dictionary<string, string>? metadata = null)
        => new() { ToolName = toolName, Succeeded = true, Output = output, Metadata = metadata };

    /// <summary>Create a failed tool result with error message</summary>
    // Stateless factory — immutable data class
    public static EToolResult Failure(string toolName, string error, Dictionary<string, string>? metadata = null)
        => new() { ToolName = toolName, Succeeded = false, Error = error, Metadata = metadata };

    /// <summary>String representation for LLM context</summary>
    public override string ToString()
        => Succeeded
           ? $"[SUCCESS] {ToolName}: {Output}"
           : $"[FAILED] {ToolName}: {Error}";
}