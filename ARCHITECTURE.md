# ECAssistant — Architecture (as-is)

**Updated:** 2026-09-23 · Naming convention: tool-family classes carry the `E` prefix (class name = wire name); everything else doesn't. `Eca*` types keep the product prefix.
**Status:** ✅ builds 0 errors | 458/458 targeted test net (unit + integration) | LDC regenerated 2026-09-23, enforcement PASSED (506 types Core / 185 LLM / 30 TUI / 13 TestSupport / 4 Console)
**History:** git log — this file describes the CURRENT state only.

## Overview

ECAssistant is a local-first, embeddable .NET 8 AI agent library. LLM inference is **HTTP-based**: Core talks to a separate `ECAssistantLLM` server process (or any OpenAI-compatible endpoint) over HTTP/SSE. There is **no in-process LLamaSharp** — zero native model dependencies.

Core handles multi-session orchestration, sub-agents, ephemeral handoff, vector memory, self-correction, playbooks, and 11 built-in tools. The model, GPU, and KV cache live server-side; Core is a thin HTTP client plus agent logic.

Two provider modes (`LlmProviderConfig.mode`):
- **local** — spawn/connect to the `ECAssistantLLM` server; full KV cache, sessions, tokenizer
- **remote** — any OpenAI-compatible API; stateless, no KV cache

## Model-Tier Awareness

One seam governs behavior for small vs large models: `ModelTier.IsLargeRuntime(isLocal)`.
Config: `model_tier.mode` = `small` | `large` | `auto` (auto: local → small, remote → large).

Tier-resolved behaviors:
- **Preplanning** (`InterfaceConfig.Preplanning`, null=auto): small models decompose the task up front (LLM plan + StepMapper); large models plan in-loop. Conversational questions skip decomposition via the verb gate.
- **Envelope budget** (`ApplyEnvelopeBudget`): token budgets for decision envelopes (small 768/1024, large 1024/4096)
- **Sampling** (`InferenceParamsFactory.CreateTiered`): small tier tightens sampling defaults
- **Sub-agent briefs** (`SubAgentBriefBuilder`): small-tier children get GUIDANCE scaffolding; large-tier children get objective + output contract only
- **Post-edit verification**: small — verify after every file edit (max 2 rounds); large — once per run, trivial edits skipped
- **System prompt**: slim directives for large tier; anti-tool-spam line lives in the live `SystemPrompt.*.md` resources
- **Dataflow toolchains** (`{{N}}` arg references → sequential chain execution): large-tier-only guidance

## Decision Pipeline

Tag-free since v14. `AgentEngine.GenerateAsync()` returns `Task<LLMDecision>` directly:

```
DecisionEnvelope JSON (local: grammar-constrained via GBNF; remote: native OpenAI tool_calls)
  → StructuredDecisionAdapter.ParseDecision() → LLMDecision
  → AgentOrchestrator consumes (tool calls XOR direct answer)
```

- **Grammar tool-name union:** structured requests carry `tool_names`; the server's `DecisionGrammar.BuildGbnf` constrains `toolcall.name` to registered tools only. Absent → permissive grammar (back-compat).
- **Early termination:** the server stops streaming as soon as the envelope parses complete (~70% time saved). `max_tokens` cap 256.
- **Reasoning:** the model's `thinking` is stored as `[reasoning]` in transcript + context (max 1 sentence); `LLMDecision.Reasoning` carries it.
- **Commentary:** remote models may emit text alongside tool calls (`LLMDecision.Commentary`, display-only); local grammar stays strict XOR.
- **Format retries:** empty/undecodable decisions trigger removal + retry (max rounds), then best-effort delivery. Thinking-only envelopes are NOT surfaced as answers.
- **Loop protection:** `ToolRepeatTracker` (nudge on 2nd repeat, alternation detection, stop on 3+), blocked repeat of failed calls, batch-loop signature stop, consecutive-failure stop.

## Orchestration Flow (`AgentOrchestrator.ExecuteMultiStep`)

1. Conversational gate (verb heuristic → LLM 1-token classification) — skips planning for chat
2. Optional preplanning (tier-resolved): `TaskPlanner.Decompose` → `StepMapper` → execution plan; `InteractionConfig.ConfirmPlan` checkpoint
3. Turn loop: `LLMDecision` → direct answer (done) | tool call(s) → policy check → execute (parallel via `ParallelToolExecutor` with dependency grouping) → result rendered for model → loop
4. **Typed per-tool outputs:** every tool has `RenderForModel` — model-facing output is rendered/compacted exactly once in `AddToolResult` (build failures embed compacted logs, paths sanitized)
5. **EHandoff interception:** a tool call named `EHandoff` never executes normally — see Handoff below
6. **Post-edit verification** (`verification` config + `IVerificationRunner`): after file edits, run the verify command; tier-gated (small: every edit, max 2 rounds; large: once per run); disable-on-persistent-failure
7. **Playbook memory** (`IPlaybookStore`): after GoalAchieved runs with tools, successful tool sequences are captured; replayed for similar future requests
8. **Context pinning**: important facts pinned to survive compaction, tier-capped eviction
9. **Steering:** `SteeringQueue` + `AgentSession.Steer()` injects user input at any turn boundary

## Tools (11 built-in)

| Tool | Purpose | Default permission |
|---|---|---|
| EShellAgent | Shell execution (**system-critical** — cannot be disabled) | approval |
| EBackgroundExecTool | Background process execution | approval |
| EDotnetBuildTool | dotnet build/restore/test | allowed |
| EGitTool | Git operations | approval |
| ECodeEditorTool | Code edit (create/patch/search/insert/delete) with fuzzy diff matching (`TextMatchPipeline`) | approval |
| EFileReaderTool | File reads | allowed |
| EFileResearchTool | File research | allowed |
| EVisionStructureTool | Vision structure extraction (vision-capable models only; grammar-forced JSON schema) | allowed |
| EUserAskTool | Model-driven user clarification (gated: `allow_user_ask`) | n/a |
| ESubAgentTool | Sub-agent spawn (result fed back, parent continues) | allowed |
| EHandoffTool | **Ephemeral handoff** — specialist takeover | allowed |

**Naming convention (Emre, 2026-09-23):** tool-family classes keep the `E` prefix (EToolBase, EToolResult, EShellAgent, …) because the class name IS the wire name — grammar union, `tool_permissions` config keys, and stored playbooks all reference it. Non-tool classes are bare (AgentEngine, AppConfig, GuiConsole, AnsiColor, …). `Eca*` types keep the product prefix.

### Handoff (v15, all-ephemeral)

The orchestrator can delegate the ENTIRE remaining task to an ephemeral specialist whose answer becomes the final answer (parent loop stops — unlike sub-agents, where the result returns to the parent).

- `EHandoff` tool: the model invents the specialist at call time (system prompt, tool subset, context summary). Zero system-prompt cost — no specialist list is ever injected.
- `HandoffExecutor` (Engine/Handoff/): creates a child AgentEngine (own KV cache + session), injects `SystemPromptText`, registers a filtered tool subset (never EHandoff on specialists — no recursion), runs a child AgentOrchestrator, returns its result as the parent's own. ESC + timeout propagation. No retry, nothing persisted.
- Specialist uses the SAME endpoint; `model_override` picks a different model id (local multi-model or remote provider).
- `SessionBuilder` always initializes handoff (zero prompt cost — one tool block, no specialist list).

### Sub-agents

`SubAgentManager` (parent → child, parent CONTINUES): max concurrent, per-agent context size, timeout, turn/tool-call/disk limits, retry with backoff, structured errors with partial results, ESC cancellation, file snapshots (created/modified tracking). Tier-aware briefs via `SubAgentBriefBuilder` (objective + OUTPUT CONTRACT; GUIDANCE scaffolding for small tier).

## Session Architecture

Each `AgentSession` owns: AgentEngine (own server-side KV cache session), orchestrator, tools, memory, output buffer, prompt queue, runner thread. Sessions share the same server (one model in VRAM) but are otherwise fully independent. `SessionBuilder` is the public embed API.

`SessionManager` wires shared HTTP infra: `ServerLauncher`, `LlmServerClient`, `OpenAIClient`, `InferenceParamsFactory`, `RemoteTokenizer` (local mode). Remote mode: no launch/registration/heartbeat — per-request connection.

- **Idle watchdog:** after 15 min inactivity → stop heartbeat, `/eca/shutdown`, free VRAM; input resets via `MarkUserActivity()`
- **Reconnection:** idle-disconnect + activity → ensure server, re-register, recreate HTTP client + KV sessions, re-prefill
- **Connection recovery:** connection-refused/HTTP errors in local mode trigger `ConnectionRecovery` (restart server, re-register, retry once); HTTP 404 = stale session → restore KV sessions + retry. Proactive ping on user activity (30s throttle)
- **Heartbeat auto-reconnect:** 3 consecutive heartbeat failures → reconnect; `OnReconnected` fires session restoration
- **Shutdown:** `SessionManager.DisposeAsync()` → disconnect + POST `/eca/shutdown` (server winds down if last client)
- **Idle watchdog / stateless background tasks:** decompose/summarize use `HttpStreamingEngine` stateless mode (no session_id), same server

Engine decisions: `GenerateAsync` → `LLMDecision`; reasoning + commentary stored per turn; early envelope termination server-side; KV-cache prefill of the static prefix once, incremental tokens per turn; rewind on format retry; full reset + re-prefill on overflow.

## Project Structure

```
ECAssistantCore/
├── Analysis/               # ContextAnalyzer (project context extraction)
├── Composition/            # EcaCompositionRoot + EcaServiceBundle
├── Config/                 # AppConfigBuilder (AgentConfigBuilder), ConfigLoader, config models (init-only)
│    └── Models/            # AppConfig (root doc), AgentConfig (agent_settings), LlmProviderConfig, …
├── Engine/                 # AgentEngine, TaskPlanner, StepMapper, ParallelToolExecutor,
│   │                       #   ToolRepeatTracker, ContextWindow, ConversationTranscript,
│   │                       #   SubAgentManager, SubAgentBriefBuilder, SelfCorrectionManager,
│   │                       #   PrefixCachedExtractor, TokenCounter
│   ├── Handoff/            # HandoffRequest (record), HandoffExecutor
│   └── SubAgent/           # SubAgentTask, SubAgentResult, SubAgentError
├── Interfaces/             # 25+ interfaces (IEngine, IInferenceEngine, IKvCacheController,
│                           #   ISubAgentEngineHost, IPostEditVerifier, IPlaybookStore, IVerificationRunner, …)
├── Memory/                 # MemoryManager, VectorMemoryStore
├── Playbooks/              # PlaybookStore (persistent success sequences)
├── Setup/                  # IFirstRunOrchestrator/FirstRunOrchestrator, IServerInstallCoordinator/
│                           #   ServerInstallCoordinator, ISetupWizard/SetupWizard, ISetupUi,
│                           #   FirstRunDetector, ModelInstallerService, NuGetServerFetcher,
│                           #   SecureKeyStore, LlmProviderRegistry, ModelCatalogDocument
├── Services/               # Logger, ContextManager, InferenceParamsFactory, HtmlTextConverter,
│   └── Http/               # HttpStreamingEngine, RemoteKvCacheController, RemoteModelLoader,
│                           #   RemoteTokenizer, HttpEmbedder, LlmServerClient, ServerLauncher,
│                           #   ServerConfigWriter
├── Session/                # AgentSession, SessionManager, SessionBuilder, SessionDiscovery
├── Transport/              # OpenAIClient, SseParser, EndpointNormalizer
├── Tools/                  # EToolBase + tool family (E prefix = wire identity)
│   ├── EBackground/ EBuild/ ECode/ EDotnet/ EGit/ EResearch/ EShell/ EVision/
│   ├── Handoff/            # EHandoffTool
│   ├── Policy/             # ToolPolicy, ToolPermission, ToolPolicyDecision
│   ├── Reader/ SubAgent/ User/
│   ├── ECode/TextMatchPipeline (ITextMatchPipeline) — exact → whitespace-tolerant → line-anchored
│   └── BuildOutputRenderer # MSBuild log compaction + path sanitization
└── UI/                     # GuiBase (terminal abstraction base)

ECAssistantLLM/             # Separate server process — owns model, GPU, KV cache, sessions,
│                           #   DecisionGrammar (GBNF), early envelope termination, multi-model host
ECAssistantTUI/             # AppController + layers (GuiConsole, IGuiConsole, ITerminalOutput)
ECAssistantConsole/         # Thin entry point: setup wizard + AppController
ECAssistantTestSupport/     # MockEngine, GuiTestHarness, ProbeTestTool, HarnessE2ESessionFactory,
                            #   TestRunner/TestScenario (NOT in the production package)
```

## Dependency Flow

```
ECAssistantCore  (no LLamaSharp — HTTP/SSE only)
     │  OpenAIClient → /v1/chat/completions, /v1/embeddings, /eca/*
     ▼
ECAssistantLLM   (model + GPU + KV cache + grammar)
     ▲
ECAssistantTUI   ←── Core (package ref)
     ▼
ECAssistantConsole ←── TUI ONLY (Core flows transitively)
```

## Key Interfaces

| Interface | Implementation | Purpose |
|---|---|---|
| IEngine | AgentEngine | Engine lifecycle |
| IInferenceEngine | HttpStreamingEngine | LLM generation over HTTP (stream/generate → LLMDecision) |
| IKvCacheController | RemoteKvCacheController | Server-side KV cache: create/prefill/rewind/save/reset |
| ILlmServerClient | LlmServerClient | Register/heartbeat/disconnect/reconnect |
| IModelLoader | RemoteModelLoader | Load/unload/list models (`/eca/models`) |
| IVectorEmbedder | HttpEmbedder / TfidfEmbedder | Embeddings (HTTP or local TF-IDF) |
| IToolPolicyEvaluator | ToolPolicy | Permission evaluation |
| ISessionContext | AgentSession | Read-only session context for tools |
| ITaskPlanner / IStepMapper | TaskPlanner / StepMapper | Decomposition + step mapping |
| IParallelToolExecutor | ParallelToolExecutor | Dependency-ordered parallel execution |
| IPostEditVerifier / IVerificationRunner | DotnetVerificationRunner | Post-edit verify loop |
| IPlaybookStore | PlaybookStore | Success-sequence memory |
| ISessionBuilder | SessionBuilder | Session construction (embed API) |
| ISubAgentEngineHost | AgentEngine | Host surface for child engines (InferenceEngine, ExecutionToken) |
| IFirstRunOrchestrator / IServerInstallCoordinator / ISetupWizard | FirstRunOrchestrator / ServerInstallCoordinator / SetupWizard | Setup seams (interface-first) |
| ITextMatchPipeline | TextMatchPipeline | Match strategy chain for code edits |
| IHtmlTextConverter / IReadableContentExtractor | HtmlTextConverter / ReadableContentExtractor | Web content pipeline |
| IProcessRunner / IFileSystem / IHttpClient | ProcessRunner / FileSystemAdapter / HttpClientAdapter | Infra abstractions |

## First-Run Setup

ALL hosts share one Core flow:

```
FirstRunOrchestrator (IFirstRunOrchestrator)
├── FirstRunDetector.Evaluate()   ← disk truth: gguf files, llm-server.json, binary, remote config
├── RunIfNeededAsync()            ← wizard only when state unresolved; never blocks startup
└── SetupWizard (ISetupWizard)    ← ConsoleSetupUi / TuiSetupUi adapters
```

- Server install is **wizard-time and conditional**: `ServerInstallCoordinator.EnsureServerAsync()` only when local chat or local embeddings is picked. Pure-remote = zero LLM footprint.
- `NuGetServerFetcher` downloads the ~170 MB server from nuget.org; packages stay thin (~2.8 MB).
- Version upgrade: VERSION stamp vs `ServerInstallCoordinator.RequiredServerVersion` (keep in sync with llm-server-v* releases).
- Reinstall: stop server → `AiSetupResetter` (config/keys reset, models + binary kept) → wizard re-registers existing models.
- No flag files — state derived from disk, crash-safe.
- Injected factories (coordinator/wizard) support test doubles.

## Tool Permission Policy

Config-driven, two sections in `appsettings.json`:

- **`system_tools`** — system-critical tools, always registered, cannot be disabled (currently: EShellAgent)
- **`tool_permissions`** — optional tools; `approvalRequired` per tool; disable via `tools.<name>.enabled: false`

Enforcement: Blocked tools are NOT registered (LLM never sees them). ApprovalRequired tools prompt `⚠ APPROVAL REQUIRED` before execution. Read-only defaults: allowed. Shell redirection counts as write → approval. Dynamic: any tool name works via config.

## Config (as-is)

- **AppConfig** (root doc) — `llm`, `memory`, `workspace`, `subagent`, `background_tasks`, `tools` (dynamic per-tool sections), `tool_output_limits`, `interaction`, `model_tier`, `context_management`, `verification`, `handoffs` (n/a — handoff is configless)
- **LlmProviderConfig** — `mode` (local/remote), `host:localhost`, `port:8420`, `endpoint`, `api_key`, `model_id`, `embedding_model_id`, `auto_start`, `heartbeat_interval_sec`, …; `ResolvedEndpoint` computed (`http://{host}:{port}` local / endpoint remote)
- **`llm_providers`** — multiple remote providers: `providers[]` (name/endpoint/api_key/model_id/is_default), `default_provider`, `fallback_enabled` (opt-in health-probe failover at session start only). Local mode ignores it.
- **API key schemes** — literal | `file:<path>` | `keyfile:<name>` (SecureKeyStore: self-encrypting DPAPI keyring, atomic writes, name-only references)
- **`model_tier`** — `small|large|auto`; drives preplanning, envelope budgets, sampling, sub-agent briefs, verification cadence
- **Model catalog** — data-driven `model-catalog.json`; live-fetch from GitHub, local copy fallback, embedded default; License self-heal backfill
- AgentConfig (`agent_settings`) — working_directory, execution_timeout_minutes, allow_delete, allowed_extensions

## Core/LLM Boundary Law

Core knows NOTHING about ECAssistantLLM internals — **OpenAI-compatible HTTP endpoints only**. LLM is a self-contained, separately versioned product (no runtime downloads at chat). Vision models always listed with mmproj. Never reintroduce in-process LLamaSharp.

## Test Coverage

| Suite | Count |
|---|---|
| Core unit + integration | ~980 (filtered quick net runs green; full suite only on explicit request) |
| Core live-model E2E | gated behind `ECA_E2E_SERVER` (real server + qwen35-4b journeys incl. handoff) |
| TUI / LLM / Console | separate suites per repo |

`MockEngine` (TestSupport) extends AgentEngine with no-op HTTP transport. `HarnessE2ESessionFactory` builds REAL AgentSession stacks against a live server (env-gated). Process hygiene: never run full `dotnet test` interactively — filtered runs only, `pkill -f "dotnet test"` after.

## Key Constraints

- No static classes, no static mutable state. Instance `Default` shared instances for utilities. Allowed statics: factories on immutable data classes + pure functions (documented).
- Constructor injection throughout; one type per file; config models init-only (2 documented exceptions)
- No LLamaSharp in Core — all inference is HTTP; KV cache control via `IKvCacheController`
- `GuiConsole` uses `ITerminalOutput` (no direct Console.Write)
- `EcaCompositionRoot` is the single wiring point
- **Interface-first:** every public service has an interface (LDC-enforced, 0 warnings)
- **Naming:** `E` prefix = tool family (wire identity); bare names elsewhere; `Eca*` product prefix
- Release discipline: **unified versioning** (Emre, 2026-09-23) — ALL packages (TestSupport, LLM, Core, TUI, Console) carry the SAME version number and ship together in one wave via the single orchestrating workflow `.github/workflows/release-unified.yml` (tag `release-v*` or manual dispatch; checks out all 5 repos side by side, stamps versions + RequiredServerVersion in-workflow, builds with project refs — no CI restore race — packs and pushes all 5). Legacy packages were deprecated + unlisted on nuget.org; the wave starts from a clean slate. Version floor: ≥ 15.0.0 (nuget.org monotonicity — LLM reached 14.9.x).