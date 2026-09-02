using System.IO;
using System.Text.Json;
using ECAssistant.Core.Config;

namespace ECAssistant.Core;

/// <summary>
/// Fluent config builder for library consumers.
///
/// On Build():
/// 1. Resolve working directory: use the given path directly (default: "./")
/// 2. If appsettings.json exists there → load it, return it (JSON is source of truth)
/// 3. If not → generate appsettings.json with code values + defaults, return it
///
/// Code values (WithModel, ContextSize, etc.) are ONLY used to seed the
/// initial JSON. After that, the JSON is authoritative. End users edit
/// the JSON to change settings — they never see the code.
///
/// Usage:
///   // First run: generates appsettings.json with these values
///   var config = AgentConfigBuilder.Create()
///       .WithModel("/path/to/model.gguf")
///       .ContextSize(16384)
///       .MaxTurns(10)
///       .Verbose(true)
///       .Build();
///
///   // Subsequent runs: loads appsettings.json, ignores code values
///   var config = AgentConfigBuilder.Create()
///       .WithModel("/path/to/model.gguf")  // ignored — JSON exists
///       .Build();
///
/// End users edit appsettings.json to tweak settings.
/// </summary>
public class AgentConfigBuilder
{
    private string _modelPath = "";
    private uint _contextSize = 16384;
    private int _maxTokens = 2048;
    private float _temperature = 0.3f;
    private float _topP = 0.9f;
    private int _topK = 40;
    private float _repeatPenalty = 1.1f;
    private string _workingDir = ".";
    private bool _enableVectorMemory = false;
    private bool _enableSubAgents = false;
    private bool _decomposeUseLlm = true;
    private bool _summarizeUseLlm = true;
    private bool _verbose = true;
    private bool _silent = false;
    private int _maxTurns = 10;
    private string _providerMode = "local";
    private string _providerEndpoint = "http://localhost:8420";
    private int _providerPort = 8420;
    private string _providerHost = "localhost";
    private string? _providerApiKey = null;
    private string _providerModelId = "main";

    private AgentConfigBuilder() { }

    /// <summary>Start building a config.</summary>
    public static AgentConfigBuilder Create() => new();

    /// <summary>Path to the GGUF model file. Used only when generating initial JSON.</summary>
    public AgentConfigBuilder WithModel(string path) { _modelPath = path; return this; }

    /// <summary>LLM context window size in tokens. Default: 16384. Seeds initial JSON only.</summary>
    public AgentConfigBuilder ContextSize(uint size) { _contextSize = size; return this; }

    /// <summary>Max agent turns (iterations) per user request. 0 = unlimited. Default: 10. Seeds initial JSON only.</summary>
    public AgentConfigBuilder MaxTurns(int turns) { _maxTurns = turns; return this; }

    /// <summary>Verbose output: show token stream, debug info, KV cache status. Default: true. Seeds initial JSON only.</summary>
    public AgentConfigBuilder Verbose(bool enabled = true) { _verbose = enabled; return this; }

    /// <summary>Silent mode: suppress token stream noise. Only show final results and errors. Default: false. Seeds initial JSON only.</summary>
    public AgentConfigBuilder Silent(bool enabled = true) { _silent = enabled; return this; }

    /// <summary>Max tokens per response. Default: 2048. Seeds initial JSON only.</summary>
    public AgentConfigBuilder MaxTokens(int tokens) { _maxTokens = tokens; return this; }

    /// <summary>Sampling temperature. Default: 0.3. Seeds initial JSON only.</summary>
    public AgentConfigBuilder Temperature(float temp) { _temperature = temp; return this; }

    /// <summary>Top-p sampling. Default: 0.9. Seeds initial JSON only.</summary>
    public AgentConfigBuilder TopP(float topP) { _topP = topP; return this; }

    /// <summary>Top-k sampling. Default: 40. Seeds initial JSON only.</summary>
    public AgentConfigBuilder TopK(int topK) { _topK = topK; return this; }

    /// <summary>Repeat penalty. Default: 1.1. Seeds initial JSON only.</summary>
    public AgentConfigBuilder RepeatPenalty(float penalty) { _repeatPenalty = penalty; return this; }

    /// <summary>
    /// Base directory for agent data. Used directly — no subdirectory appended.
    /// Default: "." → resolves to "./".
    /// Example: .WorkingDirectory("/var/lib/myapp") → "/var/lib/myapp/appsettings.json"
    /// </summary>
    public AgentConfigBuilder WorkingDirectory(string dir) { _workingDir = dir; return this; }

    /// <summary>Enable vector memory (semantic search). Default: false. Seeds initial JSON only.</summary>
    public AgentConfigBuilder EnableVectorMemory(bool enabled = true) { _enableVectorMemory = enabled; return this; }

    /// <summary>Enable sub-agents (parallel child agents). Default: false. Seeds initial JSON only.</summary>
    public AgentConfigBuilder EnableSubAgents(bool enabled = true) { _enableSubAgents = enabled; return this; }

    /// <summary>Use LLM for task decomposition. When false, uses keyword-based fallback. Seeds initial JSON only.</summary>
    public AgentConfigBuilder DecomposeUseLlm(bool useLlm = true) { _decomposeUseLlm = useLlm; return this; }

    /// <summary>Use LLM for summarization. When false, uses extractive truncation fallback. Seeds initial JSON only.</summary>
    public AgentConfigBuilder SummarizeUseLlm(bool useLlm = true) { _summarizeUseLlm = useLlm; return this; }

    // ── LLM provider config ──

    /// <summary>Use local ECAssistantLLM server (spawns if needed, full KV cache support). Seeds initial JSON only.</summary>
    public AgentConfigBuilder UseLocalLLM(int port = 8420, string host = "localhost")
    {
        _providerMode = "local";
        _providerPort = port;
        _providerHost = host;
        _providerEndpoint = $"http://{host}:{port}";
        _providerApiKey = null;
        return this;
    }

    /// <summary>Use remote OpenAI-compatible API (no KV cache, stateless inference). Seeds initial JSON only.</summary>
    public AgentConfigBuilder UseRemoteLLM(string endpoint, string apiKey, string modelId)
    {
        _providerMode = "remote";
        _providerEndpoint = endpoint;
        _providerApiKey = apiKey;
        _providerModelId = modelId;
        return this;
    }

    /// <summary>
    /// Build the EAgentConfig.
    ///
    /// 1. Use working dir directly (no subdirectory appended)
    /// 2. If appsettings.json exists there → load and return it (JSON wins)
    /// 3. If not → generate it with code values + defaults, then return
    /// </summary>
    public EAgentConfig Build()
    {
        // 1. Use working directory directly
        var workingDir = _workingDir;
        Directory.CreateDirectory(workingDir);

        var jsonPath = Path.Combine(workingDir, "appsettings.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };

        // 2. JSON exists → load it, that's the config (source of truth)
        if (File.Exists(jsonPath))
        {
            try
            {
                var json = File.ReadAllText(jsonPath);
                var config = JsonSerializer.Deserialize<EAgentConfig>(json, jsonOptions);
                if (config != null)
                {
                    // Ensure working dir paths point to the resolved location
                    config.RootPath = workingDir;
                    if (config.AgentSettings == null) config.AgentSettings = new AgentConfig();
                    config.AgentSettings.WorkingDirectory = workingDir;
                    return config;
                }
            }
            catch
            {
                // Corrupt JSON → fall through to generate fresh
            }
        }

        // 3. No JSON (or corrupt) → build from code values + defaults
        var freshConfig = new EAgentConfig
        {
            RootPath = workingDir,
            AgentSettings = new AgentConfig { WorkingDirectory = workingDir },
            Llm = new LlmConfig
            {
                ModelPath = _modelPath,
                ContextSize = _contextSize,
            },
            Inference = new InferenceConfig
            {
                MaxTokens = _maxTokens,
                AntiPrompts = new[] { "User:", "\n```\n", "Question:", "### User", "<user>" },
            },
            Sampling = new SamplingConfig
            {
                Temperature = _temperature,
                TopP = _topP,
                TopK = _topK,
                RepeatPenalty = _repeatPenalty,
            },
            VectorMemory = new VectorMemoryConfig
            {
                Enabled = _enableVectorMemory,
                Directory = "vecmem",
                MaxResults = 5,
                AutoIndex = true,
            },
            SubAgent = new SubAgentConfig
            {
                Enabled = _enableSubAgents,
            },
            Interface = new InterfaceConfig
            {
                Verbose = _verbose,
                Silent = _silent,
                MaxTurns = _maxTurns,
            },
            LlmProvider = new LlmProviderConfig
            {
                Mode = _providerMode,
                Endpoint = _providerEndpoint,
                Port = _providerPort,
                Host = _providerHost,
                ApiKey = _providerApiKey,
                ModelId = _providerModelId,
            },
            BackgroundTasks = new BackgroundTasksConfig
            {
                Decompose = new DecomposeConfig
                {
                    UseLlm = _decomposeUseLlm,
                    ContextSize = 4096,
                    MaxTokens = 256,
                    Temperature = 0.1f,
                    TopP = 0.8f,
                    TopK = 40,
                    RepeatPenalty = 1.1f,
                    AntiPrompts = new[] { "User:", "Question:" },
                },
                Summarize = new SummarizeConfig
                {
                    UseLlm = _summarizeUseLlm,
                    ContextSize = 4096,
                    MaxTokens = 200,
                    Temperature = 0.1f,
                    TopP = 0.8f,
                    TopK = 40,
                    RepeatPenalty = 1.1f,
                    AntiPrompts = new[] { "User:", "Question:" },
                },
            },
        };

        // Generate the JSON file so it exists for next time
        var freshJson = JsonSerializer.Serialize(freshConfig, jsonOptions);
        File.WriteAllText(jsonPath, freshJson);

        return freshConfig;
    }

    /// <summary>
    /// Default shared instance for convenience (used by static Update calls).
    /// </summary>
    public static readonly AgentConfigBuilder Default = new();

    /// <summary>
    /// v10.24: Write an updated EAgentConfig back to appsettings.json.
    /// Uses config.RootPath to locate the file. Called when new tools are registered
    /// and their config sections are added to the Tools dictionary.
    /// </summary>
    public void Update(EAgentConfig config)
    {
        var jsonPath = Path.Combine(config.RootPath, "appsettings.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        var json = JsonSerializer.Serialize(config, jsonOptions);
        File.WriteAllText(jsonPath, json);
    }
}