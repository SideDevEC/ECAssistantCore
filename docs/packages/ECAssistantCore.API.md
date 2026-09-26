# ECAssistantCore.API.md

Types: 415  |  LOC: 34825  |  ~20324 tokens

---

### Interface: IAiSetupResetter
> Resets all AI setup state back to first-run defaults: clears configured
Methods:
  - void Reset(string userConfigDir)
Cross-package deps: ECAssistant.Core.Setup

### Interface: IConfigLoader
> Interface for loading AppConfig from JSON files.
Methods:
  - AppConfig Load(string filePath = "appsettings.json")
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

### Interface: IContextPinner
> One pinned fact surfaced to the model after compaction.
Methods:
  - void SetGoal(string userRequest)
  - void ObserveUserMessage(string content)
  - void ObserveToolOutput(string toolName, string output)
  - string? BuildPinnedBlock(bool isLargeTier, int maxChars)

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

### Interface: IFirstRunOrchestrator
> Unified first-run / reinstall orchestration seam, shared by ALL hosts
Properties:
  - string LlmRoot { get; set; }
  - string ServerConfigPath { get; set; }
  - string ServerBinaryPath { get; set; }
Methods:
  - Task RunIfNeededAsync()

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
  - Task<string?> GenerateStructuredAsync(string prompt,
        InferenceRequestParams parameters,
        CancellationToken ct = default)

### Interface: IKvCacheController
> KV cache control over HTTP. Replaces direct LLamaSharp executor state management.
Methods:
  - Task<bool> CreateSessionAsync(string sessionId, string? modelId = null, CancellationToken ct = default)
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
  - Task<bool> DisconnectAsync(CancellationToken ct = default)

### Interface: ILogger
> Structured logging interface — file only, headless.
Properties:
  - bool IsDebugEnabled { get; set; }
  - string LogFilePath { get; set; }
  - long LogFileSize { get; set; }
Methods:
  - void Initialize(string logFilePath, LogLevel minLevel, Func<string, bool>? componentFilter = null)
  - void SetLevel(LogLevel level)
  - void Debug(string tag, string message)
  - void Info(string tag, string message)
  - void Warn(string tag, string message)
  - void Error(string tag, string message)
  - void Error(string tag, string message, Exception ex)
  - string GetRecentLines(int count)
Cross-package deps: ECAssistant.Core.Services

### Interface: IMcpClient
> Client for communicating with an MCP (Model Context Protocol) server.
Implements: IAsyncDisposable
Properties:
  - string ServerName { get; set; }
Methods:
  - Task<McpServerInfo> InitializeAsync(CancellationToken ct = default)
  - Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken ct = default)
  - Task<McpToolResult> CallToolAsync(string name, string jsonArguments, CancellationToken ct = default)

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
  - ModelLoadException? Validate(AppConfig config, string resolvedModelPath)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine

### Interface: IOutputListener
> Listener interface for session output.
Methods:
  - void OnOutput(string text, OutputState state)
  - void OnStreamStart()
  - void OnStreamStop()
  - bool OnRequestApproval(string message)
  - ApprovalScope OnRequestApprovalScoped(string message)
  - int? OnRequestChoice(string prompt, IReadOnlyList<string> options)

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

### Interface: IPdfPageRenderer
> Renders one page of a PDF document to PNG bytes so it can be sent through
Methods:
  - Task<byte[]?> RenderPageToPngAsync(string pdfPath, int page, CancellationToken ct = default)

### Interface: IPlaybookExtractor
> v14.14: Deterministic playbook extraction from a completed run's successful
Methods:
  - Playbook? Extract(string goal, IReadOnlyList<CapturedToolCall> successfulCalls, string source = "goal")

### Interface: IPlaybookStore
> v14.14: Storage for persistent success playbooks. Owns dedup, usage counters,
Properties:
  - IReadOnlyList<Playbook> All { get; set; }
Methods:
  - Task<Playbook> CaptureAsync(Playbook candidate)
  - IReadOnlyList<Playbook> Match(string userRequest, int topN)
  - string? BuildInjection(string userRequest, bool isLargeTier, int topN = 2, int maxChars = 1500)

### Interface: IPostEditVerifier
> v14.13: Tier-aware post-edit verification gate. Classifies file-modifying tool
Methods:
  - bool IsFileModifyingCall(string toolName, IReadOnlyDictionary<string, string?> args)
  - bool ShouldVerify(string toolName, IReadOnlyDictionary<string, string?> args, bool isLargeTier)
  - int MaxRounds(bool isLargeTier)
  - string BuildFailureFeedback(int round, int maxRounds, VerificationResult result)
  - string BuildSuccessNote()
  - Task<VerificationResult> VerifyAsync(string? workingDir, CancellationToken ct = default)

### Interface: IProcessRunner
> Abstract process execution.
Methods:
  - Task<ProcessResult> ExecuteAsync(string command, string? workDir = null, CancellationToken ct = default)

### Interface: IRemoteModelProbe
> A model advertised by a remote OpenAI-compatible /models endpoint.
Methods:
  - Task<RemoteProbeResult> ProbeAsync(string endpoint, string? apiKey, CancellationToken ct = default)

### Interface: ISecureKeyStore
> Cross-platform self-encrypting API key store.
Properties:
  - string KeysDirectory { get; set; }
Methods:
  - string GetKey(string fileName)
  - void SetKey(string fileName, string plaintext)

### Interface: IServerInstallCoordinator
> Interactive LLM-server install seam. Decides WHEN the server runtime must be
Methods:
  - Task<bool> EnsureServerAsync(CancellationToken cancellationToken = default)
  - ServerInstallState Inspect()

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
  - MemoryManager Memory { get; set; }
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
  - ApprovalScope RequestApprovalScoped(string message)
  - int? RequestChoice(string prompt, IReadOnlyList<string> options)

### Interface: ISetupUi
> Console I/O abstraction for the setup wizard — enables unit testing of flow logic.
Methods:
  - void WriteLine(string text = "")
  - void Write(string text)
  - string? ReadLine()
  - void WriteLineGreen(string text)

### Interface: ISetupWizard
> Staged first-run installation wizard seam. Each stage only shows what it needs:
Methods:
  - Task RunAsync(WizardContext ctx)

### Interface: IShellSandbox
> macOS Seatbelt (sandbox-exec) wrapper: shell commands run under a generated
Properties:
  - bool IsEnabled { get; set; }
Methods:
  - string Wrap(string command)
Cross-package deps: ECAssistant.Core.Interfaces

### Interface: IShellSession
> A persistent interactive shell session: working directory, environment variables,
Implements: IAsyncDisposable
Properties:
  - string CurrentWorkingDirectory { get; set; }
  - bool IsDead { get; set; }
Methods:
  - Task<ShellCommandResult> RunAsync(string command, CancellationToken ct = default)

### Interface: IShellSessionFactory
> A persistent interactive shell session: working directory, environment variables,
Methods:
  - Task<IShellSession> CreateAsync(string initialWorkingDirectory, CancellationToken ct = default)

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
  - Transport.OpenAIClient? SharedHttpClient { get; set; }
  - bool IsLocalMode { get; set; }

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

### Interface: ITextMatchPipeline
> Match pipeline seam: runs match strategies in order (exact →
Methods:
  - TextMatchResult Find(string content, string searchText)

### Interface: ITextMatchStrategy
> A single text-matching strategy for patch search/replace. Implementations
Properties:
  - string Name { get; set; }
Methods:
  - TextMatchResult Find(string content, string searchText)

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

### Interface: IVerificationRunner
> v14.13: Executes one verification command (build / filtered test) in a working
Methods:
  - Task<VerificationResult> RunAsync(string command, string? workingDir = null, CancellationToken ct = default)

### Class: ActiveSubAgent

### Class: AgentConfig
> Hard cap for a single agent execution run (StartRunner). Default 10 minutes.

### Class: AgentConfigBuilder
> Fluent config builder for library consumers.
Cross-package deps: ECAssistant.Core.Config

### Class: AgentEngine
> v10.30: Core engine. All inference + KV cache control is HTTP-based via the
Implements: IEngine, IEngineToolContext, ISubAgentEngineHost
Constructor:
  - AgentEngine(string sessionId, IInferenceEngine inferenceEngine, IKvCacheController kvCacheController, RemoteTokenizer? tokenizer = null, InferenceRequestParams? inferenceParams = null, uint contextSize = 8192, string modelPath = "", AppConfig? config = null, string? workingDir = null, ILogger? logger = null, MemoryManager? memoryManager = null, ECAssistant.Core.Engine.SelfCorrectionManager? selfCorrection = null, ECAssistant.Core.Playbooks.IPlaybookStore? playbookStore = null, ECAssistant.Core.Engine.ProjectContextManager? projectContext = null, ITaskPlanner? taskPlanner = null, Transport.OpenAIClient? sharedHttpClient = null, bool isLocalMode = true, bool isSpecialistSession = false)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.ContextPinning, ECAssistant.Core.Interfaces, ECAssistant.Core.Memory, ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Session, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Tools, ECAssistant.Core.Transport

### Class: AgentOrchestrator
> Orchestrator — the decision-making brain for multi-step agent workflows.
Implements: IAsyncDisposable
Constructor:
  - AgentOrchestrator(AgentEngine engine, ISessionOutput? sessionOutput = null, int maxTurns = 5, int maxFailures = 3, ECAssistant.Core.Tools.ToolPolicy? toolPolicy = null, ECAssistant.Core.Interfaces.ILogger? logger = null, ECAssistant.Core.Config.AppConfig? config = null, IPostEditVerifier? postEditVerifier = null, IPlaybookStore? playbookStore = null, IPlaybookExtractor? playbookExtractor = null)
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Handoff, ECAssistant.Core.Services, ECAssistant.Core.Session, ECAssistant.Core.Interfaces, ECAssistant.Core.Playbooks

### Class: AgentSession
> A fully isolated agent session.
Implements: ISessionOutput, ISessionContext, IAsyncDisposable
Constructor:
  - AgentSession(string key, string sessionId, string endpoint, string? clientId, InferenceRequestParams inferenceParams, string workingDir, SemaphoreSlim inferenceLock, SubAgentConfig? subAgentConfig = null, string? label = null, ILogger? logger = null, AppConfig? config = null, OpenAIClient? httpClient = null, RemoteTokenizer? remoteTokenizer = null, string? apiKey = null, bool isLocalMode = true)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Memory, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Transport, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Mcp

### Class: AiSetupResetter
> Default <see cref="IAiSetupResetter"/>: rewrites appsettings.json back to the
Implements: IAiSetupResetter
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: AiSetupResetterTests
Cross-package deps: ECAssistant.Core.Setup, ECAssistant.Core.Config

### Class: AllToolsTierPromptsTests
> v15: tier-aware tool prompt enforcement (Emre, 2026-09-23) — the abstract base
Cross-package deps: ECAssistant.Core.Tools

### Class: AnsiColor
> ANSI color codes. Used by ConsoleUiRenderer (the UI bridge) and the setup wizard UIs.

### Class: ApiUserController
Cross-package deps: ECAssistant.Core.Analysis

### Class: AppConfig
> App settings — matches the nested structure in appsettings.json
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Tools

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

### Class: BuildCallSignatureTests
> v12.4/v12.5 regression: tool-call signatures must be deterministic and
Cross-package deps: ECAssistant.Core.Orchestration, Xunit

### Class: BuildErrorParser
> Parser for .NET build output — extracts errors and warnings.

### Class: BuildOutputRendererTests
> v14.20: BuildOutputRenderer — semantic render of dotnet build/test output.
Cross-package deps: ECAssistant.Core.Tools.Build

### Class: CatalogFetcher
> Fetches the model catalog from GitHub at wizard time so model links/availability
Constructor:
  - CatalogFetcher(HttpClient http, HttpClient http, string url)

### Class: CatalogModelFile
> Model category — drives config generation and UI grouping.

### Class: CatalogSuggestedConfig
> Model category — drives config generation and UI grouping.

### Class: ChainExecutionTests
> v14.20: chain execution through the real ParallelToolExecutor — {{N}}
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Session, ECAssistant.Core.Tools

### Class: ConfigDrivenParamsTests
> "Every parameter from the config" (Emre, 2026-09-21): new config keys must
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Engine, ECAssistant.Core.Setup, ECAssistant.Core.Session

### Class: ConfigIntegrationTests
> Integration tests for the config loading pipeline — uses real FileSystemAdapter
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ConfigLoader
> Loads AppConfig from JSON files.
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
  - ConfigProvider(IFileSystem fileSystem, string configPath, IFileSystem fileSystem, AppConfig config)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Config

### Class: ConfigProviderTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: ConsoleSetupUi
> Default <see cref="ISetupUi"/> backed by System.Console.
Implements: ISetupUi

### Class: ContextAnalyzer
> Cross-File Context Analyzer — scans the project directory, builds file relationships,
Implements: IDisposable
Constructor:
  - ContextAnalyzer(string projectRoot)
Cross-package deps: ECAssistant.Core

### Class: ContextManagementConfig
> Context-window fill percentage (0-100) that triggers KV-cache rebuild +

### Class: ContextManager
> Context window management with summary-and-shift strategy.
Implements: IContextManager
Constructor:
  - ContextManager(IInferenceEngine inferenceEngine, IConfigProvider configProvider)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: ContextManagerTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: ContextPinner
> v14.16: deterministic context pinner. Small tier → file map + decisions + goal
Implements: IContextPinner
Constructor:
  - ContextPinner(ContextPinningConfig? config = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: ContextPinningConfig
> v14.16: tier-aware proactive context pinning. Critical state (original user

### Class: ContextPinningMatchers
> v14.16: pure-function matchers for proactive context pinning. Static methods

### Class: ContextPinningTests
> v14.16: tier-aware proactive context pinning — pure logic only (no LLM, no
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.ContextPinning, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces

### Class: ContextWindow
> Manages the LLM conversation context window.
Constructor:
  - ContextWindow(uint maxTokens, TokenCounter? tokenCounter = null, double autoSummarizeThresholdFraction = 0.50, uint maxTokens, SummaryService? summaryService, TokenCounter? tokenCounter = null, double autoSummarizeThresholdFraction = 0.50)
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

### Class: DecomposeConfig
> Task decomposition settings. When use_llm is true, uses HTTP streaming

### Class: DependencyGroup
> A group of toolcalls that can execute in parallel.

### Class: DependencyGroupTests
Cross-package deps: ECAssistant.Core.Engine

### Class: DotnetVerificationRunner
> v14.13: Real verification runner — executes the configured build/test command via
Implements: IVerificationRunner
Constructor:
  - DotnetVerificationRunner(IProcessRunner processRunner)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Services

### Class: EAgentConfigTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config

### Class: EBackgroundExecTool
> Background Exec Tool — lets the LLM start long-running processes
Implements: EToolBase
Constructor:
  - EBackgroundExecTool(BackgroundProcessManager mgr, IProcessRunner processRunner, IFileSystem fileSystem, AppConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Services

### Class: EBackgroundExecToolTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Services, ECAssistant.Core.Tools.Background

### Class: ECodeEditorTool
> Code Editor Tool — surgical code edits with diff preview, multi-line replacement,
Implements: EToolBase
Constructor:
  - ECodeEditorTool(IFileSystem fileSystem, AppConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: ECodeEditorToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Code

### Class: EContextAnalyzerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Analysis

### Class: EDotnetBuildTool
> .NET build/test tool.
Implements: EToolBase
Constructor:
  - EDotnetBuildTool(IProcessRunner processRunner, AppConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EDotnetBuildToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Build

### Class: EFileReaderTool
> EFileReader — read file contents with offset/limit/token-budget control.
Implements: EToolBase
Constructor:
  - EFileReaderTool(IFileSystem fileSystem, AppConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EFileReaderToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Reader

### Class: EFileResearchTool
> EFileResearchTool — scan project files, read content for LLM analysis.
Implements: EToolBase
Constructor:
  - EFileResearchTool(IFileSystem fileSystem, AppConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EFileResearchToolTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Research

### Class: EGitTool
> Git Integration Tool — wraps common git operations with structured output.
Implements: EToolBase
Constructor:
  - EGitTool(IProcessRunner processRunner, IFileSystem fileSystem, AppConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: EGitToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Git

### Class: EHandoffTool
> Handoff tool — allows the main agent to delegate the entire remaining task
Implements: EToolBase
Constructor:
  - EHandoffTool(Func<HandoffRequest, CancellationToken, Task<OrchestratorResult>> executeHandoff)
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration

### Class: EHandoffToolTests
> Unit tests for EHandoffTool — the model-facing handoff tool.
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Handoff

### Class: EMemoryManagerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory

### Class: EShellAgent
> Shell Agent Tool — the primary tool for all file and system operations.
Implements: EToolBase
Constructor:
  - EShellAgent(IProcessRunner processRunner, AppConfig config, string workingDirectory, IShellSessionFactory? sessionFactory = null, bool isLargeTier = false, IShellSandbox? sandbox = null)
Cross-package deps: ECAssistant.Core.Tools.Build, ECAssistant.Core.Config, ECAssistant.Core.Services.Shell, ECAssistant.Core.Interfaces

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

### Class: EToolBaseTierPromptsTests
> v15: tier-aware tool prompt enforcement (Emre, 2026-09-23) — the abstract base
Cross-package deps: ECAssistant.Core.Tools

### Class: EToolResult
> Standardized tool call result that flows from any Tool back to the Agent.

### Class: EToolResultImagesTests
Cross-package deps: ECAssistant.Core.Tools

### Class: EUserAskTool
> v14.9 ambiguity-triggered checkpoint: lets the MODEL declare uncertainty and ask
Implements: EToolBase
Constructor:
  - EUserAskTool(ISessionOutput? sessionOutput)
Cross-package deps: ECAssistant.Core.Session

### Class: EUserAskToolTests
> v14.9 AskUser tool — model-driven ambiguity checkpoint: parses options,
Cross-package deps: ECAssistant.Core.Session, ECAssistant.Core.Tools.User, Xunit

### Class: EVisionStructureTool
> EVisionStructure — analyze an image file or PDF page and return a fixed,
Implements: EToolBase
Constructor:
  - EVisionStructureTool(IInferenceEngine inferenceEngine, IPdfPageRenderer pdfRenderer, AppConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Vision

### Class: EVisionStructureToolTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.EVision, ECAssistant.Core.Vision, Moq

### Class: EcaCompositionRoot
> Central composition root for ECAssistant services.
Constructor:
  - EcaCompositionRoot(string userConfigDir, string[] args)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: EmbeddingConfig
> Configuration for the embedding model used by vector memory.

### Class: EmbeddingRoutingTests
> Embedding routing: embedding.mode is independent of the main LLM mode.
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Setup, Xunit

### Class: EmbeddingSetupWriter
> Persists the user's embeddings choice from first-run/install into appsettings.json:
Constructor:
  - EmbeddingSetupWriter(string appsettingsPath)

### Class: EndpointNormalizer
> Normalizes OpenAI-compatible base URLs so the rest of the engine can safely

### Class: EndpointNormalizerTests
> Tests for base-URL normalization (strip trailing "/v1") and the remote
Cross-package deps: ECAssistant.Core.Setup, ECAssistant.Core.Transport

### Class: EngineTierBehaviorTests
> v14.12: model-tier-adaptive behavior tests at the ENGINE level — verifies the
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Services, ECAssistant.Core.Tools, ECAssistant.TestSupport

### Class: ExactMatchStrategy
> Priority 1: exact substring match — the pre-v14.15 behavior, unchanged.
Implements: ITextMatchStrategy

### Class: ExecutionLifecycleState
> Execution lifecycle state for the main agent loop (CTS, ESC flag, turn counter).

### Class: ExecutionPlan
> An execution plan — the output of the mapping phase.

### Class: ExecutionState
> v10.30: Core engine. All inference + KV cache control is HTTP-based via the
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.ContextPinning, ECAssistant.Core.Interfaces, ECAssistant.Core.Memory, ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Session, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Tools, ECAssistant.Core.Transport

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
  - FirstRunDetector(string modelsDir, string serverConfigPath, string modelsDir, string serverConfigPath, string? serverBinaryPath, string? appsettingsPath = null)

### Class: FirstRunDetectorTests
> Tests for FirstRunDetector remote-provider awareness: a configured remote
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup

### Class: FirstRunOrchestrator
> Unified first-run / reinstall orchestration, shared by ALL hosts (Console, TUI):
Implements: IFirstRunOrchestrator
Constructor:
  - FirstRunOrchestrator(string userConfigDir, ISetupUi ui, string userConfigDir, ISetupUi ui, Func<IServerInstallCoordinator>? serverInstallCoordinatorFactory, Func<ISetupWizard>? wizardFactory)
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Setup

### Class: FirstRunStatus
> First-run / installed-model state.

### Class: GuiBase
> Abstract base for ALL console / I/O interaction points.

### Class: GuiTestHarnessTests
> Tests for GuiTestHarness — verifies it captures output correctly.
Cross-package deps: ECAssistant.TestSupport, ECAssistant.Core.UI

### Class: HandoffE2E
> v15 ephemeral handoff — end-to-end against a REAL ECAssistantLLM server with
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Orchestration, ECAssistant.Core.Session, ECAssistant.TestSupport

### Class: HandoffExecutor
> Executes a handoff: creates an isolated specialist engine with the request's
Implements: IAsyncDisposable
Constructor:
  - HandoffExecutor(ISubAgentEngineHost mainEngine, AppConfig config, InferenceRequestParams inferenceParams, string workingDir, ILogger? logger, ISessionOutput? sessionOutput = null, IProcessRunner? processRunner = null, IFileSystem? fileSystem = null, Services.BackgroundProcessManager? bgManager = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Transport

### Class: HandoffIntegrationTests
> Integration tests for the v15 ephemeral handoff: EHandoff tool registration,
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.UI

### Class: HandoffRequestTests
> Unit tests for HandoffRequest — the ephemeral handoff data record.
Cross-package deps: ECAssistant.Core.Engine

### Class: HardwareProfile
> Machine capabilities of the machine running the setup wizard. Used to tune

### Class: HardwareProfileTests
> HardwareProfile tuning rules: GPU layers / context / batch adapt to the machine.
Cross-package deps: ECAssistant.Core.Setup, Xunit

### Class: HarnessE2E
> Harness end-to-end: drives the REAL product stack (AgentSession →
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Session, ECAssistant.Core.Transport, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Build, ECAssistant.TestSupport

### Class: HarnessE2EFeatures
> v14.13–v14.16 feature E2E against a REAL server + real local model
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Session, ECAssistant.Core.Verification, ECAssistant.TestSupport

### Class: HarnessOptimizationTests
> Tests for the 2026-09-21 harness optimizations (P1-P6): tool-result truncation
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.TestSupport

### Class: HomeController
Cross-package deps: ECAssistant.Core.Analysis

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
> Factory for creating InferenceRequestParams from AppConfig.
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: InferenceRequestParams
> Abstracts LLM inference via HTTP (OpenAI-compatible endpoint).

### Class: InstallManifest
> One downloadable asset entry from an install manifest.

### Class: InstallManifestAsset
> One downloadable asset entry from an install manifest.

### Class: InstallManifestRuntime
> One downloadable asset entry from an install manifest.

### Class: InstallerVisionEmbeddingTests
> Vision-capability + embeddings-mode wiring: mmproj pairing, vision_enabled flag,
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup

### Class: InteractionConfig
> v14.9: interactive checkpoint policy — decides WHEN the orchestrator may pause

### Class: InterfaceConfig
> UI and output configuration. Verbose/silent controls token stream visibility.

### Class: JourneySuiteE2EHost
> Opt-in host for the TestSupport JourneySuiteE2E (Emre 2026-09-25): the journey
Implements: JourneySuiteE2E
Cross-package deps: ECAssistant.TestSupport

### Class: JsonEnvelopeFallbackTests
> v14.10.1: when the structured path falls back to text streaming, the model
Cross-package deps: ECAssistant.Core.Engine

### Class: KvCacheStatus
> KV cache control over HTTP. Replaces direct LLamaSharp executor state management.

### Class: LLMDecision
> LLM's structured decision about what to do next.
Constructor:
  - LLMDecision(bool wantsToolCall, string? toolName, Dictionary<string, string?> args, string? answerText = null, string? reasoning = null, List<ToolCallRequest> toolCalls, string? reasoning = null, string? commentary = null)
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools

### Class: LLMDecisionEnvelopeTests
> v14.18: thinking-only decision envelopes (answer empty, no tool calls) must
Cross-package deps: ECAssistant.Core.Orchestration

### Class: LineAnchoredMatchStrategy
> Priority 3: line-anchored match on significant content only — ALL whitespace
Implements: LineMatchStrategyBase

### Class: LineMatchStrategyBase
> Shared machinery for line-based strategies: split content into lines while
Implements: ITextMatchStrategy

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
  - Logger(string logFilePath, LogLevel minLevel = LogLevel.Info, Func<string, bool>? componentFilter = null)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: LoggerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, ECAssistant.Core.UI, Moq

### Class: LoggingConfig
> Logging configuration for ECAssistantCore. Encapsulated — Core uses its own

### Class: McpConfig
> MCP server configuration section in appsettings.json.

### Class: McpConfigTests
Cross-package deps: ECAssistant.Core.Config

### Class: McpHttpSseClient
> MCP client over HTTP/SSE (remote server). Communicates via JSON-RPC 2.0
Implements: IMcpClient
Constructor:
  - McpHttpSseClient(string serverName, McpServerConfig config, ILogger logger, Dictionary<string, string>? resolvedHeaders = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: McpServerConfig
> MCP server configuration section in appsettings.json.

### Class: McpServerRegistrar
> Manages MCP server lifecycle: create clients, initialize, discover tools,
Implements: IAsyncDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Session

### Class: McpServerRegistrarTests
> Invoke the static ApplyToolFilter method via reflection (it's private).
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Mcp

### Class: McpStdioClient
> MCP client over stdio (local subprocess). Spawns the server process,
Implements: IMcpClient
Constructor:
  - McpStdioClient(string serverName, McpServerConfig config, ILogger logger, Dictionary<string, string>? resolvedEnv = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: McpToolAdapter
> Adapter that wraps an MCP tool descriptor as an EToolBase subclass.
Implements: EToolBase
Constructor:
  - McpToolAdapter(IMcpClient client, McpToolDescriptor descriptor)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Session

### Class: McpToolAdapterTests
Cross-package deps: ECAssistant.Core.Tools.Mcp

### Class: MemoryConfig

### Class: MemoryEntry

### Class: MemoryIntegrationTests
> Integration tests for the memory pipeline — MemoryManager and VectorMemoryStore
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory, ECAssistant.Core.Services

### Class: MemoryManager
> Persistent Memory Manager - gives the agent long-term memory across sessions.
Implements: IDisposable
Constructor:
  - MemoryManager(string? dataPath = null)

### Class: MemoryService
> Persistent memory service with vector search.
Implements: IMemoryService
Constructor:
  - MemoryService(IFileSystem fileSystem, IVectorStore vectorStore, IConfigProvider configProvider, IVectorEmbedder embedder)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: MemoryServiceTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: MockProbeTool
> Integration tests for the v15 ephemeral handoff: EHandoff tool registration,
Implements: EToolBase
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.UI

### Class: MockSubAgentTool
> Integration tests for sub-agent spawning through the orchestrator.
Implements: EToolBase
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.UI

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

### Class: ModelTierAutoResolver
> Derives the model tier from the model id/name (Emre's rule, 2026-09-24):

### Class: ModelTierConfig
> v14.12: Model-tier profile — gates how much harness scaffolding (hand-holding

### Class: ModelTierConfigTests
> v14.12: model-tier profile tests — IsLargeRuntime resolution (small/large/default),
Cross-package deps: ECAssistant.Core.Config

### Class: MultiLlmProvidersConfig
> A single remote OpenAI-compatible provider entry.

### Class: MyTests
Cross-package deps: ECAssistant.Core.Analysis

### Class: NativeToolCallsAdapterTests
> v14: remote native tool_calls (OpenAI function calling) → decision envelope → LLMDecision.
Cross-package deps: ECAssistant.Core.Engine, Xunit

### Class: NopKvCacheController
> No-op KV cache controller for remote mode (cloud API).
Implements: IKvCacheController
Cross-package deps: ECAssistant.Core.Interfaces

### Class: NuGetServerFetcher
> Downloads the ECAssistant.LLM.Server NuGet package from nuget.org and extracts the
Constructor:
  - NuGetServerFetcher(HttpClient http, string version, string? tempRoot = null)

### Class: OpenAIClient
> HttpClient wrapper for OpenAI-compatible API calls.
Implements: IDisposable
Constructor:
  - OpenAIClient(string baseUrl, string? clientId = null, string? apiKey = null, TimeSpan? timeout = null)

### Class: OrchestratorIntegrationTests
> Integration tests for the full Orchestrator → Engine → Tools → Output pipeline.
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: OrchestratorResult
> Result from the orchestrator after execution completes.

### Class: OrchestratorV1419Tests
> v14.19.1 unit coverage for the gaps found in the feature audit:
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Orchestration, ECAssistant.Core.Session, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Interfaces, ECAssistant.TestSupport, Moq

### Class: Order
> Harness end-to-end: drives the REAL product stack (AgentSession →
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Session, ECAssistant.Core.Transport, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Build, ECAssistant.TestSupport

### Class: OutputEntry
> "stream" (accumulated tokens) or "line" (a discrete line)

### Class: ParallelToolExecutor
> Executes dependency-ordered tool call groups in parallel.
Implements: IParallelToolExecutor
Constructor:
  - ParallelToolExecutor(AgentEngine engine, ECAssistant.Core.Tools.ToolPolicy toolPolicy, Func<string, Dictionary<string, string?>, Task<EToolResult>> executeToolFn, Action<string>? log = null, ISessionOutput? sessionOutput = null)
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Tools, ECAssistant.Core.Session

### Class: ParallelToolExecutorIntegrationTests
> Integration tests for ParallelToolExecutor — dependency analysis and parallel
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ParallelToolExecutorTests
Cross-package deps: ECAssistant.TestSupport, ECAssistant.Core.Engine, ECAssistant.Core.Tools, Moq

### Class: PathExpander
> String utility — truncation and text helpers.

### Class: PersistentPowerShellSession
> Persistent PowerShell session via one long-lived pwsh process (Windows).
Implements: IShellSession
Cross-package deps: ECAssistant.Core.Interfaces

### Class: PersistentShellSession
> Persistent POSIX shell session via one long-lived zsh/bash process.
Implements: IShellSession
Cross-package deps: ECAssistant.Core.Interfaces

### Class: PersistentShellSessionTests
> v15: persistent shell session — working directory and exported env survive
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services.Shell

### Class: PlannedToolCall
> A single planned tool call — concrete mapping from a sub-task to a tool + args.

### Class: Playbook
> v14.14: A persistent success playbook — a lightweight, deterministic recipe

### Class: PlaybookExtractor
> v14.14: Deterministic playbook extractor — builds a playbook purely from the
Implements: IPlaybookExtractor

### Class: PlaybookMatcher
> v14.14: Pure text helpers for playbook keyword extraction and matching.

### Class: PlaybookStore
> v14.14: JSON-file-backed playbook store. One JSON file per playbook under
Implements: IPlaybookStore
Constructor:
  - PlaybookStore(string workingDir, ILogger? logger = null, int maxPlaybooks = 50)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: PlaybookTests
> v14.14: tier-aware playbook memory. Pure-logic tests — JSON persistence in
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Orchestration, ECAssistant.Core.Playbooks, ECAssistant.Core.Tools, ECAssistant.TestSupport

### Class: PlaybookTitleTests
> v14.19.1: playbook titles use the goal's FIRST SENTENCE only — prompts that
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Playbooks

### Class: PostEditVerifier
> v14.13: Default post-edit verifier. Pure classification/gating logic plus an
Implements: IPostEditVerifier
Constructor:
  - PostEditVerifier(IVerificationRunner runner, VerificationConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: PostEditVerifierTests
> v14.13: tier-aware post-edit verification loop. Pure-logic tests — the build/test
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Tools, ECAssistant.Core.Verification, ECAssistant.TestSupport

### Class: ProcessRunner
> Concrete process execution implementation.
Implements: IProcessRunner
Cross-package deps: ECAssistant.Core.Interfaces

### Class: ProcessRunnerTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ProgramGuiCollection
> xUnit test collection that serializes tests sharing the static GuiTestHarness field.

### Class: ProjectArchitecture

### Class: ProjectContext

### Class: ProjectContextExclusionTests
> Project-context scan must exclude host runtime/config files — the model should
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine

### Class: ProjectContextManager
> Project Context Manager — maintains persistent knowledge about the project structure,
Implements: IDisposable
Constructor:
  - ProjectContextManager(string workingDir, ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ProjectContextManagerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: ProjectRelationship

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

### Class: RemoteModelProbe
> Default <see cref="IRemoteModelProbe"/>: GET {endpoint}/models with optional bearer
Implements: IRemoteModelProbe
Constructor:
  - RemoteModelProbe(HttpClient? httpClient = null)

### Class: RemoteModelProbePathTests
> Tests for base-URL normalization (strip trailing "/v1") and the remote
Cross-package deps: ECAssistant.Core.Setup, ECAssistant.Core.Transport

### Class: RemoteProviderConfig
> A single remote OpenAI-compatible provider entry.

### Class: RemoteProviderIntegrationTests
> v14.7: Integration tests for the remote provider path (native OpenAI function calling).
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Tools, Xunit

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

### Class: RequestChoiceTests
> v14.9 interactive checkpoint (RequestChoice): listener gets the prompt + options,
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Session, Xunit

### Class: ResourceLoader
> Loads embedded resources from the Core DLL.

### Class: SamplingConfig

### Class: SeatbeltShellSandbox
> macOS Seatbelt (sandbox-exec) wrapper: shell commands run under a generated
Implements: IShellSandbox
Constructor:
  - SeatbeltShellSandbox(ShellSandboxOptions options, ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: SeatbeltShellSandboxTests
> v15: Seatbelt sandbox — profile generation, command wrapping, and LIVE
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services.Shell

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

### Class: ServerAssetInstaller
> One downloadable asset entry from an install manifest.
Constructor:
  - ServerAssetInstaller(HttpClient http, string backendsRootDir, string platformKey)

### Class: ServerBinaryInstaller
> Copies the LLM server runtime from the app's NuGet-populated content directory
Constructor:
  - ServerBinaryInstaller(string sourceServerDir, string targetServerDir)

### Class: ServerConfigWriter
> Core owns the LLM server config. Before the server process is launched, this writer
Cross-package deps: ECAssistant.Core.Config

### Class: ServerConfigWriterTests
> Core owns the LLM server config: before launch, {llmRoot}/llm-server.json must exist
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services.Http, Xunit

### Class: ServerConnection
> Capabilities an LLM backend advertises at connect time. v13c.

### Class: ServerInstallCoordinator
> Inspection result for an existing server directory.
Implements: IServerInstallCoordinator
Constructor:
  - ServerInstallCoordinator(string llmRoot, ISetupUi ui)

### Class: ServerLauncher
> Detects if ECAssistantLLM server is running. If not, launches it from the
Constructor:
  - ServerLauncher(LlmProviderConfig config)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Transport

### Class: ServerLauncherResolveTests
> Tests for the standalone ServerLauncher: resolves the server binary from
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services.Http, Xunit

### Class: SessionBuilder
> Builder for creating and initializing AgentSessions with standard tools.
Implements: ISessionBuilder
Constructor:
  - SessionBuilder(AppConfig config, string workingDir, string userConfigDir, ILogger? logger = null, BackgroundProcessManager? bgManager = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Services, ECAssistant.Core.Session, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Background, ECAssistant.Core.Tools.Build, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Git, ECAssistant.Core.Tools.Reader, ECAssistant.Core.Tools.Research, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Mcp, ECAssistant.Core.Interfaces, ECAssistant.Core.Services.Http, ECAssistant.Core.Transport

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
  - SessionManager(AppConfig config, string resolvedModelPath, string workingDir, ILogger? logger = null, Func<string, string?, string?, OpenAIClient>? openAIClientFactory = null, Func<LlmProviderConfig, ServerLauncher>? serverLauncherFactory = null, LlmServerClient? serverClient = null, SecureKeyStore? keyStore = null, ILlmProviderRegistry? providerRegistry = null, IModelParamValidator? modelParamValidator = null)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Interfaces, ECAssistant.Core.Transport

### Class: SessionQueueTests
> Tests for session prompt queue logic and run state transitions.
Cross-package deps: ECAssistant.Core.Session

### Class: SessionVerbosityDefaultTests
> v14.19 (Emre): sessions run VERBOSE by default — users see tool status,
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Session

### Class: SetupWizard
> Paths and services the wizard needs; assembled by the host.
Implements: ISetupWizard
Constructor:
  - SetupWizard(ISetupUi ui)
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup

### Class: ShellSessionFactory
> Factory for per-run persistent shell sessions — picks the platform
Implements: IShellSessionFactory
Constructor:
  - ShellSessionFactory(ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: ShellTeardownSweepTests
> v15: persistent shell session — working directory and exported env survive
Cross-package deps: ECAssistant.Core.Services.Shell

### Class: ShellTierPromptOSTests
> v15: EShellAgent prompt matrix — 3 OSes × 2 tiers. The tool's rules are
Cross-package deps: ECAssistant.Core.Services.Shell, ECAssistant.Core.Tools.Shell

### Class: SingleToolResult
> Result of a single tool execution within a batch.
Cross-package deps: ECAssistant.Core.Tools

### Class: SipsPdfPageRenderer
> macOS implementation of IPdfPageRenderer using the built-in `sips`
Implements: IPdfPageRenderer
Constructor:
  - SipsPdfPageRenderer(IProcessRunner processRunner)
Cross-package deps: ECAssistant.Core.Interfaces

### Class: SseParser
> Parses SSE (Server-Sent Events) stream from OpenAI-compatible chat completions.

### Class: StartupTimeoutDefaultsTests
> v12.8 regression: the installer must detect models already on disk so
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup, Xunit

### Class: StatelessSessionRoutingTests
> v15: SessionId=null on InferenceRequestParams must be a REAL stateless signal —
Cross-package deps: ECAssistant.Core.Interfaces, ECAssistant.Core.Services.Http, ECAssistant.Core.Transport

### Class: SteeringQueue
> v14.12.2: mid-run steering seam. The host queues user input while the

### Class: SteeringQueueTests
> v14.12.2: mid-run steering seam — single pending slot, newest wins, drained once.
Cross-package deps: ECAssistant.Core.Engine

### Class: StepMapper
> Step Mapper — takes decomposed sub-tasks and maps them to concrete tool calls.
Implements: IStepMapper
Constructor:
  - StepMapper(IEngineToolContext engine, ILogger? logger = null)
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, ECAssistant.Core.Engine

### Class: StepMapperTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools

### Class: StringUtil
> String utility — truncation and text helpers.

### Class: StructuredDecisionAdapter
> v14 Parses a grammar-forced decision envelope ({"thinking", "answer"|"toolcalls"})
Cross-package deps: ECAssistant.Core.Orchestration, ECAssistant.Core.Tools

### Class: StructuredDecisionAdapterTests
> v14: grammar-forced decision envelope → LLMDecision (native JSON pipeline).
Cross-package deps: ECAssistant.Core.Engine, Xunit

### Class: StructuredFallbackResilienceTests
> v14.10.1: a transient null from GenerateStructuredAsync (provider hiccup,
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Config, ECAssistant.Core.Orchestration

### Class: SubAgentBriefBuilder
> v14.17: tier-aware sub-agent brief. Builds the child orchestrator's execution

### Class: SubAgentBriefBuilderTests
> v14.17: tier-aware sub-agent brief — pure logic only (no LLM, no build, no DB).
Cross-package deps: ECAssistant.Core.Engine

### Class: SubAgentConfig
> Sub-agent configuration. GPU/thread params are server-side concerns

### Class: SubAgentError
Cross-package deps: ECAssistant.Core.Orchestration

### Class: SubAgentErrorTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration

### Class: SubAgentIntegrationTests
> Integration tests for sub-agent spawning through the orchestrator.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.UI

### Class: SubAgentManager
> Sub-agent task definition — what the main agent wants a sub-agent to do.
Implements: IDisposable
Constructor:
  - SubAgentManager(ISubAgentEngineHost mainEngine, string mainWorkingDir = "", ILogger? logger = null, ISessionOutput? sessionOutput = null, AppConfig? config = null, ECAssistant.Core.Interfaces.IProcessRunner? processRunner = null, ECAssistant.Core.Interfaces.IFileSystem? fileSystem = null, ECAssistant.Core.Interfaces.IHttpClient? httpClient = null, Services.BackgroundProcessManager? bgManager = null, Tools.ToolPolicy? parentToolPolicy = null)
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

### Class: TextMatchPipeline
> Runs match strategies in order (exact → whitespace-tolerant → line-anchored)
Implements: ITextMatchPipeline
Constructor:
  - TextMatchPipeline(IReadOnlyList<ITextMatchStrategy> strategies)

### Class: TextMatchResult
> Outcome of one match strategy scanning content for a search text:

### Class: TextMatchStrategyTests
> v14.15 fuzzy diff-based edits: layered match strategies, pipeline ordering,
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Code

### Class: TfidfEmbedder
> TF-IDF text embedding implementation.
Implements: IVectorEmbedder
Cross-package deps: ECAssistant.Core.Interfaces

### Class: TfidfEmbedderTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: TierInferenceOverride
> v14.12: Model-tier profile — gates how much harness scaffolding (hand-holding

### Class: TierInferenceTuner
> v15: tier-aware inference parameter tuning — values live in CONFIG
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces

### Class: TierInferenceTunerTests
> v15: tier inference values are CONFIG-DRIVEN (model_tier.inference overrides
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Services

### Class: TokenCounter
> Token counting via RemoteTokenizer (HTTP /eca/tokenize endpoint).
Constructor:
  - TokenCounter(RemoteTokenizer? tokenizer = null)
Cross-package deps: ECAssistant.Core.Services.Http

### Class: TokenCounterTests
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolCallChainSubstitutionTests
> v14.20: dataflow toolchains — {{N}} reference substitution over prior call
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

### Class: ToolDescriptionScopeTests
> v14.19.1: every user-facing tool description carries an explicit scope —
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services, ECAssistant.Core.Tools.Background, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Build, ECAssistant.Core.Tools.Git, ECAssistant.Core.Tools.Research, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Reader

### Class: ToolImageRefTests
Cross-package deps: ECAssistant.Core.Tools

### Class: ToolOutputLimitsConfig
> Tool-result truncation limits (2026-09-21 — previously hardcoded constants).

### Class: ToolOutputProjector
> v14.12.2: curates an oversized tool output for the context window — key lines

### Class: ToolOutputProjectorTests
> v14.12.2: curated tool-output projection — key lines + head/tail instead of a
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolPermission
> Permission rule for a single tool.

### Class: ToolPermissionConfigEntry
> Config entry for tool permissions in appsettings.json.

### Class: ToolPipelineIntegrationTests
> Integration tests for tools working through the full pipeline:
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ToolPolicy
> Tool policy manager — checks if a tool requires user approval before execution.

### Class: ToolPolicyDecision
> Result of a tool policy check.

### Class: ToolPolicySessionApprovalTests
Cross-package deps: Xunit, ECAssistant.Core.Tools

### Class: ToolPolicyTests
Cross-package deps: ECAssistant.Core.Tools

### Class: ToolRepeatTracker
> Detects pathological tool-call repetition in the orchestrator loop — beyond the

### Class: ToolRepeatTrackerTests
> Unit tests for the v14.9 orchestrator loop detection (ToolRepeatTracker):
Cross-package deps: ECAssistant.Core.Engine, Xunit

### Class: ToolSpec
> Abstracts LLM inference via HTTP (OpenAI-compatible endpoint).

### Class: TranscriptIntegrationTests
> Integration tests for ConversationTranscript persistence —
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Engine

### Class: TranscriptMessage
> A single message in the conversation transcript.

### Class: TypedOutputAndChainIntegrationTests
> v14.20 integration: typed per-tool outputs (RenderForModel) and dataflow
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Build, ECAssistant.Core.UI, Moq

### Class: UserJourneyE2E
> v14.19: USER-EXPERIENCE E2E — drives the session through AgentSession.Prompt
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Session, ECAssistant.TestSupport

### Class: UserJourneyExtendedE2E
> v14.19: extended user journeys — every remaining harness feature experienced
Cross-package deps: ECAssistant.Core.Config, ECAssistant.TestSupport

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

### Class: VerificationConfig
> v14.13: Tier-aware post-edit verification gate. After a file-modifying tool call

### Class: VerificationResult
> v14.13: Immutable outcome of one verification run (build + optional test).
Constructor:
  - VerificationResult(bool succeeded, string output, string command, int exitCode)

### Class: VisionStructureGrammar
> GBNF grammar that force-constrains the vision model's output to the

### Class: VisionStructureJsonParser
> Parses and validates a model response into a VisionStructureResult.

### Class: VisionStructureJsonParserTests
Cross-package deps: ECAssistant.Core.Vision

### Class: VisionStructurePromptBuilder
> Builds the analysis prompt sent with an image to the vision model.

### Class: WhitespaceTolerantMatchStrategy
> Priority 2: whitespace-tolerant match — indentation-insensitive anchoring.
Implements: LineMatchStrategyBase

### Class: WizardCatalogTests
> Wizard rework units: remote catalog fetch fallback, local model discovery.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup, Xunit

### Class: WizardContext
> Paths and services the wizard needs; assembled by the host.
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup

### Class: WizardOnDiskDetectionTests
> v12.8 regression: the installer must detect models already on disk so
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup, Xunit

### Class: WorkspaceConfig

### Record: BuildError
> Parser for .NET build output — extracts errors and warnings.
Constructor:
  - BuildError(string File, int Line, string Code, string Message)

### Record: CapturedToolCall
> v14.14: One successful tool call from a completed run, in summarized form.
Constructor:
  - CapturedToolCall(string ToolName, string ArgsSummary)

### Record: DownloadProgress
> Progress callback payload for a running download.
Constructor:
  - DownloadProgress(string Filename, long BytesReceived, long? TotalBytes, double Percent, double MbPerSecond)

### Record: EcaServiceBundle
> Bundle of all wired services returned by EcaCompositionRoot.Build().
Constructor:
  - EcaServiceBundle(AppConfig Config, string ModelPath, string WorkingDirectory, string UserConfigDirectory, ILogger Logger, BackgroundProcessManager BackgroundProcesses, FileWatcherService FileWatcher, ISessionBuilder SessionBuilder)
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

### Record: McpContentItem
> Client for communicating with an MCP (Model Context Protocol) server.
Constructor:
  - McpContentItem(string Type, string? Text, string? Data, string? MimeType)

### Record: McpServerInfo
> Client for communicating with an MCP (Model Context Protocol) server.
Constructor:
  - McpServerInfo(string Name, string Version)

### Record: McpToolDescriptor
> Client for communicating with an MCP (Model Context Protocol) server.
Constructor:
  - McpToolDescriptor(string Name, string Description, string InputSchema)

### Record: McpToolResult
> Client for communicating with an MCP (Model Context Protocol) server.
Constructor:
  - McpToolResult(bool IsError, IReadOnlyList<McpContentItem> Content)

### Record: MemoryEntry
Constructor:
  - MemoryEntry(string Content, string Metadata, float[]? Embedding = null)

### Record: PinnedFact
> One pinned fact surfaced to the model after compaction.
Constructor:
  - PinnedFact(string Kind, string Text)

### Record: ProcessResult
Constructor:
  - ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut)

### Record: RemoteModelInfo
> A model advertised by a remote OpenAI-compatible /models endpoint.
Constructor:
  - RemoteModelInfo(string Id, bool SupportsVision)

### Record: RemoteProbeResult
> A model advertised by a remote OpenAI-compatible /models endpoint.
Constructor:
  - RemoteProbeResult(bool Reachable, IReadOnlyList<RemoteModelInfo> Models, string? Error = null)

### Record: RemoteProvider
> A fully-resolved remote provider ready for engine construction.
Constructor:
  - RemoteProvider(string Name, string Endpoint, string? ApiKey, string ModelId, string? EmbeddingModelId)

### Record: ShellCommandResult
> A persistent interactive shell session: working directory, environment variables,
Constructor:
  - ShellCommandResult(int ExitCode, string StdOut, string StdErr)

### Record: ShellSandboxOptions
> macOS Seatbelt (sandbox-exec) wrapper: shell commands run under a generated
Constructor:
  - ShellSandboxOptions(bool Enabled, string WorkspaceRoot, bool AllowNetwork = false)
Cross-package deps: ECAssistant.Core.Interfaces

### Record: TranscriptMessage
Constructor:
  - TranscriptMessage(string Role, string Content, string? ToolCallId = null)

### Record: VectorResult
Constructor:
  - VectorResult(string Content, string Metadata, float Score)

### Record: VisionElement
> One detected element in a vision-structured analysis.
Constructor:
  - VisionElement(string Id, VisionElementType Type, string Text, VisionBoundingBox BoundingBox, double Confidence, IReadOnlyList<string> AssociatedWith)

### Record: VisionElementGroup
> A semantic group of related elements (e.g. a form, a toolbar, a section).
Constructor:
  - VisionElementGroup(string Id, VisionGroupRole Role, IReadOnlyList<string> MemberIds)

### Record: VisionSourceInfo
> Describes the analyzed source (screenshot or PDF page).
Constructor:
  - VisionSourceInfo(VisionSourceKind Kind, int Page, int Width, int Height)
