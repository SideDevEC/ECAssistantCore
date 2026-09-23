using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Config;

/// <summary>App settings — matches the nested structure in appsettings.json</summary>
public class AppConfig
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

    /// <summary>Tool-result truncation limits (2026-09-21 — was hardcoded constants).</summary>
    [JsonPropertyName("tool_output_limits")]
    public ToolOutputLimitsConfig ToolOutputLimits { get; set; } = new();

    [JsonPropertyName("llm")]
    public LlmConfig Llm { get; set; } = new();

    [JsonPropertyName("background_tasks")]
    public BackgroundTasksConfig BackgroundTasks { get; set; } = new();

    [JsonPropertyName("subagent")]
    public SubAgentConfig SubAgent { get; set; } = new();

    /// <summary>v14.9: interactive checkpoint policy (RequestChoice hook points).</summary>
    [JsonPropertyName("interaction")]
    public InteractionConfig Interaction { get; set; } = new();

    [JsonPropertyName("vector_memory")]
    public VectorMemoryConfig VectorMemory { get; set; } = new();

    [JsonPropertyName("embedding")]
    public EmbeddingConfig Embedding { get; set; } = new();

    [JsonPropertyName("inference")]
    public InferenceConfig Inference { get; set; } = new();

    [JsonPropertyName("context_management")]
    public ContextManagementConfig ContextManagement { get; set; } = new();

    /// <summary>v14.16: tier-aware proactive context pinning.</summary>
    [JsonPropertyName("context_pinning")]
    public ContextPinningConfig ContextPinning { get; set; } = new();

    [JsonPropertyName("sampling")]
    public SamplingConfig Sampling { get; set; } = new();

    [JsonPropertyName("agent")]
    public AgentConfig AgentSettings { get; set; } = new();

    [JsonPropertyName("interface")]
    public InterfaceConfig Interface { get; set; } = new();

    [JsonPropertyName("llm_provider")]
    public LlmProviderConfig LlmProvider { get; set; } = new();

    /// <summary>v14.12: model-tier profile gating harness scaffolding depth. Null = auto.</summary>
    [JsonPropertyName("model_tier")]
    // v15 fix: default instance, not null. A config without explicit model_tier must
    // resolve via the auto tier resolver (remote → large, local → small); a null
    // ModelTier made IsLargeModelTier collapse to false (small) for every config
    // that omitted the section — including remote frontier models.
    public ModelTierConfig ModelTier { get; init; } = new();

    /// <summary>v14.13: tier-aware post-edit verification gate (build/test after file edits).</summary>
    [JsonPropertyName("verification")]
    public VerificationConfig Verification { get; set; } = new();

    /// <summary>
    /// Multi-provider section (OpenClaw-style). When present with valid entries,
    /// remote mode resolves through the registry (default provider + optional fallback).
    /// Local mode is unaffected. Null/empty = single-provider "llm_provider" behavior.
    /// </summary>
    [JsonPropertyName("llm_providers")]
    public MultiLlmProvidersConfig? LlmProviders { get; set; }

    /// <summary>
    /// MCP (Model Context Protocol) server connections. Stdio subprocess or HTTP/SSE.
    /// Empty = no MCP tools registered. See McpConfig for schema.
    /// </summary>
    [JsonPropertyName("mcp")]
    public McpConfig? Mcp { get; set; }

    /// <summary>
    /// THE integration answer: "can ECAssistant handle vision (image input)?"
    /// Mode-independent — true for a local install with an mmproj-wired model,
    /// or a remote provider declared vision-capable at setup.
    /// </summary>
    [JsonIgnore]
    public bool SupportsVision => LlmProvider?.VisionEnabled ?? false;

    [JsonPropertyName("tool_permissions")]
    public List<ToolPermissionConfigEntry>? ToolPermissions { get; set; }

    /// <summary>
    /// System-critical tools — always registered, cannot be disabled.
    /// Only permission level can be changed (Allowed or ApprovalRequired).
    /// Default is ApprovalRequired.
    /// </summary>
    [JsonPropertyName("system_tools")]
    public List<SystemToolConfigEntry>? SystemTools { get; set; }

    public string GetRootPath() => Path.GetFullPath(RootPath);
    public string GetMemoryDirectory() => Path.GetFullPath(Path.Combine(RootPath, "Memory"));
    public string GetWorkspaceDirectory() => Path.GetFullPath(Path.Combine(RootPath, "Workspace"));

    public void Save(string filePath = "appsettings.json")
    {
        File.WriteAllText(filePath, JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
