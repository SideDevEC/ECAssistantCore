# Tests.API.md

Types: 89  |  LOC: 11599  |  ~3264 tokens

---

### Class: AiSetupResetterTests
Cross-package deps: ECAssistant.Core.Setup, ECAssistant.Core.Config

### Class: ApiUserController
Cross-package deps: ECAssistant.Core.Analysis

### Class: BackgroundProcessManagerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services

### Class: BuildCallSignatureTests
> v12.4/v12.5 regression: tool-call signatures must be deterministic and
Cross-package deps: ECAssistant.Core.Orchestration, Xunit

### Class: ConfigIntegrationTests
> Integration tests for the config loading pipeline — uses real FileSystemAdapter
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ConfigLoaderTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, Moq

### Class: ConfigProviderTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: ContextManagerTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: ContextWindowIntegrationTests
> Integration tests for ContextWindow with a real TokenCounter —
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Services

### Class: ContextWindowTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Services

### Class: ConversationTranscriptTests
Cross-package deps: ECAssistant.Core.Engine

### Class: DebugProbeTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Web

### Class: DecisionParserTests
> v12.12 model-agnostic decision parsing — ANY model must produce a usable
Cross-package deps: ECAssistant.Core.Orchestration, Xunit

### Class: DependencyGroupTests
Cross-package deps: ECAssistant.Core.Engine

### Class: EAgentConfigTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config

### Class: EBackgroundExecToolTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Services, ECAssistant.Core.Tools.Background

### Class: ECodeEditorToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Code

### Class: EContextAnalyzerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Analysis

### Class: EDecisionLoopTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: EDotnetBuildToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Build

### Class: EFileAnalyzerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Tools.Example

### Class: EFileReaderToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Reader

### Class: EFileResearchToolTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Research

### Class: EGitToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Git

### Class: EGuiTestHarnessTests
> Tests for EGuiTestHarness — verifies it captures output correctly.
Cross-package deps: ECAssistant.Core.Testing, ECAssistant.Core.UI

### Class: EMemoryManagerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory

### Class: EShellAgentTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Shell

### Class: EToolBaseTests
> Concrete subclass for testing EToolBase abstract members.
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Tools

### Class: EWebFetchToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Web

### Class: EWebSearchToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Web

### Class: EmbeddingRoutingTests
> Embedding routing: embedding.mode is independent of the main LLM mode.
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Setup, Xunit

### Class: FailureAnalysisTests
Cross-package deps: ECAssistant.Core.Engine

### Class: FailureEntryTests
Cross-package deps: ECAssistant.Core.Engine

### Class: FailurePatternTests
Cross-package deps: ECAssistant.Core.Engine

### Class: FakeLlmServer
> Minimal fake of the ECAssistantLLM endpoints used by LlmServerClient:
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services.Http

### Class: FileSystemAdapterTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: HomeController
Cross-package deps: ECAssistant.Core.Analysis

### Class: HtmlTextConverterTests
Cross-package deps: ECAssistant.Core.Services

### Class: HttpClientAdapterTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ImageAttachmentParserTests
> [image:path] attachment extraction — file resolution, mime mapping, error tolerance.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Engine, Xunit

### Class: InMemoryVectorStoreTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: InstallerVisionEmbeddingTests
> Vision-capability + embeddings-mode wiring: mmproj pairing, vision_enabled flag,
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup

### Class: LlmProviderRegistryTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services

### Class: LlmServerClientReconnectTests
> Minimal fake of the ECAssistantLLM endpoints used by LlmServerClient:
Cross-package deps: ECAssistant.Core.Services.Http

### Class: LoggerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, ECAssistant.Core.UI, Moq

### Class: MemoryIntegrationTests
> Integration tests for the memory pipeline — EMemoryManager and VectorMemoryStore
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory, ECAssistant.Core.Services

### Class: MemoryServiceTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: MockSubAgentTool
> Integration tests for sub-agent spawning through the orchestrator.
Implements: EToolBase
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.UI

### Class: ModelCatalogTests
> Catalog loading, default generation, validation, first-run detection.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup, Xunit

### Class: ModelInstallerConfigTests
> Config merge behaviour — mmproj wiring, id replacement, config preservation.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup, Xunit

### Class: MyTests
Cross-package deps: ECAssistant.Core.Analysis

### Class: NativeToolCallsAdapterTests
> v13b: remote native tool_calls (OpenAI function calling) → decision envelope → internal decision text.
Cross-package deps: ECAssistant.Core.Engine, Xunit

### Class: OrchestratorIntegrationTests
> Integration tests for the full Orchestrator → Engine → Tools → Output pipeline.
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ParallelToolExecutorIntegrationTests
> Integration tests for ParallelToolExecutor — dependency analysis and parallel
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ParallelToolExecutorTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools, Moq

### Class: ProcessRunnerTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ProgramGuiCollection
> xUnit test collection that serializes tests sharing the static EGuiTestHarness field.

### Class: ProjectContextManagerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: ReadableContentExtractorTests
Cross-package deps: ECAssistant.Core.Services

### Class: RemoteProviderSetupWriterTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup

### Class: SecureKeyStoreTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services

### Class: SelfCorrectionManagerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: ServerConfigWriterTests
> Core owns the LLM server config: before launch, {llmRoot}/llm-server.json must exist
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services.Http, Xunit

### Class: ServerLauncherResolveTests
> v12.10 runtime contract tests: the integrating app gives Core a root folder; Core
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services.Http, Xunit

### Class: SessionDiscoveryTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Session

### Class: SessionManagementIntegrationTests
> Integration tests for SessionDiscovery — exercises real file system I/O
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Session

### Class: SessionQueueTests
> Tests for session prompt queue logic and run state transitions.
Cross-package deps: ECAssistant.Core.Session

### Class: StartupTimeoutDefaultsTests
> v12.8 regression: the installer must detect models already on disk so
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup, Xunit

### Class: StepMapperTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools

### Class: StructuredDecisionAdapterTests
> v13: grammar-forced decision envelope → internal decision text.
Cross-package deps: ECAssistant.Core.Engine, Xunit

### Class: SubAgentErrorTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration

### Class: SubAgentIntegrationTests
> Integration tests for sub-agent spawning through the orchestrator.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.UI

### Class: SubAgentResultTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration

### Class: SubAgentTaskTests
Cross-package deps: ECAssistant.Core.Engine

### Class: SummaryServiceTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Engine

### Class: SupportsVisionTests
> SupportsVision is the single, mode-independent capability answer for
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, Xunit

### Class: TaskPlannerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: Test
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Research

### Class: Test
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Research

### Class: TfidfEmbedderTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: TokenCounterTests
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolCallRequestTests
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolDependencyAnalyzerTests
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolPipelineIntegrationTests
> Integration tests for tools working through the full pipeline:
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ToolPolicyTests
Cross-package deps: ECAssistant.Core.Tools

### Class: TranscriptIntegrationTests
> Integration tests for ConversationTranscript persistence —
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Engine

### Class: VectorMemorySetupWriterTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup

### Class: VectorMemoryStoreTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory, ECAssistant.Core.Services

### Class: WizardOnDiskDetectionTests
> v12.8 regression: the installer must detect models already on disk so
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup, Xunit
