# ECAssistant — Architecture

**Updated:** 2026-09-22 (late afternoon — TRANSIENT-NULL FALLBACK FIX: envelope==null from GenerateStructuredAsync no longer flips _useStructuredDecoding off PERMANENTLY — one malformed/empty structured reply (provider hiccup, context pressure) downgraded the whole session to raw text streaming (live regression on local qwen3.5-4b: raw JSON envelopes, toolcalls rendered as text, never recovered). Null is now a per-turn fallback; permanent fallback stays reserved for legacy-endpoint codes 404/405/501. Combined with the earlier JSON-envelope fallback parser, fallback turns now render clean answers AND execute toolcalls. StructuredFallbackResilienceTests + JsonEnvelopeFallbackTests. Previously: JSON-ENVELOPE FALLBACK FIX
**Updated:** 2026-09-22 (EVisionStructure tool (v14.10): vision-based UI/document structure extraction — image files (png/jpg/jpeg/webp/gif/bmp) or PDF page → fixed, versioned JSON (`Vision/VisionStructureResult`, schemaVersion "1.0"): elements (header/label/button/input/checkbox/radio/select/table/image/text/other) with approximate bbox + confidence + label↔control `associatedWith`, semantic `groups` (form/section/toolbar/list/table/other), warnings. Never-null design: missing fields get defaults, unknown enums map to Other, dangling refs stripped — corrections reported as non-fatal issues by `VisionStructureJsonParser` (static pure; only unparseable output fails). Prompt single-sourced in `VisionStructurePromptBuilder`. PDF path: `IPdfPageRenderer` interface + `SipsPdfPageRenderer` (macOS sips, page 1 only — swap in a full rasterizer without touching callers). Tool registered in SessionBuilder only when `config.SupportsVision`; uses the session's `IInferenceEngine` with `ImageDataUris`. Tests: VisionStructureJsonParserTests ×9 + EVisionStructureToolTests ×12 — 21/21 green. Follow-up (same day): VisionStructureGrammar (compact GBNF, NO ws rule — permissive ws lets the sampler degenerate into endless whitespace runs at delimiters and exhaust the budget) + InferenceRequestParams.Grammar + HttpStreamingEngine sends `grammar` field — server-side token enforcement of the schema; live E2E qwen35-4b: complete schema-shaped JSON in 8.6s (requires ECAssistantLLM ab57f51). NOT SHIPPED — commit/push only, no tag)
**Updated:** 2026-09-22 (late morning — REVERTED per Emre: the heuristic project-context gate (IsProjectRelatedRequest) and the system-prompt date injection are REMOVED — restore original behavior: project summary injected unconditionally on turn 1, prompts back to v7 base, SystemPromptDateSectionTests + ProjectContextGateTests deleted. STAYS: preplanning default true (planner pre-passes restored — that is the real fix for freestyle picking), grammar coverage for all 11 tools, tool renames, web-tool removal, ConfigLoader legacy-section migration, TUI whitelist guard. Lesson logged: band-aid heuristics on prompt content were the wrong layer — structure (planner) was the fix. Previously: PREPLANNING DEFAULT REVERTED TO TRUE (Emre's diagnosis, confirmed in code): the harness-optimization batch (P2, 2026-09-21 night) defaulted interface.preplanning=false — the Decompose+StepMapper pre-passes were skipped and the loop model planned freestyle, which is the direct enabler of the project-folder obsession + redundant tool calls on trivial questions. Explicit step lists constrain picking. Conversational questions still skip decomposition via the LooksConversational verb gate (zero cost for chat). HarnessOptimizationTests updated for the new default. Previously: PROJECT-CONTEXT INJECTION GATED (live regression from Emre's test): GetProjectContextInjection ignored its userRequest param and injected a 30-file project summary into EVERY conversation's turn 1 — the model fixated on the folder. Now: IsProjectRelatedRequest heuristic (pure static: extensions + task-domain keywords) gates the injection; conversational turns get nothing, project tasks get the full summary. ProjectContextGateTests x17 (conversational negatives, project positives). Combined with the date injection, simple questions now run tool-free and folder-free. Previously: SYSTEM PROMPT date injection: EAgentEngine appends "## CURRENT DATE & TIME" (local date/time + answer-directly guidance) to the system prompt on load — kills the "model dumps the project folder to answer what-day-is-it" class (observed live: 3 EShellAgent calls incl. a full folder dump for a date question). Also prompt rules: answer-directly list (date/time, math, greetings, general knowledge) + a date negative example. SystemPromptDateSectionTests x2. NOTE: date refreshes on engine start — long sessions across midnight see stale date (acceptable). Previously: GRAMMAR COVERAGE COMPLETE: all 11 built-in tools now return typed GetParameterSchema (added: EBackgroundExec action/command/id, EGitTool action/files/message/max_entries/branch, ESubAgentTool task/working_dir/tools/context_size/max_turns/timeout/max_retries, EFileAnalyzer filePath) — native function-calling path grammar-constrains every tool; ParameterSchema_IsTyped guard tests added. Also: web-tools removal + renames + ConfigLoader migration + TUI whitelist guard (see previous entry). All builds 0 errors; LDC enforcement PASSED. NOT SHIPPED — no tag)
**Status:** ✅ 0 errors, 0 warnings | LDC enforcement PASSED

**Updated:** 2026-09-22 (morning — REMOVED web tools EWebSearch + EWebFetch at Emre's call: didn't work reliably, no product value. Deleted: Tools/EWeb, Tools/Web, ReadableContentExtractor, HtmlTextConverter, IReadableContentExtractor, IHtmlTextConverter + their tests; SessionBuilder/SubAgentManager/TestRunner registrations; ToolPolicy entries; SystemPrompt.*.md web rows; ToolsConfig comment example. Tool count now **11 built-in tools** — READMEs (Core/Console/landing) + docs/tools-and-permissions.md aligned. TestSupport and Core also got tool renames DotnetBuild→EDotnetBuild, AskUser→EAskUser (tool Name strings + ToolPolicy keys + test assertions). Hygiene follow-ups (same day): (1) ConfigLoader.MigrateLegacyToolKeys — on-load migration inside merged JSON: legacy tool sections renamed (DotnetBuild→EDotnetBuild, AskUser→EAskUser; legacy key always pruned, new section wins) + removed-tool sections (EWebSearch/EWebFetch) pruned — ConfigLoaderTests ×2 added, 15/15 green; (2) mystery solved: missing llm-server.json was AiSetupResetter.Reset() deleting it BY DESIGN (AI-setup reset flow ran between sessions) — no bug; (3) multi-page PDF: DEFERRED — needs a rasterizer dependency (PDFium) or platform tool, Emre's call on adding deps. All builds 0 errors; suites 246/246 quick tests; LDC enforcement PASSED. NOT SHIPPED — no tag)
**Status:** ✅ 0 errors, 0 warnings | LDC enforcement PASSED

## Overview

ECAssistant is a local-first AI agent framework. LLM inference is **HTTP-based**: Core talks to a separate `ECAssistantLLM` server process (or any OpenAI-compatible endpoint) over HTTP/SSE. There is **no in-process LLamaSharp** — zero LLamaSharp dependency in `ECAssistant.Core`.

Core handles multi-session orchestration, sub-agents, vector memory, self-correction, 12 built-in tools, and a TUI layer. The model, GPU layers, and KV cache live server-side; Core is a thin HTTP client plus agent logic.

Two provider modes (`LlmProviderConfig.mode`):
- **local** — spawn/connect to `ECAssistantLLM` server; full KV cache, sessions, tokenizer
- **remote** — connect to any OpenAI-compatible API (e.g. `gpt-4o`, `deepseek-chat`); stateless, no KV cache

## OOP Principles

- **Encapsulation:** Config models are init-only (immutable); 2 documented exceptions for builder-mutated properties
- **No globals or statics:** Dependencies injected via constructors. No static classes, no static mutable state. Utility classes (StringUtil, InferenceParamsFactory, ResourceLoader, AgentConfigBuilder) are instance classes with `Default` shared instance. The only allowed static methods are factory methods on immutable data classes (EToolResult.Success, TranscriptMessage.User, ToolPermissionRecord construction, AgentConfigBuilder.Create, etc.) and pure protected instance helpers on EToolBase (ReadConfig, ReadCfg, IsToolEnabled)
- **No cross-dependencies:** Layers depend only on the layer below
- **Single responsibility:** One type per file, one interface = one concern
- **Modular & interchangeable:** Every service behind an interface, mockable via Moq
- **Testable by design:** 977 unit + integration tests; MockEngine/TestRunner live in the TestSupport project (kept out of the production package)
- **Composition root:** Central `EcaCompositionRoot` wires all services
- **Terminal abstraction:** `ITerminalOutput` decouples TUI from `System.Console`

## Project Structure

```
ECAssistantCore/            # Core engine, tools, sessions, memory (168 .cs files + 59 test files)
├── Analysis/               # Project context analysis (EContextAnalyzer)
├── Composition/            # EcaCompositionRoot + EcaServiceBundle
├── Config/                 # AgentConfigBuilder, ConfigLoader, 17 config models (init-only)
│    └── Models/            # LlmProviderConfig, LlmServerEndpointConfig, LlmConfig, InferenceConfig, ...
├── Engine/                 # EAgentEngine (IEngine), PrefixCachedExtractor, TaskPlanner
│    ├── SelfCorrection/    # FailureAnalysis, FailurePattern, FileSnapshot
│    ├── SubAgent/          # SubAgentTask, SubAgentResult, SubAgentError
│    ├── PrefixCachedExtractor.cs  # HTTP KV-cache reuse for long-lived extraction tasks
│    └── TokenCounter.cs    # Wraps RemoteTokenizer (HTTP /eca/tokenize)
├── Interfaces/             # 25 interfaces (IEngine, IInferenceEngine, IKvCacheController, ILlmServerClient, IConfigLoader, IModelParamValidator, IStepMapper, IParallelToolExecutor, ITaskPlanner, ISessionBuilder, IHtmlTextConverter, IReadableContentExtractor, ...)
├── Memory/                 # EMemoryManager, VectorMemoryStore
├── Services/               # Service implementations (Logger, ContextManager, InferenceParamsFactory, etc.)
│    ├── HtmlTextConverter.cs       # IHtmlTextConverter — block-tag-aware HTML→text
│    ├── ReadableContentExtractor.cs # IReadableContentExtractor — article/main extraction, boilerplate stripping
│    └── Http/              # HTTP-based services (see below)
├── Session/                # AgentSession, SessionManager, SessionBuilder, SessionDiscovery
│    └── ISessionContext    # Exposes Memory/VectorMemory/BackgroundTasks (NOT SharedWeights/SharedModelParams)
├── Transport/              # OpenAIClient (HttpClient wrapper), SseParser (SSE token stream)
└── Tools/                  # EToolBase + 12 built-in tools
     ├── EBackground/       # Background process execution
     ├── Build/             # BuildErrorParser (extracted from EDotnetBuildTool)
     ├── ECode/             # Code editor (create, patch, diff, search, insert, delete)
     ├── EDotnet/           # dotnet build/restore/test
     ├── EGit/              # Git operations
     ├── EResearch/         # File research
     ├── EShell/            # Shell command execution
     ├── EWeb/              # Web search
     ├── Policy/            # ToolPermission, ToolPolicyDecision
     ├── Reader/            # File reader
     ├── SubAgent/          # Sub-agent tool
     └── EToolResult.cs     # Tool call result (split from EToolBase.cs)

Services/Http/  (HTTP transport layer — talks to ECAssistantLLM / OpenAI-compatible API)
├── HttpStreamingEngine.cs    # IInferenceEngine — stream/generate via /v1/chat/completions (SSE)
├── RemoteKvCacheController.cs # IKvCacheController — session prefill/rewind/save/reset via /eca/sessions/*
├── RemoteModelLoader.cs       # IModelLoader — load/unload/list models via /eca/models
├── RemoteTokenizer.cs         # HTTP /eca/tokenize (char-based fallback if server down)
├── HttpEmbedder.cs            # IVectorEmbedder — embeddings via /v1/embeddings
├── LlmServerClient.cs         # ILlmServerClient — register/heartbeat/disconnect/reconnect (/eca/clients)
├── ServerLauncher.cs          # Detect/launch ECAssistantLLM server, send /eca/shutdown on stop
│                               # Root-only contract: server runtime lives ONLY in {appRoot}/server
│                               # (copied from Core's own bundled server/ when missing/stale).
│                               # Launches with --root {appRoot}/llm + explicit config path
│                               # ({llmRoot}/llm-server.json). No dev-tree/CWD fallbacks —
│                               # dev-tree locations are explicitly ignored.
├── ServerConfigWriter.cs      # Core-owned server config: EnsureServerConfig guarantees
│                               # {llmRoot}/llm-server.json matches appsettings wizard selections
│                               # (chat/vision/embedding model entries, root-contained paths only)

ECAssistantLLM/             # Separate server process — owns the model, GPU, KV cache (NOT part of Core)

ECAssistantTUI/             # Terminal UI layer (12 files + 4 test files)
├── Controller/             # AppController
└── UI/                     # BaseLayer, SessionLayer, ConfigLayer, HelpLayer, LoadingIndicator, StartupLayer
     ├── IGuiConsole.cs     # Console interface
     ├── EGuiConsole.cs     # Full TUI implementation (uses ITerminalOutput)
     ├── ITerminalOutput.cs # Terminal output abstraction
     └── ConsoleTerminalOutput.cs   # Default System.Console implementation

ECAssistantConsole/         # Console entry point (1 file)
└── Program.cs              # Uses EcaCompositionRoot, runs AppController
```

## Dependency Flow

```
ECAssistantCore (NO LLamaSharp — only Microsoft.Extensions.Logging.Abstractions + System.Text.Json)
         │  HTTP/SSE  (OpenAIClient → /v1/chat/completions, /eca/*)
         ▼
ECAssistantLLM  (separate process — owns model, GPU layers, KV cache, sessions)
         ▲
ECAssistantTUI   ←── [Core DLL]   (LLamaSharp refs in TUI/Console csproj are vestigial; Core itself is clean)
         ▼
ECAssistantConsole ←── [Core DLL, TUI DLL]
```

- **Core** has zero LLamaSharp dependency. All inference crosses a process boundary via HTTP.
- **Local mode:** Core → ECAssistantLLM (full KV cache, sessions, tokenizer via `/eca/*` extension endpoints).
- **Remote mode:** Core → any OpenAI-compatible API (stateless, no `/eca/*` endpoints, no KV cache).

## Key Interfaces (25)

| Interface | Implementation | Purpose |
|-----------|---------------|---------|
| IEngine | EAgentEngine | Engine lifecycle (Start, Run, Dispose) |
| IInferenceEngine | HttpStreamingEngine | LLM text generation over HTTP (StreamAsync / GenerateAsync) |
| IKvCacheController | RemoteKvCacheController | Server-side KV cache: session create/prefill/rewind/save/reset/status |
| ILlmServerClient | LlmServerClient | Server client lifecycle: register / heartbeat / disconnect |
| IModelLoader | RemoteModelLoader | Load/unload/list models via HTTP (`/eca/models`) |
| IVectorEmbedder | HttpEmbedder (local) / TfidfEmbedder | Vector embeddings (HTTP `/v1/embeddings` or local TF-IDF) |
| ILogger | Logger | Structured file logging |
| IMemoryService | MemoryService | Persistent memory with search |
| IContextManager | ContextManager | Context window management |
| IConfigProvider | ConfigProvider | Config value access |
| IFileSystem | FileSystemAdapter | File operations abstraction |
| IHttpClient | HttpClientAdapter | HTTP requests (browser-like headers, header-aware GET) |
| ITerminal | TerminalAdapter | Terminal operations |
| IProcessRunner | ProcessRunner | Process execution |
| IVectorStore | InMemoryVectorStore | Vector storage |
| IOutputRenderer | ConsoleUiRenderer | Output rendering |
| IToolPolicyEvaluator | ToolPolicy | Tool permission evaluation |
| ISessionContext | AgentSession | Read-only session context exposed to tools |
| IConfigLoader | ConfigLoader | JSON config file loading with embedded fallback |
| IAiSetupResetter | AiSetupResetter | First-run setup reset (config/keys/server config) |
| IModelParamValidator | ModelParamValidator | Pre-flight model parameter validation |
| IStepMapper | StepMapper | Maps decomposed sub-tasks to concrete tool calls via LLM |
| IParallelToolExecutor | ParallelToolExecutor | Dependency-ordered parallel tool execution with policy gates |
| ITaskPlanner | TaskPlanner | Decomposes complex requests into tracked sub-tasks |
| ISessionBuilder | SessionBuilder | Builds and initializes AgentSessions with standard tools |
| IHtmlTextConverter | HtmlTextConverter | HTML→plain text with block-level structure preserved |
| IReadableContentExtractor | ReadableContentExtractor | Extracts main content from HTML (article/main/body+boilerplate-strip) |

**Transport (not interfaces, concrete classes in `Transport/`):**
- `OpenAIClient` — `HttpClient` wrapper (PostJson/GetJson/Delete/PostStream/Ping), `X-Client-Id` header
- `SseParser` — static `ParseTokenStreamAsync` extracts `choices[0].delta.content` from SSE until `data: [DONE]`
- `EndpointNormalizer` — pure utility, strips a trailing `/v1` from base URLs so users may type endpoints with or without a version suffix; used by OpenAIClient + ServerConnection

## Session Architecture

Each `AgentSession` has:
- Own EAgentEngine (own **server-side** KV cache via HTTP session, not an in-process LLamaContext)
- Own orchestrator, tools, memory, output buffer, prompt queue, runner thread
- Sessions share the **same ECAssistantLLM server** (one model in VRAM) but are otherwise fully independent
- `SessionBuilder` — public API for external consumers (e.g., ECSQL)
- `ISessionContext` exposes `Memory` + `VectorMemory` + `BackgroundTasks` (**`SharedWeights`/`SharedModelParams` REMOVED** — the server owns the model)

SessionManager wires HTTP infra (shared across all sessions): `ServerLauncher`, `LlmServerClient`, `OpenAIClient`, `InferenceParamsFactory`, `RemoteTokenizer` (local mode only).
- `SessionManager` uses `ServerLauncher.EnsureServerRunningAsync` to detect/start the server, then `LlmServerClient.ConnectAsync` to register + start heartbeat.
- Remote mode: no server launch/registration/heartbeat — connection is per-request.
- `AgentSession` no longer takes `LLamaWeights`/`ModelParams` — it takes `IInferenceEngine` + `IKvCacheController` (+ session id).
- **Idle watchdog:** `StartIdleWatchdog(15)` checks every 60s; after 15 min inactivity → stops heartbeat, sends `/eca/shutdown`, frees VRAM. `MarkUserActivity()` on input resets timer.
- **Reconnection:** `MarkUserActivity()` when idle-disconnected → `ReconnectAfterIdleAsync()` → ensures server running → re-registers → recreates HTTP client → restarts heartbeat → recreates KV cache sessions → re-prefills static prefix.
- **Server-down recovery (v12.11):** connection-refused / HttpRequestException in local mode never reaches the LLM parse pipeline. `EAgentEngine` runs `ConnectionRecovery` (wired by SessionManager): restarts server via `EnsureServerRunningAsync`, re-registers, retries the request once. `MarkUserActivityAsync` (throttled 30s) proactively pings the server at user activity and recovers even when not idle-flagged. v12.12: HTTP 404 (`IsStaleSessionFailure`) is also recoverable — recovery restores KV sessions via `RestoreSessionsAsync()` (both reconnect and fast path) before the retry.
- **Heartbeat auto-reconnect:** `LlmServerClient` tracks consecutive failures; after 3 failures → `TryReconnectAsync()` re-registers. `OnReconnected` event fires for session restoration.
- **Shutdown wiring:** `AppController` calls `SessionManager.DisposeAsync()` on quit → `DisconnectAsync()` + `ServerLauncher.StopServerAsync()` → POST `/eca/shutdown` → server winds down if last client.
- **Session restore:** `AgentSession.UpdateClientId()` + `RecreateKvCacheSessionAsync()` rewire engine to new server connection and rebuild KV cache via `PrefillStaticPrefix()`. `EAgentEngine.UpdateHttpClient()` recreates `RemoteKvCacheController` + `HttpStreamingEngine`.
- **Stale-session recovery (v12.12):** a restarted server drops KV sessions while client-side `_kvState` claims they're prefilled. Fixes: (1) `UpdateHttpClient()` clears BOTH `SessionActive` and `IsPrefilled`; (2) `EAgentEngine.InvalidateKvSessionState()` is called by `RecreateKvCacheSessionAsync()` so re-prefill always forces a full session recreate; (3) `SessionManager.RestoreSessionsAsync()` (extracted helper) runs on BOTH the reconnect path AND the recovery fast path; (4) `EAgentEngine.IsStaleSessionFailure()` treats HTTP 404 on generate as a stale session — recover + retry once instead of feeding the error into the parse pipeline.
- **Tag-free decisions (v14):** The `<lm>`/`<thinking>`/`<toolcall>`/`<output>` tag protocol has been completely removed. `EAgentEngine.GenerateAsync()` returns `Task<LLMDecision>` directly — the server produces `DecisionEnvelope` JSON (grammar-constrained for local, native OpenAI `tool_calls` for remote), which `StructuredDecisionAdapter.ParseDecision()` converts to `LLMDecision`. The orchestrator consumes `LLMDecision` objects directly. `ParseLLMDecision()`, `ParseToolCallBlock()`, `ExtractCleanResponse()` deleted. Any model works — local or remote — same as OpenClaw/Hermes.
- **Reasoning in history (v14.5):** The model's `thinking` field is stored as `[reasoning] ...` in transcript + context window, prefixed to the answer text. The model can learn from prior reasoning on later turns (matches old tag system behavior). Constrained to max 1 sentence to prevent token waste and hallucination. `LLMDecision.Reasoning` property carries it through the pipeline.
- **Early termination (v14.7):** The LLM server's streaming loop checks `TryParseCompleteEnvelope()` after each token — stops generation as soon as the JSON envelope is complete (valid `DecisionEnvelope` with `answer` or `toolcalls`). Saves ~70% generation time by not wasting tokens after the grammar root matches. `max_tokens` reduced from 512 to 256 as safety cap.
- **Catalog metadata self-heal (v12.12):** `ModelCatalogDocument.Load()` backfills empty `License` fields from the embedded default catalog (by model id). User-set values always win; on-disk file is never rewritten.

## Background Tasks (v11.2)

Background tasks (decompose, summarize) use `HttpStreamingEngine` in **stateless mode** (no `session_id`), replacing the old `StatelessExecutor`. No separate model load — they hit the same server.

```
ECAssistantLLM server (model + GPU, shared)
├── Main engine → /v1/chat/completions with session_id (persistent KV cache)
├── Sub-agent engines → own session_id (own KV cache, shared model)
└── Background tasks → /v1/chat/completions stateless (no session_id, fresh per call)
     ├── DecomposeTaskAsync() — use_llm toggle (keyword fallback)
     ├── IsConversationalAsync() — 1-token TASK/CHAT classification (v11.4 gate)
     └── WireSummaryService() — use_llm toggle (extractive fallback)
```

Config:
```json
"background_tasks": {
     "decompose": { "use_llm": true, "context_size": 4096, "max_tokens": 256, ... },
     "summarize": { "use_llm": true, "context_size": 4096, "max_tokens": 200, ... }
}
```

## PrefixCachedExtractor (v11.5)

Long-lived extraction with a persistent KV cache **via HTTP**. Wraps `IKvCacheController` + `IInferenceEngine` around a server-side session. The static system-prompt prefix is cached in KV memory; only the variable portion is sent per call. After each extraction, `RewindAsync` returns the cache to the clean prefix state.

```
ECAssistantLLM server (model + GPU, shared)
├── Main engine → session_id (persistent KV cache)
├── Sub-agent engines → own session_id
├── Background tasks → stateless (no session_id, fresh per call)
└── PrefixCachedExtractor → session_id + prefill/rewind via IKvCacheController
     ├── PrefillPrefixAsync → CreateSessionAsync + PrefillAsync + SaveStateAsync
     ├── ExtractAsync → RewindAsync + StreamAsync(variablePrompt) + strip tags
     └── UpdatePrefixAsync → ResetAsync + re-prefill
     Consumers: PatternExtractor (ECSQL); future: summarizer, intent classifier, topic detector
```

**When to use:** Long-lived consumers (registered tools, session-scoped services)
**When NOT to use:** Sub-agents, one-shot tasks — use `HttpStreamingEngine` stateless mode instead

## Composition Root

`EcaCompositionRoot.Build()` wires:
- Logger, config (AgentConfigBuilder), model path resolution, directory creation
- `ModelParamValidator` pre-flight check (before connecting to server)
- BackgroundProcessManager, FileWatcherService, SessionBuilder
- Returns `EcaServiceBundle` with all services

The actual HTTP wiring (`ServerLauncher` → `LlmServerClient` → `OpenAIClient` → `HttpStreamingEngine` / `RemoteKvCacheController` / `RemoteTokenizer`) happens inside `SessionManager` at `InitializeAsync`, using `LlmProviderConfig` (endpoint, mode, `auto_start`, `server_executable_path`, `heartbeat_interval_sec`, `host`, `port`).

## First-Run Setup (v12.9 — unified orchestrator, wizard-time server install)

ALL hosts (Console, TUI) share one Core flow — no per-host setup logic:

```
FirstRunOrchestrator (Setup/)
├── FirstRunDetector.Evaluate()   ← disk truth: gguf files, llm-server.json entries, server binary,
│                                    appsettings.json remote-provider config (IsRemoteProviderConfigured:
│                                    mode=remote + endpoint + llm_providers entry → NeedsSetup=false)
├── RunIfNeededAsync()            ← wizard only when state unresolved; never blocks startup
└── SetupWizard (ISetupUi)        ← adapters: ConsoleSetupUi (headless), TuiSetupUi (TUI)
```

- **Server install is wizard-time and conditional** — `ServerInstallCoordinator.EnsureServerAsync()` is called by the wizard ONLY when local chat (Stage 1) or local embeddings (Stage 2) is picked. Pure-remote users: zero `~/.ECAssistantLLM` footprint, no downloads, no folders.
- **`NuGetServerFetcher`** downloads `ecassistant.llm.server` from nuget.org flat container (temp extract) — the tool packages are thin (~2.8 MB); the ~170 MB server is NEVER embedded anywhere.
- **Interactive guarantees** (`ServerInstallCoordinator`): missing → install; ECAssistant version mismatch (VERSION stamp) → hint + ask to replace; foreign layout → hint + ask to place alongside. Writes only into `server/`, `models/`, `llm-server.json`. Binary-in-use guard before overwrite.
- **Reinstall** (`/reinstall`, TUI): confirm → stop server (verified) → `AiSetupResetter` (config reset, keys/ + llm-server.json deleted, models + binary KEPT) → same orchestrator/wizard; existing models show "✓ already on disk" and re-register instead of re-downloading.
- **Version upgrade path**: VERSION stamp vs `ServerInstallCoordinator.RequiredServerVersion` — mismatch prompts an upgrade at the next wizard run.
- **No flag files**: all state derived from disk → crash-safe, resumable.
- **Pure-remote installs are final** (2026-09-21 fix): a configured remote provider suppresses NeedsSetup even with an empty models dir; the server binary is only required when a local chat model or local embeddings is configured (`IsLocalEmbeddingsRequested`).

## Tool Permission Policy (v11.7)

Two permission levels per tool: `approvalRequired: true/false`.
To completely disable a tool, set `enabled: false` in the `tools` config section.

**Config-driven** — two sections in `appsettings.json`:

**`system_tools`** — system-critical tools, always registered, cannot be disabled. `approvalRequired: true` (default) or `false`. No `enabled` flag.
```json
"system_tools": [
  { "tool": "EShellAgent", "approvalRequired": true, "reason": "Shell execution — system-critical" }
]
```

**`tool_permissions`** — optional tools, can be disabled via `enabled: false` in `tools` section.
```json
"tool_permissions": [
  { "tool": "EGitTool", "approvalRequired": true, "reason": "Git operations" },
  { "tool": "EFileReaderTool", "approvalRequired": false, "reason": "Read-only" }
]
```

**Defaults:**
- Read-only tools (EFileReader, EDotnetBuild, ESubAgent, EFileResearch) → `Allowed`
- Dangerous tools (EShellAgent, EGitTool, ECodeEditorTool, EBackgroundExecTool) → `ApprovalRequired`

**Enforcement:**
- `Blocked` tools are **not registered** — LLM never sees them, zero tokens wasted on descriptions or failed calls
- **System-critical tools** (`IsSystemCritical = true`) always register regardless of `enabled` or `Blocked` config — they cannot be disabled
- Currently system-critical: `EShellAgent` only
- `ApprovalRequired` tools are registered but `ParallelToolExecutor` calls `RequestApproval()` before execution → user sees `⚠ APPROVAL REQUIRED` + tool name + args → y/n prompt
- `Allowed` tools execute immediately
- Config overrides hardcoded defaults via `ToolPolicy.LoadFromConfig()` at session creation
- Dynamic: any tool name works — custom/external tools just add an entry in config

**Flow:** `appsettings.json` → `system_tools` (loaded first, cannot be Blocked) + `tool_permissions` (loaded second, can override non-system tools) → `AgentSession` constructor → `ToolPolicy.LoadSystemTools()` + `ToolPolicy.LoadFromConfig()` → `SessionBuilder.EnsureAndRegister()` checks `IsBlocked` before registering → `ParallelToolExecutor.Check()` at execution time

## v10.31 — Port Control Flow

`LlmProviderConfig` now carries `Host` (default `localhost`) and `Port` (default `8420`), plus a `ResolvedEndpoint` computed property: **local mode** derives `http://{Host}:{Port}`; **remote mode** uses `Endpoint` as-is. All internal consumers (`SessionBuilder`, `SubAgentManager`, `TestRunner`, `ServerLauncher`) resolve the endpoint via `ResolvedEndpoint` instead of raw `Endpoint`.

- **Port control flow:** `appsettings.json` → `llm_provider.port: 8420` → `ServerLauncher.Start()` spawns ECAssistantLLM with `--port 8420` (plus optional `ServerConfigPath`) → LLM overrides its config port → listens on 8420 → Core connects to `http://localhost:8420`.
- **`AgentConfigBuilder.UseLocalLLM(int port, string host)`** signature changed; sets `Port`/`Host` on `LlmProviderConfig`. CLI `--port <N>` is wired through `EcaCompositionRoot`.
- **`LlmConfig` cleanup:** `GpuLayers` and `Threads` fields REMOVED (moved to LLM server's `llm-server.json`). Only `model_path` (display) + `context_size` (validation/sub-agent windows) remain.

## Config (new / changed)

- **`LlmProviderConfig`** (active) — `mode` (local/remote), `host` (default `localhost`), `port` (default `8420`), `endpoint`, `api_key`, `model_id`, `embedding_model_id`, `auto_start`, `server_executable_path`, `startup_timeout_sec`, `heartbeat_interval_sec`, `server_config_path`; `ResolvedEndpoint` computed property (`http://{Host}:{Port}` in local mode, `Endpoint` as-is in remote mode)
- **`LlmServerEndpointConfig`** — core-side endpoint config (endpoint, `auto_start`, `server_executable_path`)
- **`InferenceParamsFactory`** — produces `InferenceRequestParams` (JSON-serializable: model_id, session_id, max_tokens, temperature, top_p, top_k, repeat_penalty, stop, stream) — **not** LLamaSharp `InferenceParams`
- **`LlmConfig`** — only `model_path` (display) + `context_size` (validation/sub-agent windows); model load params live in the server's `llm-server.json`
- **`Config/ContextParams.cs` DELETED** (was LLamaSharp-specific)
- `TokenCounter` uses `RemoteTokenizer` (HTTP `/eca/tokenize`, char-based fallback)

## Multi-Provider LLMs & Secure Key Store (v12.2)

**Summary:** Remote mode can use multiple OpenAI-compatible providers; local mode unchanged. Host apps configure everything — zero code required.

### Config (`llm_providers` section)
- `providers[]` — `name`, `endpoint`, `api_key`, `model_id`, `embedding_model_id`, `is_default`
- `default_provider` — explicit name wins; else `is_default: true`; else first valid entry
- `fallback_enabled` — **opt-in**: strict single provider when false (default); when true, health-probes candidates at startup (5s timeout each), first healthy wins
- Back-compat: empty/missing section = legacy single `llm_provider` behavior. Local mode ignores this section entirely.
- Failover happens at session start only (mid-stream failover deliberately out)

### Components
- **`ILlmProviderRegistry` / `LlmProviderRegistry`** — parses config into `RemoteProvider` records; skips invalid entries with logged `ValidationErrors`; `GetByName(name)` lookup; `OrderedCandidates()` implements the fallback ordering
- **`ISecureKeyStore` / `SecureKeyStore`** — cross-platform self-encrypting key files (Data Protection keyring under `{keys_directory}/keyring`, purpose string `ECAssistant.ApiKeys.v1`); plaintext files auto-migrate to encrypted in place on first read (`ECAKEY1:` header format); atomic writes; owner-only POSIX perms on non-Windows; name-only references (path escape rejected)

### API key schemes (per provider entry)
| Scheme | Behavior |
|---|---|
| literal | passes through (discouraged in config) |
| `file:<path>` | raw file read, `~/` expanded, trimmed — no encryption |
| `keyfile:<name>` | managed via SecureKeyStore — self-encrypting, path-escape guarded |

No admin/elevated rights required anywhere: user-scope crypto, non-privileged ports (>1024), all state inside the app root.

## Hardening (2026-08-27)

- **ConfigLoader** — partial user `appsettings.json` now deep-merges over embedded defaults (previously absent sections reset to empty); malformed JSON falls back to defaults
- **ToolPolicy** — shell redirection (`>`, `>>`, `<`, quote-aware) is classified as write → requires approval
- **AgentOrchestrator** — honors `tool.IsEnabled`; disabled tools return `[BLOCKED]` result instead of executing
- **SessionRegistry / VramBudget** — reserve+release centralized in registry; all destroy paths symmetric
- **LlmServerClient** — reconnect hysteresis (30s cooldown), disconnect-before-dispose ordering
- **EWebFetchTool tests** — Moq setup-ordering corrected (defaults in ctor; specifics win)

## Test Coverage

| Area | Test Files | Tests |
|------|-----------|-------|
| Engine | 18 | ~250 |
| Services | 11+ | ~160 |
| Tools | 15 | ~237 |
| Session | 2 | ~50 |
| Memory | 2 | ~40 |
| Config | 3 | ~45 |
| Integration | 10 | ~100 |
| Analysis | 1 | ~20 |
| UI | 1+4 | ~17 |
| **Total** | **62+4** | **914** |

`MockEngine` (now in the separate `ECAssistant.TestSupport` project) extends `EAgentEngine` with a no-op HTTP transport so tests run without a live ECAssistantLLM server. TestRunner/TestScenario/EcaTests also live in TestSupport.

## EWebFetch v2 — Structured Content Pipeline (v12.1)

EWebFetch was reworked to produce LLM-parseable output. The old tool returned a single unstructured text blob with boilerplate, which 8B models couldn't extract information from.

**Pipeline:** `IHttpClient.GetAsync` → `IReadableContentExtractor.Extract` → `IHtmlTextConverter.Convert` → offset-based paging

**IReadableContentExtractor** (`ReadableContentExtractor`):
- Prefers `<article>` → `<main>` → `role="main"` containers
- Falls back to `<body>` with nav/aside/footer/form/iframe/script stripped
- Removes elements by boilerplate class/id patterns (nav, sidebar, cookie, banner, social, share, etc.)
- Cleans share/social/nav sub-elements inside articles

**IHtmlTextConverter** (`HtmlTextConverter`):
- Converts block-level closing tags (`</p>`, `</div>`, `</h1>`…`</h6>`, `</li>`, `</tr>`, etc.) to newlines BEFORE stripping tags
- Converts block-level opening tags to newlines too (heading starts, list items)
- Removes `<script>`, `<style>`, `<noscript>`, `<head>`, HTML comments
- Decodes HTML entities
- Collapses 3+ consecutive blank lines to max 2
- Result: structured plain text with paragraph/heading/list separation

**HttpClientAdapter:**
- Sends browser-like `User-Agent` + `Accept: text/html` + `Accept-Language` headers on all GETs
- New `GetAsync(url, headers, ct)` overload on `IHttpClient` for custom header support
- Prevents Cloudflare/bot-protection 403s and block pages

**EWebFetchTool changes:**
- Constructor injects `IHttpClient` + `IReadableContentExtractor` + `IHtmlTextConverter` + `EAgentConfig`
- New `offset` arg (chars) — LLM can page through long content; output includes next-offset hint
- Default `maxchars` raised from 6000 → 12000
- `GetToolRules()` injects offset guidance into system prompt
- Output format: `Fetched {url} (offset N, returning M of T total chars)\n\n{content}\n\n[truncated — call EWebFetch with offset X / End of page]`

**Wiring:** `SessionBuilder.RegisterNativeTools` + `TestRunner` create `ReadableContentExtractor` + `HtmlTextConverter` instances and pass to `EWebFetchTool` constructor.

## Key Constraints

- No static classes, no static mutable state
- Utility classes use instance methods with `Default` shared instance (StringUtil, InferenceParamsFactory, ResourceLoader, AgentConfigBuilder)
- Factory methods on immutable data classes are the only allowed static methods (EToolResult.Success, TranscriptMessage.User, ToolPermissionRecord construction, AgentConfigBuilder.Create, etc.)
- EToolBase config helpers (ReadConfig, ReadCfg, IsToolEnabled) are protected instance methods
- Constructor injection throughout
- One type per file
- All config models init-only (2 documented exceptions)
- All ReadCfg logic consolidated in EToolBase
- EAgentEngine implements IEngine (unsealed)
- EAgentEngine: optional constructor injection for EMemoryManager, SelfCorrectionManager, ProjectContextManager, TaskPlanner
- SubAgentManager: injects IProcessRunner, IFileSystem, IHttpClient, BackgroundProcessManager via constructor
- EGuiConsole uses ITerminalOutput (no direct Console.Write)
- EcaCompositionRoot is the single wiring point
- **No LLamaSharp in Core** — all inference is HTTP; model + GPU + KV cache are server-side
- Inference crosses a process boundary via `OpenAIClient` (HTTP/SSE) to ECAssistantLLM or any OpenAI-compatible endpoint
- KV cache control is server-side via `IKvCacheController` (`RemoteKvCacheController`): `CreateSession`/`Prefill`/`Rewind`/`SaveState`/`Reset`/`GetStatus`
- Background tasks use `HttpStreamingEngine` stateless mode (no `session_id`)
- PrefixCachedExtractor uses HTTP KV-cache control (`prefill`/`rewind` via `IKvCacheController`) for long-lived extraction
- Token counting via `RemoteTokenizer` (HTTP `/eca/tokenize`) with char-based fallback
- **`ServerLauncher`** spawns ECAssistantLLM with `--port {Port}` (+ optional `ServerConfigPath`); resolves the connect target via `LlmProviderConfig.ResolvedEndpoint`
- MockEngine (in TestSupport) uses a no-op HTTP transport constructor (model-independent tests, no static flags)
- v11.4: Orchestrator gates decomposition — verb heuristic first (instant), then LLM 1-token classification (~0.15s)
- v14: System prompt is tag-free; teaches tool selection via plain descriptions and examples
- v12.1: EWebFetch uses IReadableContentExtractor + IHtmlTextConverter pipeline (fetch→extract→convert→page); HttpClientAdapter sends browser User-Agent + Accept headers; offset arg for paging long pages; default maxchars 12K; tool rules injected into system prompt with offset guidance

## Audit Fixes (2026-08-27)

- `ContextWindow`: all `_messages` access locked via `_messagesLock`; summarization guarded against overlap (`_summarizeInProgress`); LLM summary inserts asynchronously after leading system message(s) — no more async-void race on the live list
- `BackgroundProcessManager`: unix/macOS branch now executes the temp script file directly (was inline `-c "{command}"`, broke on embedded quotes)
- `SessionManager`: `StopSession`/`CreateSession` dictionary access moved under `_sessionsLock`; session counter is `Interlocked`
- `ECodeEditorTool`: empty `old_text`/`pattern` rejected before `CountOccurrences` (was an infinite loop); `file_filter` wildcard matching actually applied in search/replace-all

## Audit Fixes (2026-09-21, PM)

- `ECodeEditorTool`: GetParameterSchema action enum aligned with the dispatcher (create/diff/patch/search/replace-all/insert/delete-lines/delete — schema previously advertised write/edit/delete which don't exist). `DoCreate` reads `new_text` as fallback when `content` is absent (models send either) and returns a clear failure when both are missing — previously wrote a 0-byte file and returned SUCCESS, triggering empty-patch retry cascades (observed with glm-5.3-flash in production benchmark)

## Vision (2026-08-27)

- `[image:<path>]` tokens in user input are extracted by `ImageAttachmentParser` (png/jpg/jpeg/webp/gif/bmp) into base64 data URIs; stripped from the prompt, missing files get a note appended
- `InferenceRequestParams.ImageDataUris` carries images per call (cleared after streaming — params object is shared across turns); `HttpStreamingEngine` serializes OpenAI content-parts arrays when images are present
- Remote mode: works with any vision-capable OpenAI-compatible API. Local mode: server needs `mmproj_path` on the model config (see ECAssistantLLM ARCHITECTURE "Vision")
- Context window: each attached image adds a flat 800-token budget estimate (`ContextWindow.TokensPerImage`)

## First-Run Model Installer (2026-08-27)

- New `Setup/` package: `ModelCatalogDocument` + `ModelCatalogEntry` (data-driven catalog), `FirstRunDetector`, `ModelInstallerService`
- `model-catalog.json` — lives in app root dir; **auto-written with built-in defaults on first load** so it stays user-editable; adding a model = one JSON entry
- Catalog entry: id, name, category (Chat/Vision/Embedding), hf_repo, files (incl. mmproj sidecar for vision), suggested config, recommended flag
- `FirstRunDetector.Evaluate` → NeedsSetup when models/ has no GGUFs and llm-server.json references no existing files
- `ModelInstallerService.InstallAsync` — HF `resolve/main` download, resume via `.part` + Range header, stall timeout, per-file skip when present; then `ApplyToServerConfig` merges the model entry (mmproj_path for vision, is_embedding/pooling for embeddings) without touching other config sections
- No UI dependency — TUI/Console own interaction

## Changelog — 2026-08-27 (Installer + Embeddings)

- **Setup/**: `RemoteProviderSetupWriter` (remote config + keyfile ref), `VectorMemorySetupWriter` (vector_memory.enabled), installer hardening (internet probe, disk check, retry, orphan GGUF registration `RegisterLocalModelFile`, sibling mmproj auto-detect, `CatalogSuggestedConfig.BatchSize`)
- **Services/**: `SecureKeyStore.SetKey` — encrypt-on-save (new on `ISecureKeyStore`); key never stored plaintext in appsettings
- **Engine/**: `EAgentEngine.InitializeVectorMemoryAsync` now wires the embedder into the store (was dropped) + TfidfEmbedder fallback when none

## Changelog — 2026-08-27 (evening: embeddings independence + vision flag)

- **Config/**: `EmbeddingConfig.Mode` ("local"|"remote") + `Endpoint`/`ModelId` overrides — embeddings are independent of the main LLM mode. `LlmProviderConfig.VisionEnabled` + `RemoteProviderConfig.VisionEnabled` + `EAgentConfig.SupportsVision` (mode-agnostic integration answer)
- **Session/**: `SessionManager` spawns a local LLM server for embeddings when main AI is remote but `embedding.mode=local` (`_embeddingServerLauncher`, ctor sync-over-async pattern, disposed on shutdown). `SessionBuilder.ResolveEmbeddingEndpoint/ModelId` route the embedder by embedding mode (local → localhost + "embeddings"; remote → provider endpoint/model)
- **Setup/**: `EmbeddingSetupWriter` (embedding.mode persistence); `ModelInstallerService` keeps `llm.model_path` + `llm_provider.vision_enabled` in sync (creates minimal appsettings when missing — critical during first-run); `SecureKeyStore.SetKey`; `DetectSiblingMmproj` public (vision pairing); `LooksLikeEmbeddingModel` public
- **Interfaces/**: `ISecureKeyStore.SetKey(fileName, plaintext)` — encrypt-on-save

## Core/LLM Boundary Law + Manifest-Driven Installer (2026-09-18)

**Boundary (hard rule):** ECAssistantCore consumes ECAssistantLLM ONLY via its HTTP
endpoints. Core knows NOTHING about how the LLM server runs models. ECAssistantLLM is a
self-contained finished product: it ships everything needed to run its models and NEVER
downloads at runtime.

- `ServerAssetInstaller` (replaces deleted `BackendProvisioner`): dumb installer that
  reads `install-manifest.json` shipped with the LLM server package (content/server/),
  downloads assets, verifies SHA-256, extracts, normalizes `archive_root` → `runtime_id`
  layout. Zero LLM-internal knowledge lives in Core.
- `ModelInstallerService`: no `download_url`/`download_sha256` in generated configs —
  the wizard installs everything up front; the server only locates pre-installed assets.
- Vision rule: vision-capable catalog entries ALWAYS carry their mmproj file.

## Addendum — wizard rework (12.9.8)

**Remote catalog:** `Setup/CatalogFetcher.cs` fetches `catalog/model-catalog.json`
from GitHub main at wizard start (5s timeout, schema-validated via `Validate()`).
Success → replaces the user copy in `~/.ECAssistant/model-catalog.json`. Any
failure (offline/timeout/invalid) → embedded default catalog (same file, embedded
resource — single source, two delivery paths). Catalog edits ship to users with
zero app release.

**Flat model list:** the Vision/Chat category split is gone — one "Available
Models" list of all non-embedding catalog entries PLUS GGUFs discovered in the
models folder (`ModelInstallerService.BuildLocalModelEntry`, sibling-mmproj
detection). On-disk entries render green ("✓ already on disk"). Reinstalling
skips downloads and re-applies the config hardware-tuned
(`ApplyToServerConfigTuned` — also used by the download path).

**Vision:** no prompt. Derived from `mmproj_file` presence (catalog entry or
sibling detection). Remote-provider probe failure now assumes "no" instead of
asking.

## Addendum — audit fixes (12.9.8, pre-release hardening)

- **BackgroundProcessManager (real bug):** progressive-capture pump tasks wrote into
  detached local buffers — `GetOutput` always returned empty. Closures now use the
  BgProcess-owned buffers. Test staleness fixed for EFileResearchTool (query-aware
  selection), ConfigProvider (missing-section = fresh default contract), ContextWindow
  (auto-summarize requires a SummaryService). Core suite fully green: 1034/1034.

- Checksum mismatch no longer aborts the whole wizard (per-model failure + partial-file cleanup).
- Download resume verifies HTTP 206; servers ignoring Range reset to a clean rewrite (no corrupt appends).
- Remote catalog only replaces the user copy when strictly newer (version-aware).
- Stale-catalog merge is a UNION now: new default entries added by id, user entries preserved.
- Discovered local models keep their conservative CPU defaults (not hardware-tuned); orphan `mmproj*.gguf` files excluded from discovery.
- RAM probe returning 0 → catalog suggestions kept (no silent tiny-machine downgrade).
- Dead code removed: WizardContext.GpuLayers, IsInstalled, IsInternetAvailableAsync, GetFreeSpaceGb, ListModelFiles, RegisterLocalModelFile (prod path), LooksLikeEmbeddingModel (prod), dead ternaries/usings; Files aliasing copy in tuned apply.

## Addendum — pure-remote first-run + config-driven harness (2026-09-21 PM)

- **EndpointNormalizer** (Transport): one convention — base URLs without trailing `/v1`; all versioned paths appended by the client. `RemoteModelProbe` (Setup) tries `{base}/models` then `{base}/v1/models`.
- **ProjectContextManager**: host runtime files excluded from the project scan (appsettings.json, model-catalog.json, .project_context.json, ECAssistant.log; dirs .sessions, Workspace, tool_outputs) + prompt nudge — the model no longer narrates the app's own config.
- **Every harness parameter is config-driven now**: engine context window ← `llm.context_size` (hardcoded 8192 wiring bug fixed), orchestrator turn limit ← `interface.max_turns` (was hardcoded 5), subtask turn budget ← `interface.turns_per_subtask` + `interface.subtask_turn_buffer`, compaction trigger ← `context_management.compact_threshold_percent`. New keys default to the previous hardcoded values.
- Core suite: 1053/1053 green (FirstRunDetectorTests ×9, EndpointNormalizerTests + RemoteModelProbePathTests ×6 added).

## Addendum — terminal restore + probe reliability (2026-09-21 evening)

- `ServerConnection` probe timeout raised 5s → 12s (client 15s): remote model-list
  probes (OpenRouter) measured 10-20s during service slowness and falsely reported
  "LLM endpoint unreachable", which also triggered the ugly init-failure exit path.
- Companion: TUI restores the terminal on all exit paths; Console host wraps
  RunAsync in try/finally (see TUI/Console ARCHITECTURE.md changelogs).

## Addendum — harness optimization P1-P6 (2026-09-21 night, local-first low-compute)

Research-grounded (2026 harness-engineering sources: Terminal-Bench harness-only
delta, Aider/Cline/Claude-Code teardowns). All model-independent, all config-driven.

- **P1 — Tool-result truncation limits config-driven** (`tool_output_limits`):
  `max_result_chars` (default 4000), per-tool overrides
  (`max_result_chars_per_tool`), `max_stored_outputs`. Truncation before
  injection already existed (EAgentEngine.TruncateToolOutput) — the hardcoded
  4000/6000/8000 constants are now config values with the same defaults.
- **P2 — In-loop planning by default** (`interface.preplanning`, default false):
  skips the 2-3 LLM pre-pass calls (Decompose + StepMapper). Pre-planning stays
  available via config for small models that want explicit step lists.
- **P3 — Verifier contract** (`interface.verify_command`): injected into the
  system prompt (VERIFIER rule) — act → observe → verify loop.
- **P4 — Typed tool schemas**: `EToolBase.GetParameterSchema()` (virtual, JSON
  Schema string) implemented on Shell/CodeEditor/FileReader/WebSearch/WebFetch/
  EDotnetBuild/FileResearch; `ToolSpec.ParameterSchema` carries it into the
  native function-calling request (tools without a schema fall back permissive).
- **P5 — Staged compaction**: `ContextWindow.TrimStaleToolOutputs()` drops stale
  tool outputs (keeps the last) — zero-LLM-cost stage 1 before the full
  summarize-rebuild.
- **P6 — Rules file**: working-dir `AGENTS.md` injected into the system prompt
  (## PROJECT RULES, truncated at 6000 chars).
- Tests: HarnessOptimizationTests ×10. Suite 1071/1071.
