using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class MemoryConfig
{
    [JsonPropertyName("data_path")]
    public string DataPath { get; init; } = "Memory";
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
    [JsonPropertyName("load_on_start")]
    public bool LoadOnStart { get; init; } = true;
    [JsonPropertyName("save_on_exit")]
    public bool SaveOnExit { get; init; } = true;
    [JsonPropertyName("max_entries")]
    public int MaxEntries { get; init; } = 500;
    [JsonPropertyName("auto_backup")]
    public bool AutoBackup { get; init; } = true;
}