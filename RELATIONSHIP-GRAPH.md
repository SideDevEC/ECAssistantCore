# RELATIONSHIP-GRAPH.md — ECAssistantCore

Generated: 2026-08-24T20:50:24.544153+00:00
Types: 172  |  Implements edges: 35  |  Uses edges: 51

## Implements (class → interface)

- AgentSession ──implements──► ISessionContext  [Session → Session]
- AgentSession ──implements──► ISessionOutput  [Session → Session]
- ConfigProvider ──implements──► IConfigProvider  [Services → Interfaces] ⚠ CROSS-PKG
- ContextManager ──implements──► IContextManager  [Services → Interfaces] ⚠ CROSS-PKG
- EAgentEngine ──implements──► IEngine  [Engine → Interfaces] ⚠ CROSS-PKG
- EBackgroundExecTool ──implements──► EToolBase  [Tools → Tools]
- ECodeEditorTool ──implements──► EToolBase  [Tools → Tools]
- EDotnetBuildTool ──implements──► EToolBase  [Tools → Tools]
- EFileAnalyzer ──implements──► EToolBase  [Tools → Tools]
- EFileReaderTool ──implements──► EToolBase  [Tools → Tools]
- EFileResearchTool ──implements──► EToolBase  [Tools → Tools]
- EGitTool ──implements──► EToolBase  [Tools → Tools]
- EGuiTestHarness ──implements──► EGuiBase  [Testing → UI] ⚠ CROSS-PKG
- EShellAgent ──implements──► EToolBase  [Tools → Tools]
- ESubAgentTool ──implements──► EToolBase  [Tools → Tools]
- EWebFetchTool ──implements──► EToolBase  [Tools → Tools]
- EWebSearchTool ──implements──► EToolBase  [Tools → Tools]
- FileSystemAdapter ──implements──► IFileSystem  [Services → Interfaces] ⚠ CROSS-PKG
- HttpClientAdapter ──implements──► IHttpClient  [Services → Interfaces] ⚠ CROSS-PKG
- HttpEmbedder ──implements──► IVectorEmbedder  [Services → Interfaces] ⚠ CROSS-PKG
- HttpStreamingEngine ──implements──► IInferenceEngine  [Services → Interfaces] ⚠ CROSS-PKG
- InMemoryVectorStore ──implements──► IVectorStore  [Services → Interfaces] ⚠ CROSS-PKG
- InferenceEngineNoop ──implements──► IInferenceEngine  [Engine → Interfaces] ⚠ CROSS-PKG
- KvCacheNoop ──implements──► IKvCacheController  [Engine → Interfaces] ⚠ CROSS-PKG
- LlmServerClient ──implements──► ILlmServerClient  [Services → Interfaces] ⚠ CROSS-PKG
- Logger ──implements──► ILogger  [Services → Interfaces] ⚠ CROSS-PKG
- MemoryService ──implements──► IMemoryService  [Services → Interfaces] ⚠ CROSS-PKG
- MockEngine ──implements──► EAgentEngine  [Engine → Engine]
- NopKvCacheController ──implements──► IKvCacheController  [Services → Interfaces] ⚠ CROSS-PKG
- ProcessRunner ──implements──► IProcessRunner  [Services → Interfaces] ⚠ CROSS-PKG
- RemoteKvCacheController ──implements──► IKvCacheController  [Services → Interfaces] ⚠ CROSS-PKG
- RemoteModelLoader ──implements──► IModelLoader  [Services → Interfaces] ⚠ CROSS-PKG
- TerminalAdapter ──implements──► ITerminal  [Services → Interfaces] ⚠ CROSS-PKG
- TestSessionOutput ──implements──► ISessionOutput  [Testing → Session] ⚠ CROSS-PKG
- TfidfEmbedder ──implements──► IVectorEmbedder  [Services → Interfaces] ⚠ CROSS-PKG

## Uses (type → dependency)

- AgentOrchestrator ──uses──► EAgentEngine  [Root → Engine] ⚠ CROSS-PKG
- AgentSession ──uses──► InferenceRequestParams  [Session → Interfaces] ⚠ CROSS-PKG
- ConfigLoader ──uses──► IFileSystem  [Config → Interfaces] ⚠ CROSS-PKG
- ConfigProvider ──uses──► IFileSystem  [Services → Interfaces] ⚠ CROSS-PKG
- ContextManager ──uses──► IConfigProvider  [Services → Interfaces] ⚠ CROSS-PKG
- ContextManager ──uses──► IInferenceEngine  [Services → Interfaces] ⚠ CROSS-PKG
- EAgentEngine ──uses──► IInferenceEngine  [Engine → Interfaces] ⚠ CROSS-PKG
- EAgentEngine ──uses──► IKvCacheController  [Engine → Interfaces] ⚠ CROSS-PKG
- EBackgroundExecTool ──uses──► BackgroundProcessManager  [Tools → Services] ⚠ CROSS-PKG
- EBackgroundExecTool ──uses──► EAgentConfig  [Tools → Config] ⚠ CROSS-PKG
- EBackgroundExecTool ──uses──► IFileSystem  [Tools → Interfaces] ⚠ CROSS-PKG
- EBackgroundExecTool ──uses──► IProcessRunner  [Tools → Interfaces] ⚠ CROSS-PKG
- ECodeEditorTool ──uses──► EAgentConfig  [Tools → Config] ⚠ CROSS-PKG
- ECodeEditorTool ──uses──► IFileSystem  [Tools → Interfaces] ⚠ CROSS-PKG
- EDecisionLoop ──uses──► EAgentEngine  [Engine → Engine]
- EDotnetBuildTool ──uses──► EAgentConfig  [Tools → Config] ⚠ CROSS-PKG
- EDotnetBuildTool ──uses──► IProcessRunner  [Tools → Interfaces] ⚠ CROSS-PKG
- EFileReaderTool ──uses──► EAgentConfig  [Tools → Config] ⚠ CROSS-PKG
- EFileReaderTool ──uses──► IFileSystem  [Tools → Interfaces] ⚠ CROSS-PKG
- EFileResearchTool ──uses──► EAgentConfig  [Tools → Config] ⚠ CROSS-PKG
- EFileResearchTool ──uses──► IFileSystem  [Tools → Interfaces] ⚠ CROSS-PKG
- EGitTool ──uses──► EAgentConfig  [Tools → Config] ⚠ CROSS-PKG
- EGitTool ──uses──► IFileSystem  [Tools → Interfaces] ⚠ CROSS-PKG
- EGitTool ──uses──► IProcessRunner  [Tools → Interfaces] ⚠ CROSS-PKG
- EShellAgent ──uses──► EAgentConfig  [Tools → Config] ⚠ CROSS-PKG
- EShellAgent ──uses──► IProcessRunner  [Tools → Interfaces] ⚠ CROSS-PKG
- ESubAgentTool ──uses──► SubAgentManager  [Tools → Engine] ⚠ CROSS-PKG
- EWebFetchTool ──uses──► EAgentConfig  [Tools → Config] ⚠ CROSS-PKG
- EWebFetchTool ──uses──► IHttpClient  [Tools → Interfaces] ⚠ CROSS-PKG
- EWebSearchTool ──uses──► EAgentConfig  [Tools → Config] ⚠ CROSS-PKG
- EWebSearchTool ──uses──► IHttpClient  [Tools → Interfaces] ⚠ CROSS-PKG
- HttpEmbedder ──uses──► OpenAIClient  [Services → Transport] ⚠ CROSS-PKG
- HttpStreamingEngine ──uses──► OpenAIClient  [Services → Transport] ⚠ CROSS-PKG
- MemoryService ──uses──► IConfigProvider  [Services → Interfaces] ⚠ CROSS-PKG
- MemoryService ──uses──► IFileSystem  [Services → Interfaces] ⚠ CROSS-PKG
- MemoryService ──uses──► IVectorEmbedder  [Services → Interfaces] ⚠ CROSS-PKG
- MemoryService ──uses──► IVectorStore  [Services → Interfaces] ⚠ CROSS-PKG
- ModelLoadException ──uses──► ModelLoadPhase  [Engine → Engine]
- ParallelToolExecutor ──uses──► EAgentEngine  [Engine → Engine]
- ParallelToolExecutor ──uses──► ToolPolicy  [Engine → Tools] ⚠ CROSS-PKG
- PrefixCachedExtractor ──uses──► IInferenceEngine  [Engine → Interfaces] ⚠ CROSS-PKG
- PrefixCachedExtractor ──uses──► IKvCacheController  [Engine → Interfaces] ⚠ CROSS-PKG
- RemoteKvCacheController ──uses──► OpenAIClient  [Services → Transport] ⚠ CROSS-PKG
- RemoteModelLoader ──uses──► OpenAIClient  [Services → Transport] ⚠ CROSS-PKG
- RemoteTokenizer ──uses──► OpenAIClient  [Services → Transport] ⚠ CROSS-PKG
- ServerLauncher ──uses──► LlmProviderConfig  [Services → Config] ⚠ CROSS-PKG
- SessionBuilder ──uses──► EAgentConfig  [Session → Config] ⚠ CROSS-PKG
- SessionManager ──uses──► EAgentConfig  [Session → Config] ⚠ CROSS-PKG
- StepMapper ──uses──► EAgentEngine  [Engine → Engine]
- SubAgentManager ──uses──► EAgentEngine  [Engine → Engine]
- TestSessionOutput ──uses──► EGuiTestHarness  [Testing → Testing]

## Cross-Package Dependencies

- Config → Interfaces
- Engine → Interfaces, Tools
- Root → Engine
- Services → Config, Interfaces, Transport
- Session → Config, Interfaces
- Tools → Config, Engine, Interfaces, Services
