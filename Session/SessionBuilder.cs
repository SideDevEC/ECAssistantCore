using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Services;
using ECAssistant.Core.Session;
using ECAssistant.Core.Tools;
using ECAssistant.Core.Tools.Background;
using ECAssistant.Core.Tools.Build;
using ECAssistant.Core.Tools.Code;
using ECAssistant.Core.Tools.Git;
using ECAssistant.Core.Tools.Reader;
using ECAssistant.Core.Tools.Research;
using ECAssistant.Core.Tools.Shell;
using ECAssistant.Core.Tools.Web;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Services.Http;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core;

/// <summary>
/// Builder for creating and initializing AgentSessions with standard tools.
///
/// This extracts the session initialization logic from Program.cs into a reusable
/// public API. Library consumers (e.g., ECSQL) use this to create sessions with
/// the built-in tool set, then add their own custom tools on top.
///
/// Usage:
///   var builder = new SessionBuilder(config, workingDir, logger);
///   builder.ExternalTools.Add(new MyCustomTool());
///   await builder.BuildAsync(session);
///
/// To skip built-in tools entirely (only your custom tools):
///   var builder = new SessionBuilder(config, workingDir, logger) { RegisterBuiltInTools = false };
///   builder.ExternalTools.Add(new MyCustomTool());
/// </summary>
public class SessionBuilder
{
    private readonly EAgentConfig _config;
    private readonly string _workingDir;
    private readonly string _userConfigDir;
    private readonly ILogger _logger;
    private readonly BackgroundProcessManager _bgManager;

    /// <summary>
    /// External tools to register alongside built-in tools.
    /// Set before calling BuildAsync(). These are registered first,
    /// before the native tools.
    /// </summary>
    public List<EToolBase> ExternalTools { get; set; } = new();

    /// <summary>
    /// Whether to register the built-in ECAssistant tools (shell, file reader, git, etc.).
    /// Default: true. Set to false if you want ONLY your custom tools.
    /// </summary>
    public bool RegisterBuiltInTools { get; set; } = true;

    /// <summary>
    /// Whether to initialize vector memory (FAISS semantic search).
    /// Default: follows config (config.VectorMemory.Enabled).
    /// </summary>
    public bool? EnableVectorMemory { get; set; }

    /// <summary>
    /// Whether to initialize sub-agents.
    /// Default: follows config (config.SubAgent.Enabled).
    /// </summary>
    public bool? EnableSubAgents { get; set; }

    /// <summary>
    /// Wire background task config into the session. Default: true.
    /// Passes config.BackgroundTasks to the engine for HTTP stateless inference use.
    /// </summary>
    public bool EnableBackgroundTasks { get; set; } = true;

    /// <summary>
    /// The background process manager shared across sessions.
    /// Created automatically if not provided.
    /// </summary>
    public BackgroundProcessManager BackgroundManager => _bgManager;

    /// <summary>
    /// Create a SessionBuilder.
    /// </summary>
    /// <param name="config">Loaded EAgentConfig from appsettings.json</param>
    /// <param name="workingDir">Working directory for the agent</param>
    /// <param name="userConfigDir">User config directory (for resolving relative model paths)</param>
    /// <param name="logger">Logger instance (optional, creates default if null)</param>
    /// <param name="bgManager">Background process manager (optional, creates one if null)</param>
    public SessionBuilder(
        EAgentConfig config,
        string workingDir,
        string userConfigDir,
        ILogger? logger = null,
        BackgroundProcessManager? bgManager = null)
    {
        _config = config;
        _workingDir = workingDir;
        _userConfigDir = userConfigDir;
        _logger = logger ?? new Logger();
        _bgManager = bgManager ?? new BackgroundProcessManager();
    }

    /// <summary>
    /// Build a fully initialized session with standard tools + optional secondary model.
    /// Uses the ExternalTools property if set.
    /// </summary>
    public async Task BuildAsync(AgentSession session)
    {
        await BuildAsync(session, ExternalTools.Count > 0 ? ExternalTools : null);
    }

    /// <summary>
    /// Build a fully initialized session with external tools + standard tools + secondary model.
    /// External tools are registered first (if enabled), then built-in tools.
    /// Both get their config section written if missing.
    /// </summary>
    /// <param name="externalTools">Tools from the host to register alongside native tools</param>
    public async Task BuildAsync(AgentSession session, List<EToolBase>? externalTools)
    {
        // ── Vector Memory (semantic search) ──
        if (EnableVectorMemory ?? _config.VectorMemory.Enabled)
        {
            var vecDir = Path.Combine(_workingDir, _config.VectorMemory.Directory);
            
            // Create embedder from config — uses real LLM if configured, falls back to TF-IDF
            IVectorEmbedder? embedder = null;
            if (_config.Embedding != null && _config.Embedding.Enabled)
            {
                var embedderClient = new OpenAIClient(_config.LlmProvider.Endpoint);
                embedder = new HttpEmbedder(embedderClient, _config.LlmProvider.EmbeddingModelId);
            }
            
            await session.InitializeVectorMemoryAsync(vecDir, embedder);
        }

        // ── Project Context Manager ──
        await session.InitializeProjectContextAsync();

        // ── Register tools (external first, then built-in) ──
        if (RegisterBuiltInTools || (externalTools != null && externalTools.Count > 0))
        {
            RegisterBuiltInToolsAsync(session, externalTools);
        }

        // ── Background Tasks (decompose + summarize config) ──
        if (EnableBackgroundTasks)
        {
            session.SetBackgroundTasks(_config.BackgroundTasks);
        }

        // ── Sub-agents (only if enabled) ──
        if (EnableSubAgents ?? _config.SubAgent.Enabled)
        {
            await session.InitializeSubAgentsAsync();
        }
    }

    /// <summary>
    /// Register external tools first, then built-in tools.
    /// External tools are processed first so that native tools take precedence
    /// in case of name conflicts. Both follow the same logic:
    /// - Config section written if missing (via GetConfigSection())
    /// - Tool registered only if IsEnabled is true
    /// </summary>
    public void RegisterBuiltInToolsAsync(AgentSession session, List<EToolBase>? externalTools = null)
    {
        // ── External tools first ──
        if (externalTools != null)
        {
            foreach (var tool in externalTools)
            {
                EnsureToolConfigSection(tool);
                if (tool.IsEnabled)
                    session.RegisterTool(tool);
            }
        }

        // ── Built-in tools ──
        RegisterNativeTools(session);
    }

    /// <summary>
    /// Register all built-in ECAssistant tools on the session.
    /// Creates each tool, ensures its config section exists, and registers
    /// only if the tool is enabled.
    /// </summary>
    private void RegisterNativeTools(AgentSession session)
    {
        var fileSystem = new FileSystemAdapter();
        var processRunner = new ProcessRunner();
        var httpClient = new HttpClientAdapter();

        // Create all tool instances
        var shellAgent = new EShellAgent(processRunner, _config, _workingDir);
        var backgroundExec = new EBackgroundExecTool(_bgManager, processRunner, fileSystem, _config);
        var webSearch = new EWebSearchTool(httpClient, _config);
        var dotnetBuild = new EDotnetBuildTool(processRunner, _config);
        var gitTool = new EGitTool(processRunner, fileSystem, _config);
        var codeEditor = new ECodeEditorTool(fileSystem, _config);
        var fileReader = new EFileReaderTool(fileSystem, _config);
        var webFetch = new EWebFetchTool(httpClient, _config);
        var fileResearch = new EFileResearchTool(fileSystem, _config);

        // Ensure config sections exist for all tools (regardless of enabled state)
        // and register only enabled tools
        EnsureAndRegister(session, shellAgent);
        EnsureAndRegister(session, backgroundExec);
        EnsureAndRegister(session, webSearch);
        EnsureAndRegister(session, dotnetBuild);
        EnsureAndRegister(session, gitTool);
        EnsureAndRegister(session, codeEditor);
        EnsureAndRegister(session, fileReader);
        EnsureAndRegister(session, webFetch);
        EnsureAndRegister(session, fileResearch);
    }

    /// <summary>
    /// Ensure the tool's config section exists in EAgentConfig.Tools.
    /// If not, writes the default from GetConfigSection() and persists to appsettings.json.
    /// Called for all tools — enabled or disabled — so config always has a section.
    /// </summary>
    private void EnsureToolConfigSection(EToolBase tool)
    {
        if (!_config.Tools.ContainsKey(tool.Name))
        {
            var section = tool.GetConfigSection();
            var jsonElement = JsonSerializer.SerializeToElement(section);
            _config.Tools[tool.Name] = jsonElement;
            AgentConfigBuilder.Default.Update(_config);
        }
    }

    /// <summary>
    /// Ensure config section exists, then register the tool if it's enabled.
    /// </summary>
    private void EnsureAndRegister(AgentSession session, EToolBase tool)
    {
        EnsureToolConfigSection(tool);
        if (tool.IsEnabled)
            session.RegisterTool(tool);
    }

}