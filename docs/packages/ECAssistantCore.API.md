# ECAssistantCore.API.md

Types: 279  |  LOC: 24660  |  ~12967 tokens

---

### Interface: IAiSetupResetter
> Resets all AI setup state back to first-run defaults: clears configured
Methods:
  - void Reset(string userConfigDir)
Cross-package deps: ECAssistant.Core.Setup

### Interface: IConfigLoader
> Interface for loading EAgentConfig from JSON files.
Methods:
  - EAgentConfig Load(string filePath = "appsettings.json")
Cross-package deps: ECAssistant.Core.Config

### Interface: IConfigProvider
> Configuration access abstraction.
Methods:
  - string GetValue(string key, string defaultValue = "")
  - int GetInt(string key, int defaultValue = 0)
  - float GetFloat(string key, float defaultValue = 0f)
  - bool GetBool(string key, bool defaultValue = false)

### Interface: IContextManager
> Context window management with summary-and-shift strategy.
Properties:
  - int MessageCount { get; set; }
Methods:
  - void AddMessage(TranscriptMessage message)
  - List<TranscriptMessage> GetMessages()
  - Task<string> SummarizeAsync()
  - bool NeedsShift()
  - void Shift()

### Interface: IEngine
> Engine interface for testing/abstraction.
Methods:
  - Task StartAsync(string userMessage)
  - Task RunAsync(CancellationToken ct = default)
  - void Dispose()

### Interface: IEngineToolContext
> Read-only view of the engine's tool surface used by planning components
Properties:
  - IReadOnlyList<EToolBase> Tools { get; set; }
Methods:
  - Task<string?> GeneratePlanAsync(string userRequest)
Cross-package deps: ECAssistant.Core.Tools

### Interface: IFileSystem
> File system abstraction.
Methods:
  - string ReadFile(string path)
  - string[] ListFiles(string directory, string pattern = "*")
  - bool FileExists(string path)
  - void WriteFile(string path, string content)
  - bool DirectoryExists(string path)
  - void CreateDirectory(string path)

### Interface: IHtmlTextConverter
> Converts raw HTML into structured plain text, preserving block-level
Methods:
  - string Convert(string html)

### Interface: IHttpClient
> HTTP client abstraction.
Methods:
  - Task<string> GetAsync(string url, CancellationToken ct = default)
  - Task<string> GetAsync(string url, Dictionary<string, string>? headers, CancellationToken ct = default)
  - Task<string> PostAsync(string url, string content, CancellationToken ct = default)

### Interface: IInferenceEngine
> Abstracts LLM inference via HTTP (OpenAI-compatible endpoint).
Properties:
  - string Endpoint { get; set; }
Methods:
  - IAsyncEnumerable<string> StreamAsync(string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default)
  - Task<string> GenerateAsync(string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default)

### Interface: IKvCacheController
> KV cache control over HTTP. Replaces direct LLamaSharp executor state management.
Methods:
  - Task<bool> CreateSessionAsync(string sessionId, CancellationToken ct = default)
  - Task<bool> DestroySessionAsync(string sessionId, CancellationToken ct = default)
  - Task<bool> PrefillAsync(string sessionId, string text, CancellationToken ct = default)
  - Task<bool> SaveStateAsync(string sessionId, CancellationToken ct = default)
  - Task<bool> RewindAsync(string sessionId, CancellationToken ct = default)
  - Task<bool> ResetAsync(string sessionId, CancellationToken ct = default)
  - Task<KvCacheStatus?> GetStatusAsync(string sessionId, CancellationToken ct = default)

### Interface: ILlmProviderRegistry
> A fully-resolved remote provider ready for engine construction.
Properties:
  - IReadOnlyList<string> ValidationErrors { get; set; }
  - IReadOnlyList<RemoteProvider> Providers { get; set; }
  - RemoteProvider? Default { get; set; }
Methods:
  - RemoteProvider? GetByName(string? name)
  - IReadOnlyList<RemoteProvider> OrderedCandidates(string? preferredName = null)

### Interface: ILlmServerClient
> Client lifecycle management for ECAssistantLLM server.
Implements: IAsyncDisposable
Properties:
  - string ClientId { get; set; }
  - bool IsConnected { get; set; }
Methods:
  - Task<bool> ConnectAsync(string clientName, string? version = null, CancellationToken ct = default)
  - Task<bool> HeartbeatAsync(int activeSessions = 0, CancellationToken ct = default)
  - Task<bool> DisconnectAsync(CancellationToken ct = default)

### Interface: ILogger
> Structured logging interface — file only, headless.
Properties:
  - bool IsDebugEnabled { get; set; }
  - string LogFilePath { get; set; }
  - long LogFileSize { get; set; }
Methods:
  - void Initialize(string logFilePath, LogLevel minLevel)
  - void SetLevel(LogLevel level)
  - void Debug(string tag, string message)
  - void Info(string tag, string message)
  - void Warn(string tag, string message)
  - void Error(string tag, string message)
  - void Error(string tag, string message, Exception ex)
  - string GetRecentLines(int count)
Cross-package deps: ECAssistant.Core.Services

### Interface: IMemoryService
> Persistent memory with vector search.
Methods:
  - Task<List<MemoryEntry>> SearchAsync(string query, int maxResults = 5)
  - Task AddAsync(MemoryEntry entry)
  - Task SaveAsync()
  - Task LoadAsync()

### Interface: IModelLoader
> Remote model loading via HTTP. Replaces the old LLamaSharp-based IModelLoader.
Methods:
  - Task<IReadOnlyList<RemoteModelInfo>> GetLoadedModelsAsync(CancellationToken ct = default)
  - Task<bool> LoadModelAsync(string modelId, string path, RemoteModelLoadOptions options, CancellationToken ct = default)
  - Task<bool> UnloadModelAsync(string modelId, CancellationToken ct = default)

### Interface: IModelParamValidator
> Interface for validating model parameters before loading.
Methods:
  - ModelLoadException? Validate(string modelPath, uint contextSize)
  - ModelLoadException? Validate(EAgentConfig config, string resolvedModelPath)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine

### Interface: IOutputListener
> Listener interface for session output.
Methods:
  - void OnOutput(string text, OutputState state)
  - void OnStreamStart()
  - void OnStreamStop()
  - bool OnRequestApproval(string message)

### Interface: IOutputRenderer
> Terminal output abstraction.
Methods:
  - void Write(string text)
  - void WriteLine(string text)
  - void WriteColored(string color, string text)
  - void WriteColoredLine(string color, string text)
  - void Flush()

### Interface: IParallelToolExecutor
> Interface for parallel tool execution with dependency-aware scheduling.
Methods:
  - Task<BatchToolResult> ExecuteAsync(List<ToolCallRequest> toolCalls, CancellationToken ct = default)
  - string CombineResults(BatchToolResult batch)
  - string FormatConsoleSummary(BatchToolResult batch)
Cross-package deps: ECAssistant.Core.Engine

### Interface: IProcessRunner
> Abstract process execution.
Methods:
  - Task<ProcessResult> ExecuteAsync(string command, string? workDir = null, CancellationToken ct = default)

### Interface: IReadableContentExtractor
> Extracts the main readable content from an HTML page, discarding
Methods:
  - string Extract(string html)

### Interface: ISecureKeyStore
> Cross-platform self-encrypting API key store.
Properties:
  - string KeysDirectory { get; set; }
Methods:
  - string GetKey(string fileName)
  - void SetKey(string fileName, string plaintext)

### Interface: ISessionBuilder
> Interface for building and initializing AgentSessions with standard tools.
Properties:
  - List<EToolBase> ExternalTools { get; set; }
  - bool RegisterBuiltInTools { get; set; }
  - bool? EnableVectorMemory { get; set; }
  - bool? EnableSubAgents { get; set; }
  - bool EnableBackgroundTasks { get; set; }
  - BackgroundProcessManager BackgroundManager { get; set; }
Methods:
  - Task BuildAsync(AgentSession session)
  - Task BuildAsync(AgentSession session, List<EToolBase>? externalTools)
  - void RegisterBuiltInToolsAsync(AgentSession session, List<EToolBase>? externalTools = null)
Cross-package deps: ECAssistant.Core.Session, ECAssistant.Core.Services, ECAssistant.Core.Tools

### Interface: ISessionContext
> Read-only session context exposed to tools.
Properties:
  - string Key { get; set; }
  - string? Label { get; set; }
  - ToolPolicy Policy { get; set; }
  - int ContextTokens { get; set; }
  - uint MaxTokens { get; set; }
  - BackgroundTasksConfig? BackgroundTasks { get; set; }
  - EMemoryManager Memory { get; set; }
  - VectorMemoryStore? VectorMemory { get; set; }
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Memory, ECAssistant.Core.Tools

### Interface: ISessionOutput
> Interface for session output — implemented by AgentSession.
Methods:
  - void StartStream(OutputState state)
  - void Write(string token)
  - void StopStream()
  - void WriteLine(string text, OutputState state = OutputState.Info)
  - void BlankLine()
  - void WriteInfo(string text)
  - void WriteSuccess(string text)
  - void WriteWarning(string text)
  - void WriteError(string text)
  - void WriteDim(string text)
  - void WriteTag(string tag, string message, OutputState state = OutputState.Info)
  - string GetStreamBuffer()
  - OutputState GetStreamState()
  - bool RequestApproval(string message)

### Interface: IStepMapper
> Interface for mapping sub-tasks to concrete tool calls.
Methods:
  - Task<ExecutionPlan> MapAsync(List<SubTask> subTasks, string originalGoal)
Cross-package deps: ECAssistant.Core.Engine

### Interface: ISubAgentEngineHost
> Host-side surface the SubAgentManager needs from the main engine.
Properties:
  - IInferenceEngine? InferenceEngine { get; set; }
  - CancellationToken ExecutionToken { get; set; }

### Interface: ITaskPlanner
> Interface for decomposing user requests into sub-tasks.
Properties:
  - SubTask? Current { get; set; }
  - bool HasRemaining { get; set; }
Methods:
  - List<SubTask> Decompose(string request)
  - void CompleteCurrent()
  - void FailCurrent(string reason)
  - string GetProgressContext()
  - string GetSummary()
Cross-package deps: ECAssistant.Core.Engine

### Interface: ITerminal
> Terminal I/O abstraction.
Properties:
  - int Width { get; set; }
  - int Height { get; set; }
Methods:
  - void Write(string text)
  - string ReadLine()
  - void Clear()

### Interface: IToolPolicyEvaluator
> Evaluates tool permissions.
Methods:
  - bool IsToolAllowed(string toolName)
  - ToolPermissionRecord GetPolicy(string toolName)

### Interface: IVectorEmbedder
> Text embedding abstraction.
Methods:
  - float[] Embed(string text)

### Interface: IVectorStore
> Vector similarity search abstraction.
Methods:
  - Task IndexAsync(string content, string metadata)
  - Task<List<VectorResult>> SearchAsync(float[] embedding, int maxResults = 5)

### Class: ActiveSubAgent

### Class: AgentConfig

### Class: AgentConfigBuilder
> Fluent config builder for library consumers.
Cross-package deps: ECAssistant.Core.Config

### Class: AgentOrchestrator
> Orchestrator — the decision-making brain for multi-step agent workflows.
Implements: IAsyncDisposable
Constructor:
  - AgentOrchestrator(EAgentEngine engine, ISessionOutput? sessionOutput = null, int maxTurns = 5, int maxFailures = 3, ECAssistant.Core.Tools.ToolPolicy? toolPolicy = null, ECAssistant.Core.Interfaces.ILogger? logger = null, ECAssistant.Core.Config.EAgentConfig? config = null)
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools, ECAssistant.Core.Services, ECAssistant.Core.Session, ECAssistant.Core.Interfaces

### Class: AgentSession
> A fully isolated agent session.
Implements: ISessionOutput, ISessionContext, IAsyncDisposable
Constructor:
  - AgentSession(string key, string sessionId, string endpoint, string? clientId, InferenceRequestParams inferenceParams, string workingDir, SemaphoreSlim inferenceLock, SubAgentConfig? subAgentConfig = null, string? label = null, ILogger? logger = null, EAgentConfig? config = null, OpenAIClient? httpClient = null, RemoteTokenizer? remoteTokenizer = null, string? apiKey = null, bool isLocalMode = true)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Memory, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Transport, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools

### Class: AiSetupResetter
> Default <see cref="IAiSetupResetter"/>: rewrites appsettings.json back to the
Implements: IAiSetupResetter
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: AiSetupResetterTests
Cross-package deps: ECAssistant.Core.Setup, ECAssistant.Core.Config

### Class: ApiUserController
Cross-package deps: ECAssistant.Core.Analysis

### Class: BackgroundProcessManager
> Background Process Manager — starts, tracks, and manages long-running processes
Implements: IDisposable

### Class: BackgroundProcessManagerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services

### Class: BackgroundTasksConfig
> Configuration for background tasks (decomposition, summarization) that use

### Class: BatchToolResult
> Combined result of an entire batch execution.
Cross-package deps: ECAssistant.Core.Tools

### Class: BgProcessInfo

### Class: BuildErrorParser
> Parser for .NET build output — extracts errors and warnings.

### Class: CatalogModelFile
> Model category — drives config generation and UI grouping.

### Class: CatalogSuggestedConfig
> Model category — drives config generation and UI grouping.

### Class: ConfigIntegrationTests
> Integration tests for the config loading pipeline — uses real FileSystemAdapter
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ConfigLoader
> Loads EAgentConfig from JSON files.
Implements: IConfigLoader
Constructor:
  - ConfigLoader(IFileSystem fileSystem)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Services

### Class: ConfigLoaderTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, Moq

### Class: ConfigProvider
> JSON configuration loader.
Implements: IConfigProvider
Constructor:
  - ConfigProvider(IFileSystem fileSystem, string configPath, IFileSystem fileSystem, EAgentConfig config)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Config

### Class: ConfigProviderTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: ContextManagementConfig

### Class: ContextManager
> Context window management with summary-and-shift strategy.
Implements: IContextManager
Constructor:
  - ContextManager(IInferenceEngine inferenceEngine, IConfigProvider configProvider)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: ContextManagerTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: ContextWindow
> Manages the LLM conversation context window.
Constructor:
  - ContextWindow(uint maxTokens, TokenCounter? tokenCounter = null, uint maxTokens, SummaryService? summaryService, TokenCounter? tokenCounter = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services

### Class: ContextWindowIntegrationTests
> Integration tests for ContextWindow with a real TokenCounter —
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Services

### Class: ContextWindowTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Services

### Class: ConversationTranscript
> Full conversation transcript — all messages across all turns.

### Class: ConversationTranscriptTests
Cross-package deps: ECAssistant.Core.Engine

### Class: DebugProbeTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Web

### Class: DecisionResult

### Class: DecomposeConfig
> Task decomposition settings. When use_llm is true, uses HTTP streaming

### Class: DependencyGroup
> A group of toolcalls that can execute in parallel.

### Class: DependencyGroupTests
Cross-package deps: ECAssistant.Core.Engine

### Class: EAgentConfig
> App settings — matches the nested structure in appsettings.json
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Tools

### Class: EAgentConfigTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config

### Class: EAgentEngine
> v10.30: Core engine. All inference + KV cache control is HTTP-based via the
Implements: IEngine, IEngineToolContext, ISubAgentEngineHost
Constructor:
  - EAgentEngine(string sessionId, IInferenceEngine inferenceEngine, IKvCacheController kvCacheController, RemoteTokenizer? tokenizer = null, InferenceRequestParams? inferenceParams = null, uint contextSize = 8192, string modelPath = "", EAgentConfig? config = null, string? workingDir = null, ILogger? logger = null, EMemoryManager? memoryManager = null, ECAssistant.Core.Engine.SelfCorrectionManager? selfCorrection = null, ECAssistant.Core.Engine.ProjectContextManager? projectContext = null, ITaskPlanner? taskPlanner = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Memory, ECAssistant.Core.Engine, ECAssistant.Core.Session, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Tools, ECAssistant.Core.Transport

### Class: EBackgroundExecTool
> Background Exec Tool — lets the LLM start long-running processes
Implements: EToolBase
Constructor:
  - EBackgroundExecTool(BackgroundProcessManager mgr, IProcessRunner processRunner, IFileSystem fileSystem, EAgentConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Services

### Class: EBackgroundExecToolTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Services, ECAssistant.Core.Tools.Background

### Class: ECodeEditorTool
> Code Editor Tool — surgical code edits with diff preview, multi-line replacement,
Implements: EToolBase
Constructor:
  - ECodeEditorTool(IFileSystem fileSystem, EAgentConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: ECodeEditorToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Code

### Class: EColor
> ANSI color codes. Used ONLY by ConsoleUiRenderer (the UI bridge).

### Class: EContextAnalyzer
> Cross-File Context Analyzer — scans the project directory, builds file relationships,
Implements: IDisposable
Constructor:
  - EContextAnalyzer(string projectRoot)
Cross-package deps: ECAssistant.Core

### Class: EContextAnalyzerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Analysis

### Class: EDecisionLoop
> Interactive Decision Loop — lets the agent ask clarifying questions,
Implements: IDisposable
Constructor:
  - EDecisionLoop(EAgentEngine engine, ISessionOutput? sessionOutput = null)
Cross-package deps: ECAssistant.Core.Session

### Class: EDecisionLoopTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: EDotnetBuildTool
> .NET build/test tool.
Implements: EToolBase
Constructor:
  - EDotnetBuildTool(IProcessRunner processRunner, EAgentConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EDotnetBuildToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Build

### Class: EFileAnalyzer
> EXAMPLE TOOL — Extends EToolBase to show how to add a new tool.
Implements: EToolBase
Constructor:
  - EFileAnalyzer(string workingDir)
Cross-package deps: ECAssistant.Core.Tools, ECAssistant.Core

### Class: EFileAnalyzerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Tools.Example

### Class: EFileReaderTool
> EFileReader — read file contents with offset/limit/token-budget control.
Implements: EToolBase
Constructor:
  - EFileReaderTool(IFileSystem fileSystem, EAgentConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EFileReaderToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Reader

### Class: EFileResearchTool
> EFileResearchTool — scan project files, read content for LLM analysis.
Implements: EToolBase
Constructor:
  - EFileResearchTool(IFileSystem fileSystem, EAgentConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EFileResearchToolTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Research

### Class: EGitTool
> Git Integration Tool — wraps common git operations with structured output.
Implements: EToolBase
Constructor:
  - EGitTool(IProcessRunner processRunner, IFileSystem fileSystem, EAgentConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EGitToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Git

### Class: EGuiBase
> Abstract base for ALL console / I/O interaction points.

### Class: EGuiTestHarness
> Non-interactive test harness for EGuiBase.
Implements: EGuiBase
Cross-package deps: ECAssistant.Core.UI

### Class: EGuiTestHarnessTests
> Tests for EGuiTestHarness — verifies it captures output correctly.
Cross-package deps: ECAssistant.Core.Testing, ECAssistant.Core.UI

### Class: EMemoryManager
> Persistent Memory Manager - gives the agent long-term memory across sessions.
Implements: IDisposable
Constructor:
  - EMemoryManager(string? dataPath = null)

### Class: EMemoryManagerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory

### Class: EShellAgent
> Shell Agent Tool — the primary tool for all file and system operations.
Implements: EToolBase
Constructor:
  - EShellAgent(IProcessRunner processRunner, EAgentConfig config, string workingDirectory)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EShellAgentTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Shell

### Class: ESubAgentTool
> Sub-Agent Spawn Tool — allows the main agent to spawn isolated sub-agents
Implements: EToolBase
Constructor:
  - ESubAgentTool(SubAgentManager manager, string defaultWorkingDir)
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration

### Class: EToolBase
> Base class for all Tools.
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Session

### Class: EToolBaseTests
> Concrete subclass for testing EToolBase abstract members.
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Tools

### Class: EToolResult
> Standardized tool call result that flows from any Tool back to the Agent.

### Class: EWebFetchTool
> EWebFetch — fetch a URL, extract main readable content, convert to
Implements: EToolBase
Constructor:
  - EWebFetchTool(IHttpClient httpClient, IReadableContentExtractor contentExtractor, IHtmlTextConverter htmlConverter, EAgentConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EWebFetchToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Web

### Class: EWebSearchTool
> Web Search Tool — lets the LLM search the web using Bing search results.
Implements: EToolBase
Constructor:
  - EWebSearchTool(IHttpClient httpClient, EAgentConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EWebSearchToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Web

### Class: EcaCompositionRoot
> Central composition root for ECAssistant services.
Constructor:
  - EcaCompositionRoot(string userConfigDir, string[] args)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: EcaTestSuite
> Predefined test scenarios for ECAssistant.
Cross-package deps: ECAssistant.Core.Orchestration

### Class: EmbeddingConfig
> Configuration for the embedding model used by vector memory.

### Class: EmbeddingRoutingTests
> Embedding routing: embedding.mode is independent of the main LLM mode.
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Setup, Xunit

### Class: EmbeddingSetupWriter
> Persists the user's embeddings choice from first-run/install into appsettings.json:
Constructor:
  - EmbeddingSetupWriter(string appsettingsPath)

### Class: ExecutionLifecycleState
> Execution lifecycle state for the main agent loop (CTS, ESC flag, turn counter).

### Class: ExecutionPlan
> An execution plan — the output of the mapping phase.

### Class: ExecutionState
> v10.30: Core engine. All inference + KV cache control is HTTP-based via the
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Memory, ECAssistant.Core.Engine, ECAssistant.Core.Session, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Tools, ECAssistant.Core.Transport

### Class: FailureAnalysis

### Class: FailureAnalysisTests
Cross-package deps: ECAssistant.Core.Engine

### Class: FailureEntry

### Class: FailureEntryTests
Cross-package deps: ECAssistant.Core.Engine

### Class: FailurePatternTests
Cross-package deps: ECAssistant.Core.Engine

### Class: FakeLlmServer
> Minimal fake of the ECAssistantLLM endpoints used by LlmServerClient:
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services.Http

### Class: FileChange

### Class: FileContext

### Class: FileInfoData

### Class: FileRelationship

### Class: FileSnapshot

### Class: FileSystemAdapter
> Concrete file system implementation.
Implements: IFileSystem
Cross-package deps: ECAssistant.Core.Interfaces

### Class: FileSystemAdapterTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: FileWatcherService
> File Watcher — monitors the workspace directory for changes and raises events.
Implements: IDisposable
Constructor:
  - FileWatcherService(string watchPath, string filter = "*.*", ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: FirstRunDetector
> First-run / installed-model state.
Constructor:
  - FirstRunDetector(string modelsDir, string serverConfigPath)

### Class: FirstRunStatus
> First-run / installed-model state.

### Class: HomeController
Cross-package deps: ECAssistant.Core.Analysis

### Class: HtmlTextConverter
> Converts HTML to plain text while preserving block-level structure.
Implements: IHtmlTextConverter
Cross-package deps: ECAssistant.Core.Interfaces

### Class: HtmlTextConverterTests
Cross-package deps: ECAssistant.Core.Services

### Class: HttpClientAdapter
> Concrete HTTP client implementation with browser-like default headers
Implements: IHttpClient, IDisposable
Cross-package deps: ECAssistant.Core.Interfaces

### Class: HttpClientAdapterTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: HttpEmbedder
> HTTP-based embedder. Calls /v1/embeddings on the server.
Implements: IVectorEmbedder
Constructor:
  - HttpEmbedder(OpenAIClient client, string modelId = "embeddings")
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Transport

### Class: HttpStreamingEngine
> HTTP-based inference engine. Talks to ECAssistantLLM (or any OpenAI-compatible endpoint).
Implements: IInferenceEngine
Constructor:
  - HttpStreamingEngine(OpenAIClient client, string defaultModelId = "main", string? defaultSessionId = null)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Transport

### Class: ImageAttachmentParser
> Parses <c>[image:&lt;path&gt;]</c> attachment tokens out of user input.

### Class: ImageAttachmentParserTests
> [image:path] attachment extraction — file resolution, mime mapping, error tolerance.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Engine, Xunit

### Class: InMemoryVectorStore
> In-memory vector store implementation.
Implements: IVectorStore
Cross-package deps: ECAssistant.Core.Interfaces

### Class: InMemoryVectorStoreTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: InferenceConfig

### Class: InferenceParamsFactory
> Factory for creating InferenceRequestParams from EAgentConfig.
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: InferenceRequestParams
> Abstracts LLM inference via HTTP (OpenAI-compatible endpoint).

### Class: InstallerVisionEmbeddingTests
> Vision-capability + embeddings-mode wiring: mmproj pairing, vision_enabled flag,
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup

### Class: InterfaceConfig
> UI and output configuration. Verbose/silent controls token stream visibility.

### Class: KvCacheStatus
> KV cache control over HTTP. Replaces direct LLamaSharp executor state management.

### Class: LLMDecision
> LLM's structured decision about what to do next.
Constructor:
  - LLMDecision(bool wantsToolCall, string? toolName, Dictionary<string, string?> args, string? answerText = null, List<ToolCallRequest> toolCalls)
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools

### Class: LlmConfig
> LLM configuration. Model loading params (gpu_layers, batch_size, threads) live in

### Class: LlmProviderConfig
> LLM provider configuration. Supports two modes:

### Class: LlmProviderRegistry
> Resolves the "llm_providers" config section into concrete RemoteProvider records.
Implements: ILlmProviderRegistry
Constructor:
  - LlmProviderRegistry(MultiLlmProvidersConfig? section, ILogger? logger = null, ISecureKeyStore? keyStore = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: LlmProviderRegistryTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services

### Class: LlmServerClient
> Manages client lifecycle with ECAssistantLLM server.
Implements: ILlmServerClient
Constructor:
  - LlmServerClient(string endpoint, string? clientId = null, int maxHeartbeatFailures = DefaultMaxFailures, ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Transport

### Class: LlmServerClientReconnectTests
> Minimal fake of the ECAssistantLLM endpoints used by LlmServerClient:
Cross-package deps: ECAssistant.Core.Services.Http

### Class: LlmServerEndpointConfig
> Core-side config for connecting to ECAssistantLLM server.

### Class: Logger
> Lightweight structured logger — writes to file only.
Implements: ILogger
Constructor:
  - Logger(string logFilePath, LogLevel minLevel = LogLevel.Info)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: LoggerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, ECAssistant.Core.UI, Moq

### Class: MemoryConfig

### Class: MemoryEntry

### Class: MemoryIntegrationTests
> Integration tests for the memory pipeline — EMemoryManager and VectorMemoryStore
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory, ECAssistant.Core.Services

### Class: MemoryService
> Persistent memory service with vector search.
Implements: IMemoryService
Constructor:
  - MemoryService(IFileSystem fileSystem, IVectorStore vectorStore, IConfigProvider configProvider, IVectorEmbedder embedder)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: MemoryServiceTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: MockEngine
> Mock engine for testing — no real model loaded. Returns pre-queued responses.
Implements: EAgentEngine
Constructor:
  - MockEngine(Queue<string> responses, int maxIterations = 5, bool stopAfterFirstTool = false, string? workingDir = null, ISessionOutput? sessionOutput = null, string? workingDir = null, ISessionOutput? sessionOutput = null, bool cycleResponses = false)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Session

### Class: MockSubAgentTool
> Integration tests for sub-agent spawning through the orchestrator.
Implements: EToolBase
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.UI

### Class: ModelCatalogDocument
> Root document for model-catalog.json. Lives in the app root dir;

### Class: ModelCatalogEntry
> Model category — drives config generation and UI grouping.

### Class: ModelCatalogTests
> Catalog loading, default generation, validation, first-run detection.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup, Xunit

### Class: ModelInstallerConfigTests
> Config merge behaviour — mmproj wiring, id replacement, config preservation.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup, Xunit

### Class: ModelInstallerService
> Progress callback payload for a running download.
Constructor:
  - ModelInstallerService(HttpClient http, string modelsDir, string serverConfigPath, string? appsettingsPath = null)

### Class: ModelLoadException
> Exception thrown when model loading or context creation fails.
Implements: Exception
Constructor:
  - ModelLoadException(ModelLoadPhase phase, string modelPath, int gpuLayers, uint contextSize, string message, Exception? inner = null)

### Class: ModelParamValidator
> Pre-flight validation for model loading parameters.
Implements: IModelParamValidator
Constructor:
  - ModelParamValidator(ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: MultiLlmProvidersConfig
> A single remote OpenAI-compatible provider entry.

### Class: MyTests
Cross-package deps: ECAssistant.Core.Analysis

### Class: NopKvCacheController
> No-op KV cache controller for remote mode (cloud API).
Implements: IKvCacheController
Cross-package deps: ECAssistant.Core.Interfaces

### Class: OpenAIClient
> HttpClient wrapper for OpenAI-compatible API calls.
Implements: IDisposable
Constructor:
  - OpenAIClient(string baseUrl, string? clientId = null, string? apiKey = null, TimeSpan? timeout = null)

### Class: OrchestratorIntegrationTests
> Integration tests for the full Orchestrator → Engine → Tools → Output pipeline.
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: OrchestratorResult
> Result from the orchestrator after execution completes.

### Class: OutputEntry
> "stream" (accumulated tokens) or "line" (a discrete line)

### Class: ParallelToolExecutor
> Executes dependency-ordered tool call groups in parallel.
Implements: IParallelToolExecutor
Constructor:
  - ParallelToolExecutor(EAgentEngine engine, ECAssistant.Core.Tools.ToolPolicy toolPolicy, Func<string, Dictionary<string, string?>, Task<EToolResult>> executeToolFn, Action<string>? log = null, ISessionOutput? sessionOutput = null)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Tools, ECAssistant.Core.Session

### Class: ParallelToolExecutorIntegrationTests
> Integration tests for ParallelToolExecutor — dependency analysis and parallel
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ParallelToolExecutorTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools, Moq

### Class: PlannedToolCall
> A single planned tool call — concrete mapping from a sub-task to a tool + args.

### Class: PrefixCachedExtractor
> One-shot LLM extraction with a persistent KV cache via HTTP.
Implements: IAsyncDisposable
Constructor:
  - PrefixCachedExtractor(IInferenceEngine engine, IKvCacheController kvCache, string sessionId, InferenceParamsFactory? paramsFactory = null)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Services

### Class: ProcessRunner
> Concrete process execution implementation.
Implements: IProcessRunner
Cross-package deps: ECAssistant.Core.Interfaces

### Class: ProcessRunnerTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ProgramGuiCollection
> xUnit test collection that serializes tests sharing the static EGuiTestHarness field.

### Class: ProjectArchitecture

### Class: ProjectContext

### Class: ProjectContextManager
> Project Context Manager — maintains persistent knowledge about the project structure,
Implements: IDisposable
Constructor:
  - ProjectContextManager(string workingDir, ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ProjectContextManagerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: ProjectRelationship

### Class: ReadableContentExtractor
> Extracts the main readable content from a full HTML page.
Implements: IReadableContentExtractor
Cross-package deps: ECAssistant.Core.Interfaces

### Class: ReadableContentExtractorTests
Cross-package deps: ECAssistant.Core.Services

### Class: RemoteKvCacheController
> HTTP-based KV cache controller. Manages server-side sessions (prefill, rewind, save, reset)
Implements: IKvCacheController
Constructor:
  - RemoteKvCacheController(OpenAIClient client)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Transport

### Class: RemoteModelInfo
> Remote model loading via HTTP. Replaces the old LLamaSharp-based IModelLoader.

### Class: RemoteModelLoadOptions
> Remote model loading via HTTP. Replaces the old LLamaSharp-based IModelLoader.

### Class: RemoteModelLoader
> HTTP-based model loader. Calls /eca/models endpoints on the server.
Implements: IModelLoader
Constructor:
  - RemoteModelLoader(OpenAIClient client)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Transport

### Class: RemoteProviderConfig
> A single remote OpenAI-compatible provider entry.

### Class: RemoteProviderSetupWriter
> Writes remote (OpenAI-compatible) provider settings chosen during first-run
Constructor:
  - RemoteProviderSetupWriter(string appsettingsPath, string? keysDirectory = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services

### Class: RemoteProviderSetupWriterTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup

### Class: RemoteTokenizer
> HTTP-based tokenizer. Calls /eca/tokenize on the server for accurate token counting.
Constructor:
  - RemoteTokenizer(OpenAIClient client, string modelId = "main")
Cross-package deps: ECAssistant.Core.Transport

### Class: ResourceLoader
> Loads embedded resources from the Core DLL.

### Class: SamplingConfig

### Class: SecureKeyStore
> Cross-platform encrypted-at-rest API key store.
Implements: ISecureKeyStore
Constructor:
  - SecureKeyStore(string directory, ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: SecureKeyStoreTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services

### Class: SelfCorrectionManager
> Self-Correction Manager — detects failure loops, manages file rollback snapshots,
Implements: IDisposable
Constructor:
  - SelfCorrectionManager(string workingDir, ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: SelfCorrectionManagerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: ServerLauncher
> Detects if ECAssistantLLM server is running. If not, launches it as a child process.
Constructor:
  - ServerLauncher(LlmProviderConfig config, string appRoot)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Transport

### Class: SessionBuilder
> Builder for creating and initializing AgentSessions with standard tools.
Implements: ISessionBuilder
Constructor:
  - SessionBuilder(EAgentConfig config, string workingDir, string userConfigDir, ILogger? logger = null, BackgroundProcessManager? bgManager = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Services, ECAssistant.Core.Session, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Background, ECAssistant.Core.Tools.Build, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Git, ECAssistant.Core.Tools.Reader, ECAssistant.Core.Tools.Research, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Web, ECAssistant.Core.Interfaces, ECAssistant.Core.Services.Http, ECAssistant.Core.Transport

### Class: SessionDiscovery
> Discovers existing sessions on disk and determines which one to load as active.

### Class: SessionDiscoveryTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Session

### Class: SessionManagementIntegrationTests
> Integration tests for SessionDiscovery — exercises real file system I/O
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Session

### Class: SessionManager
> Session Manager — creates, tracks, and manages all sessions.
Implements: IAsyncDisposable
Constructor:
  - SessionManager(EAgentConfig config, string resolvedModelPath, string workingDir, ILogger? logger = null, Func<string, string?, string?, OpenAIClient>? openAIClientFactory = null, Func<LlmProviderConfig, string, ServerLauncher>? serverLauncherFactory = null, LlmServerClient? serverClient = null, SecureKeyStore? keyStore = null, ILlmProviderRegistry? providerRegistry = null, IModelParamValidator? modelParamValidator = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Interfaces, ECAssistant.Core.Transport

### Class: SessionQueueTests
> Tests for session prompt queue logic and run state transitions.
Cross-package deps: ECAssistant.Core.Session

### Class: SingleToolResult
> Result of a single tool execution within a batch.
Cross-package deps: ECAssistant.Core.Tools

### Class: SseParser
> Parses SSE (Server-Sent Events) stream from OpenAI-compatible chat completions.

### Class: StepMapper
> Step Mapper — takes decomposed sub-tasks and maps them to concrete tool calls.
Implements: IStepMapper
Constructor:
  - StepMapper(IEngineToolContext engine, ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: StepMapperTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools

### Class: StringUtil
> String utility — truncation and text helpers.

### Class: SubAgentConfig
> Sub-agent configuration. GPU/thread params are server-side concerns

### Class: SubAgentError
Cross-package deps: ECAssistant.Core.Orchestration

### Class: SubAgentErrorTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration

### Class: SubAgentIntegrationTests
> Integration tests for sub-agent spawning through the orchestrator.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.UI

### Class: SubAgentManager
> Sub-agent task definition — what the main agent wants a sub-agent to do.
Implements: IDisposable
Constructor:
  - SubAgentManager(ISubAgentEngineHost mainEngine, string mainWorkingDir = "", ILogger? logger = null, ISessionOutput? sessionOutput = null, EAgentConfig? config = null, ECAssistant.Core.Interfaces.IProcessRunner? processRunner = null, ECAssistant.Core.Interfaces.IFileSystem? fileSystem = null, ECAssistant.Core.Interfaces.IHttpClient? httpClient = null, Services.BackgroundProcessManager? bgManager = null)
Cross-package deps: ECAssistant.Core.Session, ECAssistant.Core.Config, ECAssistant.Core.Tools, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: SubAgentResult

### Class: SubAgentResultTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration

### Class: SubAgentTask

### Class: SubAgentTaskTests
Cross-package deps: ECAssistant.Core.Engine

### Class: SubTask

### Class: SummarizeConfig
> Summarization settings. When use_llm is true, uses HTTP streaming inference

### Class: SummaryService
> Handles LLM-based summarization of old conversation context.
Constructor:
  - SummaryService(Func<string, Task<string>>? generateAsync = null, Func<string, Task<string>>? warmSessionGenerateAsync = null)
Cross-package deps: ECAssistant.Core.Engine

### Class: SummaryServiceTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Engine

### Class: SupportsVisionTests
> SupportsVision is the single, mode-independent capability answer for
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, Xunit

### Class: SystemPromptBuilder
> Builds a system prompt for ECAssistant.Core that includes the required

### Class: SystemToolConfigEntry
> Config entry for system-critical tools.

### Class: TaskPlanner
> Task Planner — breaks complex requests into sub-tasks, tracks progress, and adapts.
Implements: ITaskPlanner
Constructor:
  - TaskPlanner(ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: TaskPlannerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: TerminalAdapter
> Concrete terminal I/O implementation.
Implements: ITerminal
Cross-package deps: ECAssistant.Core.Interfaces

### Class: Test
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Research

### Class: Test
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Research

### Class: TestContext
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration

### Class: TestResult

### Class: TestRunner
> Automated test runner for ECAssistant.
Implements: IAsyncDisposable
Constructor:
  - TestRunner(string modelPath, string? testRootDir = null, ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Research, ECAssistant.Core.Tools.Background, ECAssistant.Core.Tools.Web, ECAssistant.Core.Tools.Build, ECAssistant.Core.Tools.Git, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.Analysis, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Interfaces, ECAssistant.Core.Transport, ECAssistant.Core.UI

### Class: TestScenario
> Scripted inputs for approval prompts (e.g., "y" to approve).
Cross-package deps: ECAssistant.Core.Orchestration

### Class: TestSessionOutput
> Test implementation of ISessionOutput.
Implements: ISessionOutput
Constructor:
  - TestSessionOutput(EGuiTestHarness gui)
Cross-package deps: ECAssistant.Core.Session

### Class: TfidfEmbedder
> TF-IDF text embedding implementation.
Implements: IVectorEmbedder
Cross-package deps: ECAssistant.Core.Interfaces

### Class: TfidfEmbedderTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: TokenCounter
> Token counting via RemoteTokenizer (HTTP /eca/tokenize endpoint).
Constructor:
  - TokenCounter(RemoteTokenizer? tokenizer = null)
Cross-package deps: ECAssistant.Core.Services.Http

### Class: TokenCounterTests
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolCallRequest
> A single parsed tool call request from the LLM response.
Cross-package deps: ECAssistant.Core

### Class: ToolCallRequestTests
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolCallResult

### Class: ToolDependencyAnalyzer
> Analyzes a batch of toolcalls and determines which can run in parallel
Cross-package deps: ECAssistant.Core.Tools

### Class: ToolDependencyAnalyzerTests
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolPermission
> Permission rule for a single tool.

### Class: ToolPermissionConfigEntry
> Config entry for tool permissions in appsettings.json.

### Class: ToolPipelineIntegrationTests
> Integration tests for tools working through the full pipeline:
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ToolPolicy
> Tool policy manager — checks if a tool requires user approval before execution.

### Class: ToolPolicyDecision
> Result of a tool policy check.

### Class: ToolPolicyTests
Cross-package deps: ECAssistant.Core.Tools

### Class: TranscriptIntegrationTests
> Integration tests for ConversationTranscript persistence —
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Engine

### Class: TranscriptMessage
> A single message in the conversation transcript.

### Class: VectorEntry

### Class: VectorMemoryConfig

### Class: VectorMemorySetupWriter
> Persists the user's vector-memory choice from first-run/install into
Constructor:
  - VectorMemorySetupWriter(string appsettingsPath)

### Class: VectorMemorySetupWriterTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup

### Class: VectorMemoryStore
> Vector Memory Store — semantic search over memory entries using embeddings.
Implements: IDisposable
Constructor:
  - VectorMemoryStore(string storeDir, ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: VectorMemoryStoreTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory, ECAssistant.Core.Services

### Class: VectorSearchResult

### Class: WorkspaceConfig

### Record: BuildError
> Parser for .NET build output — extracts errors and warnings.
Constructor:
  - BuildError(string File, int Line, string Code, string Message)

### Record: DownloadProgress
> Progress callback payload for a running download.
Constructor:
  - DownloadProgress(string Filename, long BytesReceived, long? TotalBytes, double Percent, double MbPerSecond)

### Record: EcaServiceBundle
> Bundle of all wired services returned by EcaCompositionRoot.Build().
Constructor:
  - EcaServiceBundle(EAgentConfig Config, string ModelPath, string WorkingDirectory, string UserConfigDirectory, ILogger Logger, BackgroundProcessManager BackgroundProcesses, FileWatcherService FileWatcher, ISessionBuilder SessionBuilder)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Record: GenerationParams
Constructor:
  - GenerationParams(int MaxTokens, float Temperature, float TopP, int TopK, float RepeatPenalty)

### Record: ImageRef
> Parses <c>[image:&lt;path&gt;]</c> attachment tokens out of user input.
Constructor:
  - ImageRef(string OriginalPath, string FullPath, string DataUri)

### Record: InstallResult
> Progress callback payload for a running download.
Constructor:
  - InstallResult(bool Success, string Message, IReadOnlyList<string> DownloadedFiles)

### Record: MemoryEntry
Constructor:
  - MemoryEntry(string Content, string Metadata, float[]? Embedding = null)

### Record: ProcessResult
Constructor:
  - ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)

### Record: RemoteProvider
> A fully-resolved remote provider ready for engine construction.
Constructor:
  - RemoteProvider(string Name, string Endpoint, string? ApiKey, string ModelId, string? EmbeddingModelId)

### Record: TranscriptMessage
Constructor:
  - TranscriptMessage(string Role, string Content, string? ToolCallId = null)

### Record: VectorResult
Constructor:
  - VectorResult(string Content, string Metadata, float Score)
