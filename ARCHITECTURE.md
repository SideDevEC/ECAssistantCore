# ECAssistant — Architecture

**Updated:** 2026-08-26 (v12.1 — EWebFetch v2: structured HTML conversion + content extraction + offset paging)
**Status:** ✅ 850 Core tests + 64 LLM integration tests, 0 errors

## Overview

ECAssistant is a local-first AI agent framework. LLM inference is **HTTP-based**: Core talks to a separate `ECAssistantLLM` server process (or any OpenAI-compatible endpoint) over HTTP/SSE. There is **no in-process LLamaSharp** — zero LLamaSharp dependency in `ECAssistant.Core`.

Core handles multi-session orchestration, sub-agents, vector memory, self-correction, 12 built-in tools, and a TUI layer. The model, GPU layers, and KV cache live server-side; Core is a thin HTTP client plus agent logic.

Two provider modes (`LlmProviderConfig.mode`):
- **local** — spawn/connect to `ECAssistantLLM` server; full KV cache, sessions, tokenizer
- **remote** — connect to any OpenAI-compatible API (e.g. `gpt-4o`, `deepseek-chat`); stateless, no KV cache

## OOP Principles

- **Encapsulation:** Config models are init-only (immutable); 2 documented exceptions for builder-mutated properties
- **No globals or statics:** Dependencies injected via constructors. No static classes, no static mutable state. Utility classes (StringUtil, InferenceParamsFactory, ResourceLoader, AgentConfigBuilder) are instance classes with `Default` shared instance. The only allowed static methods are factory methods on immutable data classes (EToolResult.Success, TranscriptMessage.User, ToolPolicy.Allowed, AgentConfigBuilder.Create, etc.) and pure protected instance helpers on EToolBase (ReadConfig, ReadCfg, IsToolEnabled)
- **No cross-dependencies:** Layers depend only on the layer below
- **Single responsibility:** One type per file, one interface = one concern
- **Modular & interchangeable:** Every service behind an interface, mockable via Moq
- **Testable by design:** 857 unit + integration tests, MockEngine for model-independent testing
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
├── Testing/                # TestRunner, MockEngine (in EAgentEngine.cs), TestScenario, EcaTests
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
     └── Web/               # EWebFetch — fetch URL, extract readable content, convert to structured text (offset paging)
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
└── ServerLauncher.cs          # Detect/launch ECAssistantLLM server, send /eca/shutdown on stop

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
- **Heartbeat auto-reconnect:** `LlmServerClient` tracks consecutive failures; after 3 failures → `TryReconnectAsync()` re-registers. `OnReconnected` event fires for session restoration.
- **Shutdown wiring:** `AppController` calls `SessionManager.DisposeAsync()` on quit → `DisconnectAsync()` + `ServerLauncher.StopServerAsync()` → POST `/eca/shutdown` → server winds down if last client.
- **Session restore:** `AgentSession.UpdateClientId()` + `RecreateKvCacheSessionAsync()` rewire engine to new server connection and rebuild KV cache via `PrefillStaticPrefix()`. `EAgentEngine.UpdateHttpClient()` recreates `RemoteKvCacheController` + `HttpStreamingEngine`.

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
- Read-only tools (EFileReader, EWebSearch, EWebFetch, EDotnetBuild, ESubAgent, EFileResearch) → `Allowed`
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

`MockEngine` (in `EAgentEngine.cs`) now extends `EAgentEngine` with a no-op HTTP transport so tests run without a live ECAssistantLLM server.

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
- Factory methods on immutable data classes are the only allowed static methods (EToolResult.Success, TranscriptMessage.User, ToolPolicy.Allowed, AgentConfigBuilder.Create, etc.)
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
- MockEngine uses a no-op HTTP transport constructor (model-independent tests, no static flags)
- v11.4: Orchestrator gates decomposition — verb heuristic first (instant), then LLM 1-token classification (~0.15s)
- v11.4: System prompt teaches LLM to learn from failed <thinking> blocks in conversation history
- v12.1: EWebFetch uses IReadableContentExtractor + IHtmlTextConverter pipeline (fetch→extract→convert→page); HttpClientAdapter sends browser User-Agent + Accept headers; offset arg for paging long pages; default maxchars 12K; tool rules injected into system prompt with offset guidance

## Audit Fixes (2026-08-27)

- `ContextWindow`: all `_messages` access locked via `_messagesLock`; summarization guarded against overlap (`_summarizeInProgress`); LLM summary inserts asynchronously after leading system message(s) — no more async-void race on the live list
- `BackgroundProcessManager`: unix/macOS branch now executes the temp script file directly (was inline `-c "{command}"`, broke on embedded quotes)
- `SessionManager`: `StopSession`/`CreateSession` dictionary access moved under `_sessionsLock`; session counter is `Interlocked`
- `ECodeEditorTool`: empty `old_text`/`pattern` rejected before `CountOccurrences` (was an infinite loop); `file_filter` wildcard matching actually applied in search/replace-all

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
