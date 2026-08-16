namespace ECAssistant.Core.Engine;

/// <summary>
/// A single planned tool call — concrete mapping from a sub-task to a tool + args.
/// </summary>
public class PlannedToolCall
{
     /// <summary>Tool name to call (e.g., "EShellAgent").</summary>
    public string ToolName { get; set; } = "";

     /// <summary>Arguments for the tool call.</summary>
    public Dictionary<string, string?> Args { get; set; } = new();

     /// <summary>Which sub-task indices this call covers (0-based).</summary>
    public List<int> CoversSubTasks { get; set; } = new();

     /// <summary>Human-readable description of what this call does.</summary>
    public string Description { get; set; } = "";
}
