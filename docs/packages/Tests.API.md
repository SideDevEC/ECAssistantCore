# Tests.API.md

Types: 65  |  LOC: 9242  |  ~2315 tokens

---

### Class: ApiUserController
Cross-package deps: ECAssistant.Core.Analysis

### Class: BackgroundProcessManagerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services

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

### Class: FailureAnalysisTests
Cross-package deps: ECAssistant.Core.Engine

### Class: FailureEntryTests
Cross-package deps: ECAssistant.Core.Engine

### Class: FailurePatternTests
Cross-package deps: ECAssistant.Core.Engine

### Class: FileSystemAdapterTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: HomeController
Cross-package deps: ECAssistant.Core.Analysis

### Class: HttpClientAdapterTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: InMemoryVectorStoreTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

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

### Class: MyTests
Cross-package deps: ECAssistant.Core.Analysis

### Class: OrchestratorIntegrationTests
> Integration tests for the full Orchestrator → Engine → Tools → Output pipeline.
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ParallelToolExecutorIntegrationTests
> Integration tests for ParallelToolExecutor — dependency analysis and parallel
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Testing, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ParallelToolExecutorTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Engine, ECAssistant.Core.Tools, Moq

### Class: ProcessRunnerTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ProgramGuiCollection
> xUnit test collection that serializes tests sharing the static EGuiTestHarness field.

### Class: ProjectContextManagerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: SelfCorrectionManagerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

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

### Class: StepMapperTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools

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

### Class: VectorMemoryStoreTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory, ECAssistant.Core.Services
