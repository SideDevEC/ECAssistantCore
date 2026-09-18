# RELATIONSHIP-GRAPH.md — ECAssistantCore

Generated: 2026-09-18T09:57:27.391635+00:00
Edges: 84  |  Packages: 3

---

## ECAssistantCore

- AgentSession ──implements──► ISessionContext (ECAssistantCore)
- AgentSession ──implements──► ISessionOutput (ECAssistantCore)
- AiSetupResetter ──implements──► IAiSetupResetter (ECAssistantCore)
- ConfigLoader ──implements──► IConfigLoader (ECAssistantCore)
- ConfigLoader ──uses──► IFileSystem (ECAssistantCore)
- ConfigProvider ──implements──► IConfigProvider (ECAssistantCore)
- ConfigProvider ──uses──► IFileSystem (ECAssistantCore)
- ConfigProvider ──uses──► IFileSystem (ECAssistantCore)
- ConsoleSetupUi ──implements──► ISetupUi (ECAssistantCore)
- ContextManager ──implements──► IContextManager (ECAssistantCore)
- ContextManager ──uses──► IConfigProvider (ECAssistantCore)
- ContextManager ──uses──► IInferenceEngine (ECAssistantCore)
- EAgentEngine ──implements──► IEngine (ECAssistantCore)
- EAgentEngine ──implements──► IEngineToolContext (ECAssistantCore)
- EAgentEngine ──implements──► ISubAgentEngineHost (ECAssistantCore)
- EAgentEngine ──uses──► IInferenceEngine (ECAssistantCore)
- EAgentEngine ──uses──► IKvCacheController (ECAssistantCore)
- EBackgroundExecTool ──implements──► EToolBase (ECAssistantCore)
- EBackgroundExecTool ──uses──► IFileSystem (ECAssistantCore)
- EBackgroundExecTool ──uses──► IProcessRunner (ECAssistantCore)
- ECodeEditorTool ──implements──► EToolBase (ECAssistantCore)
- ECodeEditorTool ──uses──► IFileSystem (ECAssistantCore)
- EDotnetBuildTool ──implements──► EToolBase (ECAssistantCore)
- EDotnetBuildTool ──uses──► IProcessRunner (ECAssistantCore)
- EFileAnalyzer ──implements──► EToolBase (ECAssistantCore)
- EFileReaderTool ──implements──► EToolBase (ECAssistantCore)
- EFileReaderTool ──uses──► IFileSystem (ECAssistantCore)
- EFileResearchTool ──implements──► EToolBase (ECAssistantCore)
- EFileResearchTool ──uses──► IFileSystem (ECAssistantCore)
- EGitTool ──implements──► EToolBase (ECAssistantCore)
- EGitTool ──uses──► IFileSystem (ECAssistantCore)
- EGitTool ──uses──► IProcessRunner (ECAssistantCore)
- EGuiTestHarness ──implements──► EGuiBase (ECAssistantCore)
- EShellAgent ──implements──► EToolBase (ECAssistantCore)
- EShellAgent ──uses──► IProcessRunner (ECAssistantCore)
- ESubAgentTool ──implements──► EToolBase (ECAssistantCore)
- EWebFetchTool ──implements──► EToolBase (ECAssistantCore)
- EWebFetchTool ──uses──► IHtmlTextConverter (ECAssistantCore)
- EWebFetchTool ──uses──► IHttpClient (ECAssistantCore)
- EWebFetchTool ──uses──► IReadableContentExtractor (ECAssistantCore)
- EWebSearchTool ──implements──► EToolBase (ECAssistantCore)
- EWebSearchTool ──uses──► IHttpClient (ECAssistantCore)
- EcaServiceBundle ──uses──► ILogger (ECAssistantCore)
- EcaServiceBundle ──uses──► ISessionBuilder (ECAssistantCore)
- FileSystemAdapter ──implements──► IFileSystem (ECAssistantCore)
- HtmlTextConverter ──implements──► IHtmlTextConverter (ECAssistantCore)
- HttpClientAdapter ──implements──► IHttpClient (ECAssistantCore)
- HttpEmbedder ──implements──► IVectorEmbedder (ECAssistantCore)
- HttpStreamingEngine ──implements──► IInferenceEngine (ECAssistantCore)
- InMemoryVectorStore ──implements──► IVectorStore (ECAssistantCore)
- LlmProviderRegistry ──implements──► ILlmProviderRegistry (ECAssistantCore)
- LlmServerClient ──implements──► ILlmServerClient (ECAssistantCore)
- Logger ──implements──► ILogger (ECAssistantCore)
- MemoryService ──implements──► IMemoryService (ECAssistantCore)
- MemoryService ──uses──► IConfigProvider (ECAssistantCore)
- MemoryService ──uses──► IFileSystem (ECAssistantCore)
- MemoryService ──uses──► IVectorEmbedder (ECAssistantCore)
- MemoryService ──uses──► IVectorStore (ECAssistantCore)
- MockEngine ──implements──► EAgentEngine (ECAssistantCore)
- MockSubAgentTool ──implements──► EToolBase (ECAssistantCore)
- ModelParamValidator ──implements──► IModelParamValidator (ECAssistantCore)
- NopKvCacheController ──implements──► IKvCacheController (ECAssistantCore)
- ParallelToolExecutor ──implements──► IParallelToolExecutor (ECAssistantCore)
- PrefixCachedExtractor ──uses──► IInferenceEngine (ECAssistantCore)
- PrefixCachedExtractor ──uses──► IKvCacheController (ECAssistantCore)
- ProcessRunner ──implements──► IProcessRunner (ECAssistantCore)
- ReadableContentExtractor ──implements──► IReadableContentExtractor (ECAssistantCore)
- RemoteKvCacheController ──implements──► IKvCacheController (ECAssistantCore)
- RemoteModelLoader ──implements──► IModelLoader (ECAssistantCore)
- RemoteModelProbe ──implements──► IRemoteModelProbe (ECAssistantCore)
- SecureKeyStore ──implements──► ISecureKeyStore (ECAssistantCore)
- SessionBuilder ──implements──► ISessionBuilder (ECAssistantCore)
- SetupWizard ──uses──► ISetupUi (ECAssistantCore)
- StepMapper ──implements──► IStepMapper (ECAssistantCore)
- StepMapper ──uses──► IEngineToolContext (ECAssistantCore)
- SubAgentManager ──uses──► ISubAgentEngineHost (ECAssistantCore)
- TaskPlanner ──implements──► ITaskPlanner (ECAssistantCore)
- TerminalAdapter ──implements──► ITerminal (ECAssistantCore)
- TestSessionOutput ──implements──► ISessionOutput (ECAssistantCore)
- TfidfEmbedder ──implements──► IVectorEmbedder (ECAssistantCore)

## TestSupport

- EGuiTestHarness ──implements──► EGuiBase (ECAssistantCore) ← CROSS-PKG
- MockEngine ──implements──► EAgentEngine (ECAssistantCore) ← CROSS-PKG
- TestSessionOutput ──implements──► ISessionOutput (ECAssistantCore) ← CROSS-PKG

## Tests

- MockSubAgentTool ──implements──► EToolBase (ECAssistantCore) ← CROSS-PKG
