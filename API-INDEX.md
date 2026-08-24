# API-INDEX.md — ECAssistantCore

Generated: 2026-08-24T20:50:24.445832+00:00
Packages: 13  |  Types: 172  |  LOC: 11475

## Analysis (4 types, ~307 LOC)

- 🟡 EContextAnalyzer  (ECAssistant.Core.Analysis)
- 🟡 FileInfoData  (ECAssistant.Core.Analysis)
- 🟡 ProjectArchitecture  (ECAssistant.Core.Analysis)
- 🟡 ProjectRelationship  (ECAssistant.Core.Analysis)

## Composition (1 types, ~95 LOC)

- 🟡 EcaCompositionRoot  (ECAssistant.Core.Composition)

## Config (19 types, ~537 LOC)

- 🟡 AgentConfig  (ECAssistant.Core.Config)
- 🟡 AgentConfigBuilder  (ECAssistant.Core)
- 🟡 BackgroundTasksConfig  (ECAssistant.Core.Config)
- 🟡 ConfigLoader  (ECAssistant.Core.Config)  deps: [IFileSystem]
- 🟡 ContextManagementConfig  (ECAssistant.Core.Config)
- 🟡 DecomposeConfig  (ECAssistant.Core.Config)
- 🟡 EAgentConfig  (ECAssistant.Core.Config)
- 🟡 EmbeddingConfig  (ECAssistant.Core.Config)
- 🟡 InferenceConfig  (ECAssistant.Core.Config)
- 🟡 InterfaceConfig  (ECAssistant.Core.Config)
- 🟡 LlmConfig  (ECAssistant.Core.Config)
- 🟡 LlmProviderConfig  (ECAssistant.Core.Config)
- 🟡 LlmServerEndpointConfig  (ECAssistant.Core.Config)
- 🟡 MemoryConfig  (ECAssistant.Core.Config)
- 🟡 SamplingConfig  (ECAssistant.Core.Config)
- 🟡 SubAgentConfig  (ECAssistant.Core.Config)
- 🟡 SummarizeConfig  (ECAssistant.Core.Config)
- 🟡 VectorMemoryConfig  (ECAssistant.Core.Config)
- 🟡 WorkspaceConfig  (ECAssistant.Core.Config)

## Engine (47 types, ~9095 LOC)

- 🟡 ActiveSubAgent  (ECAssistant.Core.Engine)
- 🟡 BatchToolResult  (ECAssistant.Core.Engine)
- 🟡 ContextWindow  (ECAssistant.Core.Engine)  deps: [uint, =]
- 🟡 ConversationTranscript  (ECAssistant.Core.Engine)
- 🟡 DecisionResult  (ECAssistant.Core.Engine)
- 🟡 DependencyGroup  (ECAssistant.Core.Engine)
- 🟡 EAgentEngine : IEngine  (ECAssistant.Core.Engine)  deps: [IInferenceEngine, IKvCacheController, =, =, =, =, =, =, =, =, =, =, =]
- 🟡 EDecisionLoop  (ECAssistant.Core.Engine)  deps: [EAgentEngine, =]
- 🟡 ExecutionPlan  (ECAssistant.Core.Engine)
- 🟡 ExecutionState  (ECAssistant.Core.Engine)
- 🟡 FailureAnalysis  (ECAssistant.Core.Engine)
- 🟡 FailureEntry  (ECAssistant.Core.Engine)
- 🟡 FileContext  (ECAssistant.Core.Engine)
- 🟡 FileRelationship  (ECAssistant.Core.Engine)
- 🟡 FileSnapshot  (ECAssistant.Core.Engine)
- 🟡 InferenceEngineNoop : IInferenceEngine  (ECAssistant.Core.Engine)
- 🟡 KvCacheNoop : IKvCacheController  (ECAssistant.Core.Engine)
- 🟡 LLMDecision  (ECAssistant.Core.Orchestration)  deps: [string?, string?>, =]
- 🟡 MockEngine : EAgentEngine  (ECAssistant.Core.Engine)  deps: [Queue<string>, =, =, =, =]
- 🟡 ModelLoadException  (ECAssistant.Core.Engine)  deps: [ModelLoadPhase, uint, =]
- 🟡 ModelParamValidator  (ECAssistant.Core.Engine)  deps: [=]
- 🟡 NullLogger : Microsoft.Extensions.Logging.ILogger  (ECAssistant.Core.Engine)
- 🟡 OrchestratorResult  (ECAssistant.Core.Orchestration)
- 🟡 ParallelToolExecutor  (ECAssistant.Core.Engine)  deps: [EAgentEngine, ToolPolicy, Task<EToolResult>>, =, =]
- 🟡 PlannedToolCall  (ECAssistant.Core.Engine)
- 🟡 PrefixCachedExtractor  (ECAssistant.Core.Engine)  deps: [IInferenceEngine, IKvCacheController, =]
- 🟡 ProjectContext  (ECAssistant.Core.Engine)
- 🟡 ProjectContextManager  (ECAssistant.Core.Engine)  deps: [=]
- 🟡 SelfCorrectionManager  (ECAssistant.Core.Engine)  deps: [=]
- 🟡 SingleToolResult  (ECAssistant.Core.Engine)
- 🟡 StepMapper  (ECAssistant.Core.Engine)  deps: [EAgentEngine, =]
- 🟡 SubAgentError  (ECAssistant.Core.Engine)
- 🟡 SubAgentManager  (ECAssistant.Core.Engine)  deps: [EAgentEngine, =, =, =, =, =, =, =, =]
- 🟡 SubAgentResult  (ECAssistant.Core.Engine)
- 🟡 SubAgentTask  (ECAssistant.Core.Engine)
- 🟡 SubTask  (ECAssistant.Core.Engine)
- 🟡 TaskPlanner  (ECAssistant.Core.Engine)  deps: [=]
- 🟡 TokenCounter  (ECAssistant.Core.Engine)  deps: [=]
- 🟡 ToolCallRequest  (ECAssistant.Core.Engine)
- 🟡 ToolCallResult  (ECAssistant.Core.Engine)
- 🟡 ToolDependencyAnalyzer  (ECAssistant.Core.Engine)
- 🟡 TranscriptMessage  (ECAssistant.Core.Engine)
- ⚪ FailurePattern  (ECAssistant.Core.Engine)
- ⚪ ModelLoadPhase  (ECAssistant.Core.Engine)
- ⚪ OrchestratorStatus  (ECAssistant.Core.Orchestration)
- ⚪ SubAgentErrorKind  (ECAssistant.Core.Engine)
- ⚪ SubTaskStatus  (ECAssistant.Core.Engine)

## Interfaces (21 types, ~311 LOC)

- 🟡 InferenceRequestParams  (ECAssistant.Core.Interfaces)
- 🟡 KvCacheStatus  (ECAssistant.Core.Interfaces)
- 🟡 RemoteModelInfo  (ECAssistant.Core.Interfaces)
- 🟡 RemoteModelLoadOptions  (ECAssistant.Core.Interfaces)
- 🔵 IConfigProvider  (ECAssistant.Core.Interfaces)
- 🔵 IContextManager  (ECAssistant.Core.Interfaces)
- 🔵 IEngine  (ECAssistant.Core.Interfaces)
- 🔵 IFileSystem  (ECAssistant.Core.Interfaces)
- 🔵 IHttpClient  (ECAssistant.Core.Interfaces)
- 🔵 IInferenceEngine  (ECAssistant.Core.Interfaces)
- 🔵 IKvCacheController  (ECAssistant.Core.Interfaces)
- 🔵 ILlmServerClient  (ECAssistant.Core.Interfaces)
- 🔵 ILogger  (ECAssistant.Core.Interfaces)
- 🔵 IMemoryService  (ECAssistant.Core.Interfaces)
- 🔵 IModelLoader  (ECAssistant.Core.Interfaces)
- 🔵 IOutputRenderer  (ECAssistant.Core.Interfaces)
- 🔵 IProcessRunner  (ECAssistant.Core.Interfaces)
- 🔵 ITerminal  (ECAssistant.Core.Interfaces)
- 🔵 IToolPolicyEvaluator  (ECAssistant.Core.Interfaces)
- 🔵 IVectorEmbedder  (ECAssistant.Core.Interfaces)
- 🔵 IVectorStore  (ECAssistant.Core.Interfaces)

## Memory (5 types, ~410 LOC)

- 🟡 EMemoryManager  (ECAssistant.Core.Memory)  deps: [=]
- 🟡 MemoryEntry  (ECAssistant.Core.Memory)
- 🟡 VectorEntry  (ECAssistant.Core.Memory)
- 🟡 VectorMemoryStore  (ECAssistant.Core.Memory)  deps: [=]
- 🟡 VectorSearchResult  (ECAssistant.Core.Memory)

## Root (4 types, ~794 LOC)

- 🟡 AgentOrchestrator  (ECAssistant.Core.Orchestration)  deps: [EAgentEngine, =, =, =, =, =, =]
- 🟡 EColor  (ECAssistant.Core)
- 🟡 StringUtil  (ECAssistant.Core)
- 🟡 SystemPromptBuilder  (ECAssistant.Core)

## Services (31 types, ~1514 LOC)

- 🟡 BackgroundProcessManager  (ECAssistant.Core.Services)
- 🟡 BgProcess  (ECAssistant.Core.Services)
- 🟡 BgProcessInfo  (ECAssistant.Core.Services)
- 🟡 ConfigProvider : IConfigProvider  (ECAssistant.Core.Services)  deps: [IFileSystem]
- 🟡 ContextManager : IContextManager  (ECAssistant.Core.Services)  deps: [IInferenceEngine, IConfigProvider]
- 🟡 FileChange  (ECAssistant.Core.Services)
- 🟡 FileSystemAdapter : IFileSystem  (ECAssistant.Core.Services)
- 🟡 FileWatcherService  (ECAssistant.Core.Services)  deps: [=, =]
- 🟡 HttpClientAdapter : IHttpClient  (ECAssistant.Core.Services)
- 🟡 HttpEmbedder : IVectorEmbedder  (ECAssistant.Core.Services.Http)  deps: [OpenAIClient, =]
- 🟡 HttpStreamingEngine : IInferenceEngine  (ECAssistant.Core.Services.Http)  deps: [OpenAIClient, =, =]
- 🟡 InMemoryVectorStore : IVectorStore  (ECAssistant.Core.Services)
- 🟡 InferenceParamsFactory  (ECAssistant.Core.Services)
- 🟡 LlmServerClient : ILlmServerClient  (ECAssistant.Core.Services.Http)  deps: [=]
- 🟡 Logger : ILogger  (ECAssistant.Core.Services)
- 🟡 MemoryService : IMemoryService  (ECAssistant.Core.Services)  deps: [IFileSystem, IVectorStore, IConfigProvider, IVectorEmbedder]
- 🟡 NopKvCacheController : IKvCacheController  (ECAssistant.Core.Services.Http)
- 🟡 ProcessRunner : IProcessRunner  (ECAssistant.Core.Services)
- 🟡 RegisterResponse  (ECAssistant.Core.Services.Http)
- 🟡 RemoteKvCacheController : IKvCacheController  (ECAssistant.Core.Services.Http)  deps: [OpenAIClient]
- 🟡 RemoteModelLoader : IModelLoader  (ECAssistant.Core.Services.Http)  deps: [OpenAIClient]
- 🟡 RemoteModelsResponse  (ECAssistant.Core.Services.Http)
- 🟡 RemoteTokenizer  (ECAssistant.Core.Services.Http)  deps: [OpenAIClient, =]
- 🟡 ResourceLoader  (ECAssistant.Core.Services)
- 🟡 ServerLauncher  (ECAssistant.Core.Services.Http)  deps: [LlmProviderConfig]
- 🟡 SummaryService  (ECAssistant.Core.Services)  deps: [=]
- 🟡 TerminalAdapter : ITerminal  (ECAssistant.Core.Services)
- 🟡 TfidfEmbedder : IVectorEmbedder  (ECAssistant.Core.Services)
- ⚪ BgStatus  (ECAssistant.Core.Services)
- ⚪ FileChangeType  (ECAssistant.Core.Services)
- ⚪ LogLevel  (ECAssistant.Core.Services)

## Session (11 types, ~1138 LOC)

- 🟡 AgentSession : ISessionOutput, ISessionContext  (ECAssistant.Core.Session)  deps: [string?, InferenceRequestParams, SemaphoreSlim, =, =, =, =, =, =, =, =]
- 🟡 OutputEntry  (ECAssistant.Core.Session)
- 🟡 SessionBuilder  (ECAssistant.Core)  deps: [EAgentConfig, =, =]
- 🟡 SessionDiscovery  (ECAssistant.Core.Session)
- 🟡 SessionManager  (ECAssistant.Core.Session)  deps: [EAgentConfig, =]
- 🟡 SessionMeta  (ECAssistant.Core.Session)
- ⚪ OutputState  (ECAssistant.Core.Session)
- ⚪ SessionRunState  (ECAssistant.Core.Session)
- 🔵 IOutputListener  (ECAssistant.Core.Session)
- 🔵 ISessionContext  (ECAssistant.Core.Session)
- 🔵 ISessionOutput  (ECAssistant.Core.Session)

## Testing (7 types, ~1283 LOC)

- 🟡 EGuiTestHarness : EGuiBase  (ECAssistant.Core.Testing)
- 🟡 EcaTests  (ECAssistant.Core.Testing)
- 🟡 TestContext  (ECAssistant.Core.Testing)
- 🟡 TestResult  (ECAssistant.Core.Testing)
- 🟡 TestRunner  (ECAssistant.Core.Testing)  deps: [=, =]
- 🟡 TestScenario  (ECAssistant.Core.Testing)
- 🟡 TestSessionOutput : ISessionOutput  (ECAssistant.Core.Testing)  deps: [EGuiTestHarness]

## Tools (19 types, ~1392 LOC)

- 🟡 BuildErrorParser  (ECAssistant.Core.Tools.Build)
- 🟡 EBackgroundExecTool : EToolBase  (ECAssistant.Core.Tools.Background)  deps: [BackgroundProcessManager, IProcessRunner, IFileSystem, EAgentConfig]
- 🟡 ECodeEditorTool : EToolBase  (ECAssistant.Core.Tools.Code)  deps: [IFileSystem, EAgentConfig]
- 🟡 EDotnetBuildTool : EToolBase  (ECAssistant.Core.Tools.Build)  deps: [IProcessRunner, EAgentConfig]
- 🟡 EFileAnalyzer : EToolBase  (ECAssistant.Core.Tools.Example)
- 🟡 EFileReaderTool : EToolBase  (ECAssistant.Core.Tools.Reader)  deps: [IFileSystem, EAgentConfig]
- 🟡 EFileResearchTool : EToolBase  (ECAssistant.Core.Tools.Research)  deps: [IFileSystem, EAgentConfig]
- 🟡 EGitTool : EToolBase  (ECAssistant.Core.Tools.Git)  deps: [IProcessRunner, IFileSystem, EAgentConfig]
- 🟡 EShellAgent : EToolBase  (ECAssistant.Core.Tools.Shell)  deps: [IProcessRunner, EAgentConfig]
- 🟡 ESubAgentTool : EToolBase  (ECAssistant.Core.Tools.SubAgent)  deps: [SubAgentManager]
- 🟡 EToolBase  (ECAssistant.Core.Tools)
- 🟡 EToolResult  (ECAssistant.Core.Tools)
- 🟡 EWebFetchTool : EToolBase  (ECAssistant.Core.Tools.Web)  deps: [IHttpClient, EAgentConfig]
- 🟡 EWebSearchTool : EToolBase  (ECAssistant.Core.Tools.Web)  deps: [IHttpClient, EAgentConfig]
- 🟡 ToolPermission  (ECAssistant.Core.Tools)
- 🟡 ToolPermissionConfigEntry  (ECAssistant.Core.Tools)
- 🟡 ToolPolicy  (ECAssistant.Core.Tools)
- 🟡 ToolPolicyDecision  (ECAssistant.Core.Tools)
- ⚪ ToolPermissionLevel  (ECAssistant.Core.Tools)

## Transport (2 types, ~136 LOC)

- 🟡 OpenAIClient  (ECAssistant.Core.Transport)  deps: [=, =, =]
- 🟡 SseParser  (ECAssistant.Core.Transport)

## UI (1 types, ~35 LOC)

- 🟡 EGuiBase  (ECAssistant.Core.UI)
