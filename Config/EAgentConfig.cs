using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Config;

/// <summary>App settings — matches the nested structure in appsettings.json</summary>
public class EAgentConfig
{
    [JsonPropertyName("root_path")]
    public string RootPath { get; set; } = ".";  // v10.23: default to current dir, not 'ECAssistant'

    [JsonPropertyName("memory")]
    public MemoryConfig Memory { get; set; } = new();

    [JsonPropertyName("workspace")]
    public WorkspaceConfig Workspace { get; set; } = new();

    // v10.24: Tools is now a dynamic dictionary — any tool can add its config section
    [JsonPropertyName("tools")]
    public Dictionary<string, JsonElement> Tools { get; set; } = new();

    [JsonPropertyName("llm")]
    public LlmConfig Llm { get; set; } = new();

    [JsonPropertyName("background_tasks")]
    public BackgroundTasksConfig BackgroundTasks { get; set; } = new();

    [JsonPropertyName("subagent")]
    public SubAgentConfig SubAgent { get; set; } = new();

    [JsonPropertyName("vector_memory")]
    public VectorMemoryConfig VectorMemory { get; set; } = new();

    [JsonPropertyName("embedding")]
    public EmbeddingConfig Embedding { get; set; } = new();

    [JsonPropertyName("inference")]
    public InferenceConfig Inference { get; set; } = new();

    [JsonPropertyName("context_management")]
    public ContextManagementConfig ContextManagement { get; set; } = new();

    [JsonPropertyName("sampling")]
    public SamplingConfig Sampling { get; set; } = new();

    [JsonPropertyName("agent")]
    public AgentConfig AgentSettings { get; set; } = new();

    [JsonPropertyName("interface")]
    public InterfaceConfig Interface { get; set; } = new();

    [JsonPropertyName("llm_server")]
    public LlmServerEndpointConfig LlmServer { get; set; } = new();

    public string GetRootPath() => Path.GetFullPath(RootPath);
    public string GetMemoryDirectory() => Path.GetFullPath(Path.Combine(RootPath, "Memory"));
    public string GetWorkspaceDirectory() => Path.GetFullPath(Path.Combine(RootPath, "Workspace"));

    public void Save(string filePath = "appsettings.json")
    {
        File.WriteAllText(filePath, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}