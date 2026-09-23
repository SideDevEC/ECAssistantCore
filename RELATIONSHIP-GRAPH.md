# RELATIONSHIP-GRAPH.md — ECAssistant

Generated: 2026-09-23T22:20:19.541270+00:00
Edges: 159  |  Packages: 8

---

## ECAssistantConsole

- (no outgoing edges)

## ECAssistantCore

- AgentEngine ──implements──► IEngine (ECAssistantCore)
- AgentEngine ──implements──► IEngineToolContext (ECAssistantCore)
- AgentEngine ──implements──► ISubAgentEngineHost (ECAssistantCore)
- AgentEngine ──uses──► IInferenceEngine (ECAssistantCore)
- AgentEngine ──uses──► IKvCacheController (ECAssistantCore)
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
- ContextPinner ──implements──► IContextPinner (ECAssistantCore)
- DotnetVerificationRunner ──implements──► IVerificationRunner (ECAssistantCore)
- DotnetVerificationRunner ──uses──► IProcessRunner (ECAssistantCore)
- EBackgroundExecTool ──implements──► EToolBase (ECAssistantCore)
- EBackgroundExecTool ──uses──► IFileSystem (ECAssistantCore)
- EBackgroundExecTool ──uses──► IProcessRunner (ECAssistantCore)
- ECodeEditorTool ──implements──► EToolBase (ECAssistantCore)
- ECodeEditorTool ──uses──► IFileSystem (ECAssistantCore)
- EDotnetBuildTool ──implements──► EToolBase (ECAssistantCore)
- EDotnetBuildTool ──uses──► IProcessRunner (ECAssistantCore)
- EFileReaderTool ──implements──► EToolBase (ECAssistantCore)
- EFileReaderTool ──uses──► IFileSystem (ECAssistantCore)
- EFileResearchTool ──implements──► EToolBase (ECAssistantCore)
- EFileResearchTool ──uses──► IFileSystem (ECAssistantCore)
- EGitTool ──implements──► EToolBase (ECAssistantCore)
- EGitTool ──uses──► IFileSystem (ECAssistantCore)
- EGitTool ──uses──► IProcessRunner (ECAssistantCore)
- EHandoffTool ──implements──► EToolBase (ECAssistantCore)
- EShellAgent ──implements──► EToolBase (ECAssistantCore)
- EShellAgent ──uses──► IProcessRunner (ECAssistantCore)
- ESubAgentTool ──implements──► EToolBase (ECAssistantCore)
- EUserAskTool ──implements──► EToolBase (ECAssistantCore)
- EUserAskTool ──uses──► ISessionOutput (ECAssistantCore)
- EVisionStructureTool ──implements──► EToolBase (ECAssistantCore)
- EVisionStructureTool ──uses──► IInferenceEngine (ECAssistantCore)
- EVisionStructureTool ──uses──► IPdfPageRenderer (ECAssistantCore)
- EcaServiceBundle ──uses──► ILogger (ECAssistantLLM) ← CROSS-PKG
- EcaServiceBundle ──uses──► ISessionBuilder (ECAssistantCore)
- ExactMatchStrategy ──implements──► ITextMatchStrategy (ECAssistantCore)
- FileSystemAdapter ──implements──► IFileSystem (ECAssistantCore)
- FirstRunOrchestrator ──implements──► IFirstRunOrchestrator (ECAssistantCore)
- FirstRunOrchestrator ──uses──► ISetupUi (ECAssistantCore)
- FirstRunOrchestrator ──uses──► ISetupUi (ECAssistantCore)
- HandoffExecutor ──uses──► ILogger (ECAssistantLLM) ← CROSS-PKG
- HandoffExecutor ──uses──► ISubAgentEngineHost (ECAssistantCore)
- HttpClientAdapter ──implements──► IHttpClient (ECAssistantCore)
- HttpEmbedder ──implements──► IVectorEmbedder (ECAssistantCore)
- HttpStreamingEngine ──implements──► IInferenceEngine (ECAssistantCore)
- InMemoryVectorStore ──implements──► IVectorStore (ECAssistantCore)
- LineAnchoredMatchStrategy ──implements──► LineMatchStrategyBase (ECAssistantCore)
- LineMatchStrategyBase ──implements──► ITextMatchStrategy (ECAssistantCore)
- LlmProviderRegistry ──implements──► ILlmProviderRegistry (ECAssistantCore)
- LlmServerClient ──implements──► ILlmServerClient (ECAssistantCore)
- Logger ──implements──► ILogger (ECAssistantLLM) ← CROSS-PKG
- McpHttpSseClient ──implements──► IMcpClient (ECAssistantCore)
- McpHttpSseClient ──uses──► ILogger (ECAssistantLLM) ← CROSS-PKG
- McpStdioClient ──implements──► IMcpClient (ECAssistantCore)
- McpStdioClient ──uses──► ILogger (ECAssistantLLM) ← CROSS-PKG
- McpToolAdapter ──implements──► EToolBase (ECAssistantCore)
- McpToolAdapter ──uses──► IMcpClient (ECAssistantCore)
- MemoryService ──implements──► IMemoryService (ECAssistantCore)
- MemoryService ──uses──► IConfigProvider (ECAssistantCore)
- MemoryService ──uses──► IFileSystem (ECAssistantCore)
- MemoryService ──uses──► IVectorEmbedder (ECAssistantCore)
- MemoryService ──uses──► IVectorStore (ECAssistantCore)
- MockProbeTool ──implements──► EToolBase (ECAssistantCore)
- MockSubAgentTool ──implements──► EToolBase (ECAssistantCore)
- ModelParamValidator ──implements──► IModelParamValidator (ECAssistantCore)
- NopKvCacheController ──implements──► IKvCacheController (ECAssistantCore)
- ParallelToolExecutor ──implements──► IParallelToolExecutor (ECAssistantCore)
- PersistentPowerShellSession ──implements──► IShellSession (ECAssistantCore)
- PersistentShellSession ──implements──► IShellSession (ECAssistantCore)
- PlaybookExtractor ──implements──► IPlaybookExtractor (ECAssistantCore)
- PlaybookStore ──implements──► IPlaybookStore (ECAssistantCore)
- PostEditVerifier ──implements──► IPostEditVerifier (ECAssistantCore)
- PostEditVerifier ──uses──► IVerificationRunner (ECAssistantCore)
- ProcessRunner ──implements──► IProcessRunner (ECAssistantCore)
- RemoteKvCacheController ──implements──► IKvCacheController (ECAssistantCore)
- RemoteModelLoader ──implements──► IModelLoader (ECAssistantCore)
- RemoteModelProbe ──implements──► IRemoteModelProbe (ECAssistantCore)
- SeatbeltShellSandbox ──implements──► IShellSandbox (ECAssistantCore)
- SecureKeyStore ──implements──► ISecureKeyStore (ECAssistantCore)
- ServerInstallCoordinator ──implements──► IServerInstallCoordinator (ECAssistantCore)
- ServerInstallCoordinator ──uses──► ISetupUi (ECAssistantCore)
- SessionBuilder ──implements──► ISessionBuilder (ECAssistantCore)
- SetupWizard ──implements──► ISetupWizard (ECAssistantCore)
- SetupWizard ──uses──► ISetupUi (ECAssistantCore)
- ShellSessionFactory ──implements──► IShellSessionFactory (ECAssistantCore)
- SipsPdfPageRenderer ──implements──► IPdfPageRenderer (ECAssistantCore)
- SipsPdfPageRenderer ──uses──► IProcessRunner (ECAssistantCore)
- StepMapper ──implements──► IStepMapper (ECAssistantCore)
- StepMapper ──uses──► IEngineToolContext (ECAssistantCore)
- SubAgentManager ──uses──► ISubAgentEngineHost (ECAssistantCore)
- TaskPlanner ──implements──► ITaskPlanner (ECAssistantCore)
- TerminalAdapter ──implements──► ITerminal (ECAssistantCore)
- TextMatchPipeline ──implements──► ITextMatchPipeline (ECAssistantCore)
- TfidfEmbedder ──implements──► IVectorEmbedder (ECAssistantCore)
- WhitespaceTolerantMatchStrategy ──implements──► LineMatchStrategyBase (ECAssistantCore)

## ECAssistantLLM

- ClientManager ──implements──► IClientManager (ECAssistantLLM)
- ClientManager ──uses──► ILogger (ECAssistantLLM)
- ClientManager ──uses──► ILogger (ECAssistantLLM)
- InferenceScheduler ──implements──► IInferenceScheduler (ECAssistantLLM)
- InferenceScheduler ──uses──► ILogger (ECAssistantLLM)
- LlmHttpServer ──uses──► IClientManager (ECAssistantLLM)
- LlmHttpServer ──uses──► IInferenceScheduler (ECAssistantLLM)
- LlmHttpServer ──uses──► ILogger (ECAssistantLLM)
- ModelSlot ──uses──► ILogger (ECAssistantLLM)
- MultiModelHost ──uses──► ILogger (ECAssistantLLM)
- ProcessModelHost ──implements──► IProcessModelHost (ECAssistantLLM)
- ProcessModelHost ──uses──► ILogger (ECAssistantLLM)
- ProcessModelInstance ──uses──► ILogger (ECAssistantLLM)
- ProcessSession ──uses──► ILogger (ECAssistantLLM)
- ProcessSession ──uses──► IProcessModelHost (ECAssistantLLM)
- ProcessSessionRegistry ──uses──► ILogger (ECAssistantLLM)
- ProcessSessionRegistry ──uses──► IProcessModelHost (ECAssistantLLM)
- RequestRouter ──implements──► IRequestRouter (ECAssistantLLM)
- RequestRouter ──uses──► IClientManager (ECAssistantLLM)
- RequestRouter ──uses──► IInferenceScheduler (ECAssistantLLM)
- RequestRouter ──uses──► ILogger (ECAssistantLLM)
- ServerLogger ──implements──► ILogger (ECAssistantLLM)
- SessionContext ──uses──► ILogger (ECAssistantLLM)
- SessionRegistry ──uses──► IInferenceScheduler (ECAssistantLLM)
- SessionRegistry ──uses──► IInferenceScheduler (ECAssistantLLM)
- SessionRegistry ──uses──► ILogger (ECAssistantLLM)
- SessionRegistry ──uses──► ILogger (ECAssistantLLM)

## ECAssistantTUI

- AppController ──implements──► IAppController (ECAssistantTUI)
- AppController ──uses──► IGuiConsole (ECAssistantTUI)
- AppController ──uses──► IGuiConsole (ECAssistantTUI)
- AppController ──uses──► IGuiConsole (ECAssistantTUI)
- AppController ──uses──► ILogger (ECAssistantLLM) ← CROSS-PKG
- AppController ──uses──► ILogger (ECAssistantLLM) ← CROSS-PKG
- AppController ──uses──► ILogger (ECAssistantLLM) ← CROSS-PKG
- ConfigLayer ──implements──► BaseLayer (ECAssistantTUI)
- ConsoleTerminalOutput ──implements──► ITerminalOutput (ECAssistantTUI)
- ConsoleUiRenderer ──implements──► IOutputListener (ECAssistantCore) ← CROSS-PKG
- GuiConsole ──implements──► GuiBase (ECAssistantCore) ← CROSS-PKG
- GuiConsole ──implements──► IGuiConsole (ECAssistantTUI)
- GuiConsole ──uses──► ITerminalOutput (ECAssistantTUI)
- HelpLayer ──implements──► BaseLayer (ECAssistantTUI)
- LoadingIndicator ──uses──► IGuiConsole (ECAssistantTUI)
- SessionLayer ──implements──► BaseLayer (ECAssistantTUI)
- StartupLayer ──implements──► BaseLayer (ECAssistantTUI)
- StatusIndicator ──uses──► IGuiConsole (ECAssistantTUI)
- TuiSetupUi ──implements──► ISetupUi (ECAssistantCore) ← CROSS-PKG
- TuiSetupUi ──uses──► IGuiConsole (ECAssistantTUI)

## ECAssistantTestSupport

- GuiTestHarness ──implements──► GuiBase (ECAssistantCore) ← CROSS-PKG
- InferenceEngineNoop ──implements──► IInferenceEngine (ECAssistantCore) ← CROSS-PKG
- KvCacheNoop ──implements──► IKvCacheController (ECAssistantCore) ← CROSS-PKG
- MockEngine ──implements──► AgentEngine (ECAssistantCore) ← CROSS-PKG
- ProbeTestTool ──implements──► EToolBase (ECAssistantCore) ← CROSS-PKG
- TestSessionOutput ──implements──► ISessionOutput (ECAssistantCore) ← CROSS-PKG
- UserExperienceHarness ──implements──► IOutputListener (ECAssistantCore) ← CROSS-PKG

## TestModelLoad

- (no outgoing edges)

## Tests

- (no outgoing edges)

## grammar-decision

- (no outgoing edges)
