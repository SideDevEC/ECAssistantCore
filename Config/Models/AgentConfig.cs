using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class AgentConfig
{
    [JsonPropertyName("working_directory")]
    public string WorkingDirectory { get; set; } = ".";  // Mutable: set by AgentConfigBuilder during loading

    /// <summary>Hard cap for a single agent execution run (StartRunner). Default 10 minutes.</summary>
    [JsonPropertyName("execution_timeout_minutes")]
    public int ExecutionTimeoutMinutes { get; set; } = 10;
    [JsonPropertyName("allow_delete")]
    public bool AllowDelete { get; init; } = true;
    [JsonPropertyName("allowed_extensions")]
    public List<string> AllowedExtensions { get; init; } = new() { ".txt", ".json", ".md", ".cs", ".py" };
}