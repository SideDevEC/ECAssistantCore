using System.Text.Json.Serialization;

namespace ECAssistant.Config;

public class AgentConfig
{
    [JsonPropertyName("working_directory")]
    public string WorkingDirectory { get; set; } = ".";
    [JsonPropertyName("allow_delete")]
    public bool AllowDelete { get; set; } = true;
    [JsonPropertyName("allowed_extensions")]
    public List<string> AllowedExtensions { get; set; } = new() { ".txt", ".json", ".md", ".cs", ".py" };
}