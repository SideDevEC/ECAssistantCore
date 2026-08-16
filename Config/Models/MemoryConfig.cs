using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

public class MemoryConfig
{
    [JsonPropertyName("data_path")]
    public string DataPath { get; set; } = "Memory";
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    [JsonPropertyName("load_on_start")]
    public bool LoadOnStart { get; set; } = true;
    [JsonPropertyName("save_on_exit")]
    public bool SaveOnExit { get; set; } = true;
    [JsonPropertyName("max_entries")]
    public int MaxEntries { get; set; } = 500;
    [JsonPropertyName("auto_backup")]
    public bool AutoBackup { get; set; } = true;
}