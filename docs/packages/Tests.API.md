# Tests.API.md

Types: 134  |  LOC: 15815  |  ~6013 tokens

---

### Class: AiSetupResetterTests
Cross-package deps: ECAssistant.Core.Setup, ECAssistant.Core.Config

### Class: AllToolsTierPromptsTests
> v15: tier-aware tool prompt enforcement (Emre, 2026-09-23) — the abstract base
Cross-package deps: ECAssistant.Core.Tools

### Class: ApiUserController
Cross-package deps: ECAssistant.Core.Analysis

### Class: BackgroundProcessManagerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services

### Class: BuildCallSignatureTests
> v12.4/v12.5 regression: tool-call signatures must be deterministic and
Cross-package deps: ECAssistant.Core.Orchestration, Xunit

### Class: BuildOutputRendererTests
> v14.20: BuildOutputRenderer — semantic render of dotnet build/test output.
Cross-package deps: ECAssistant.Core.Tools.Build

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

### Class: ConfigLoaderTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, Moq

### Class: ConfigProviderTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: ContextManagerTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces, Moq

### Class: ContextPinningTests
> v14.16: tier-aware proactive context pinning — pure logic only (no LLM, no
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.ContextPinning, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces

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

### Class: EDotnetBuildToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Build

### Class: EFileReaderToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Reader

### Class: EFileResearchToolTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Research

### Class: EGitToolTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Git

### Class: EHandoffToolTests
> Unit tests for EHandoffTool — the model-facing handoff tool.
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Handoff

### Class: EMemoryManagerTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory

### Class: EShellAgentTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Shell

### Class: EToolBaseTests
> Concrete subclass for testing EToolBase abstract members.
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Tools

### Class: EToolBaseTierPromptsTests
> v15: tier-aware tool prompt enforcement (Emre, 2026-09-23) — the abstract base
Cross-package deps: ECAssistant.Core.Tools

### Class: EUserAskToolTests
> v14.9 AskUser tool — model-driven ambiguity checkpoint: parses options,
Cross-package deps: ECAssistant.Core.Session, ECAssistant.Core.Tools.User, Xunit

### Class: EVisionStructureToolTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.EVision, ECAssistant.Core.Vision, Moq

### Class: EmbeddingRoutingTests
> Embedding routing: embedding.mode is independent of the main LLM mode.
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Setup, Xunit

### Class: EndpointNormalizerTests
> Tests for base-URL normalization (strip trailing "/v1") and the remote
Cross-package deps: ECAssistant.Core.Setup, ECAssistant.Core.Transport

### Class: EngineTierBehaviorTests
> v14.12: model-tier-adaptive behavior tests at the ENGINE level — verifies the
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Services, ECAssistant.Core.Tools, ECAssistant.TestSupport

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

### Class: FirstRunDetectorTests
> Tests for FirstRunDetector remote-provider awareness: a configured remote
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup

### Class: GuiTestHarnessTests
> Tests for GuiTestHarness — verifies it captures output correctly.
Cross-package deps: ECAssistant.TestSupport, ECAssistant.Core.UI

### Class: HandoffE2E
> v15 ephemeral handoff — end-to-end against a REAL ECAssistantLLM server with
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Orchestration, ECAssistant.Core.Session, ECAssistant.TestSupport

### Class: HandoffIntegrationTests
> Integration tests for the v15 ephemeral handoff: EHandoff tool registration,
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.UI

### Class: HandoffRequestTests
> Unit tests for HandoffRequest — the ephemeral handoff data record.
Cross-package deps: ECAssistant.Core.Engine

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

### Class: JourneySuiteE2E
> v15 rigorous journey E2E — long mixed conversations (chat → tools → chat → tools),
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Session, ECAssistant.Core.Services, ECAssistant.Core.Tools.Build, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Shell, ECAssistant.TestSupport

### Class: JsonEnvelopeFallbackTests
> v14.10.1: when the structured path falls back to text streaming, the model
Cross-package deps: ECAssistant.Core.Engine

### Class: LLMDecisionEnvelopeTests
> v14.18: thinking-only decision envelopes (answer empty, no tool calls) must
Cross-package deps: ECAssistant.Core.Orchestration

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
> Integration tests for the memory pipeline — MemoryManager and VectorMemoryStore
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory, ECAssistant.Core.Services

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

### Class: ModelCatalogTests
> Catalog loading, default generation, validation, first-run detection.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup, Xunit

### Class: ModelInstallerConfigTests
> Config merge behaviour — mmproj wiring, id replacement, config preservation.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup, Xunit

### Class: ModelTierConfigTests
> v14.12: model-tier profile tests — IsLargeRuntime resolution (small/large/auto),
Cross-package deps: ECAssistant.Core.Config

### Class: MyTests
Cross-package deps: ECAssistant.Core.Analysis

### Class: NativeToolCallsAdapterTests
> v14: remote native tool_calls (OpenAI function calling) → decision envelope → LLMDecision.
Cross-package deps: ECAssistant.Core.Engine, Xunit

### Class: OrchestratorIntegrationTests
> Integration tests for the full Orchestrator → Engine → Tools → Output pipeline.
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: OrchestratorV1419Tests
> v14.19.1 unit coverage for the gaps found in the feature audit:
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Orchestration, ECAssistant.Core.Session, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Interfaces, ECAssistant.TestSupport, Moq

### Class: Order
> Harness end-to-end: drives the REAL product stack (AgentSession →
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.Core.Services.Http, ECAssistant.Core.Session, ECAssistant.Core.Transport, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Build, ECAssistant.TestSupport

### Class: ParallelToolExecutorIntegrationTests
> Integration tests for ParallelToolExecutor — dependency analysis and parallel
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ParallelToolExecutorTests
Cross-package deps: ECAssistant.TestSupport, ECAssistant.Core.Engine, ECAssistant.Core.Tools, Moq

### Class: PersistentShellSessionTests
> v15: persistent shell session — working directory and exported env survive
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services.Shell

### Class: PlaybookTests
> v14.14: tier-aware playbook memory. Pure-logic tests — JSON persistence in
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Orchestration, ECAssistant.Core.Playbooks, ECAssistant.Core.Tools, ECAssistant.TestSupport

### Class: PlaybookTitleTests
> v14.19.1: playbook titles use the goal's FIRST SENTENCE only — prompts that
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Playbooks

### Class: PostEditVerifierTests
> v14.13: tier-aware post-edit verification loop. Pure-logic tests — the build/test
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Tools, ECAssistant.Core.Verification, ECAssistant.TestSupport

### Class: ProcessRunnerTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: ProgramGuiCollection
> xUnit test collection that serializes tests sharing the static GuiTestHarness field.

### Class: ProjectContextExclusionTests
> Project-context scan must exclude host runtime/config files — the model should
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Engine

### Class: ProjectContextManagerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: RemoteModelProbePathTests
> Tests for base-URL normalization (strip trailing "/v1") and the remote
Cross-package deps: ECAssistant.Core.Setup, ECAssistant.Core.Transport

### Class: RemoteProviderIntegrationTests
> v14.7: Integration tests for the remote provider path (native OpenAI function calling).
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration, ECAssistant.Core.Tools, Xunit

### Class: RemoteProviderSetupWriterTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup

### Class: RequestChoiceTests
> v14.9 interactive checkpoint (RequestChoice): listener gets the prompt + options,
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Session, Xunit

### Class: SeatbeltShellSandboxTests
> v15: Seatbelt sandbox — profile generation, command wrapping, and LIVE
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Services.Shell

### Class: SecureKeyStoreTests
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services

### Class: SelfCorrectionManagerTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, Moq

### Class: ServerConfigWriterTests
> Core owns the LLM server config: before launch, {llmRoot}/llm-server.json must exist
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services.Http, Xunit

### Class: ServerLauncherResolveTests
> Tests for the standalone ServerLauncher: resolves the server binary from
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services.Http, Xunit

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

### Class: SessionVerbosityDefaultTests
> v14.19 (Emre): sessions run VERBOSE by default — users see tool status,
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Session

### Class: ShellTeardownSweepTests
> v15: persistent shell session — working directory and exported env survive
Cross-package deps: ECAssistant.Core.Services.Shell

### Class: ShellTierPromptOSTests
> v15: EShellAgent prompt matrix — 3 OSes × 2 tiers. The tool's rules are
Cross-package deps: ECAssistant.Core.Services.Shell, ECAssistant.Core.Tools.Shell

### Class: StartupTimeoutDefaultsTests
> v12.8 regression: the installer must detect models already on disk so
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup, Xunit

### Class: SteeringQueueTests
> v14.12.2: mid-run steering seam — single pending slot, newest wins, drained once.
Cross-package deps: ECAssistant.Core.Engine

### Class: StepMapperTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Tools

### Class: StructuredDecisionAdapterTests
> v14: grammar-forced decision envelope → LLMDecision (native JSON pipeline).
Cross-package deps: ECAssistant.Core.Engine, Xunit

### Class: StructuredFallbackResilienceTests
> v14.10.1: a transient null from GenerateStructuredAsync (provider hiccup,
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Config, ECAssistant.Core.Orchestration

### Class: SubAgentBriefBuilderTests
> v14.17: tier-aware sub-agent brief — pure logic only (no LLM, no build, no DB).
Cross-package deps: ECAssistant.Core.Engine

### Class: SubAgentErrorTests
Cross-package deps: ECAssistant.Core.Engine, ECAssistant.Core.Orchestration

### Class: SubAgentIntegrationTests
> Integration tests for sub-agent spawning through the orchestrator.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.UI

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

### Class: TextMatchStrategyTests
> v14.15 fuzzy diff-based edits: layered match strategies, pipeline ordering,
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Tools.Code

### Class: TfidfEmbedderTests
Cross-package deps: ECAssistant.Core.Services, ECAssistant.Core.Interfaces

### Class: TierInferenceTunerTests
> v15: tier inference values are CONFIG-DRIVEN (model_tier.inference overrides
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Interfaces, ECAssistant.Core.Services

### Class: TokenCounterTests
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolCallChainSubstitutionTests
> v14.20: dataflow toolchains — {{N}} reference substitution over prior call
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolCallRequestTests
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolDependencyAnalyzerTests
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolDescriptionScopeTests
> v14.19.1: every user-facing tool description carries an explicit scope —
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Services, ECAssistant.Core.Tools.Background, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Build, ECAssistant.Core.Tools.Git, ECAssistant.Core.Tools.Research, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Reader

### Class: ToolOutputProjectorTests
> v14.12.2: curated tool-output projection — key lines + head/tail instead of a
Cross-package deps: ECAssistant.Core.Engine

### Class: ToolPipelineIntegrationTests
> Integration tests for tools working through the full pipeline:
Implements: IDisposable
Cross-package deps: ECAssistant.Core, ECAssistant.Core.Config, ECAssistant.Core.Engine, ECAssistant.Core.Interfaces, ECAssistant.Core.Orchestration, ECAssistant.Core.Services, ECAssistant.TestSupport, ECAssistant.Core.Tools, ECAssistant.Core.Tools.Shell, ECAssistant.Core.Tools.Code, ECAssistant.Core.Tools.Reader, ECAssistant.Core.UI

### Class: ToolPolicySessionApprovalTests
Cross-package deps: Xunit, ECAssistant.Core.Tools

### Class: ToolPolicyTests
Cross-package deps: ECAssistant.Core.Tools

### Class: ToolRepeatTrackerTests
> Unit tests for the v14.9 orchestrator loop detection (ToolRepeatTracker):
Cross-package deps: ECAssistant.Core.Engine, Xunit

### Class: TranscriptIntegrationTests
> Integration tests for ConversationTranscript persistence —
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Engine

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

### Class: VectorMemorySetupWriterTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup

### Class: VectorMemoryStoreTests
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Memory, ECAssistant.Core.Services

### Class: VisionStructureJsonParserTests
Cross-package deps: ECAssistant.Core.Vision

### Class: WizardCatalogTests
> Wizard rework units: remote catalog fetch fallback, local model discovery.
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Setup, Xunit

### Class: WizardOnDiskDetectionTests
> v12.8 regression: the installer must detect models already on disk so
Implements: IDisposable
Cross-package deps: ECAssistant.Core.Config, ECAssistant.Core.Setup, Xunit
