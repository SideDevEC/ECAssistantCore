# ECAssistantCore — Architecture

**Updated:** 2026-08-24 (v10.30 — HTTP-based inference via ECAssistantLLM server)
**Status:** ✅ Build clean, 0 errors, 6 pre-existing warnings

## Overview

ECAssistantCore is a local-first AI agent framework. Supports two LLM provider modes:
- **local**: Spawns ECAssistantLLM server (full KV cache, session management, tokenizer)
- **remote**: Connects to any OpenAI-compatible API (cloud, no KV cache, stateless inference)

No in-process LLamaSharp — Core is a pure .NET library with zero native dependencies.

Supports multi-session orchestration, sub-agents, vector memory, self-correction, 12 built-in tools, and a TUI layer.

## OOP Principles

- **Encapsulation:** Config models are init-only (immutable); 2 documented exceptions for builder-mutated properties
- **No globals or statics:** Dependencies injected via constructors. Utility classes (StringUtil, InferenceParamsFactory, ResourceLoader, AgentConfigBuilder) are instance classes with `Default` shared instance. Factory methods on immutable data classes are the only allowed static methods
- **No cross-dependencies:** Layers depend only on the layer below, through interfaces
- **Single responsibility:** One type per file, one interface = one concern
- **Modular & interchangeable:** Every service behind an interface, mockable via Moq
- **Testable by design:** MockEngine implements IInferenceEngine (returns canned responses, no server needed)
- **Composition root:** Central `EcaCompositionRoot` wires all services
- **Terminal abstraction:** `ITerminalOutput` decouples TUI from `System.Console`

## Project Structure

```
ECAssistantCore/               # 166 .cs files, ~15,371 LOC + 59 test files
├── Analysis/                  # Project context analysis
├── Composition/               # EcaCompositionRoot + EcaServiceBundle
├── Config/                    # AgentConfigBuilder, ConfigLoader, 13 config models
├── Engine/                    # EAgentEngine (IEngine), Orchestrator, SubAgentManager
│   ├── SelfCorrection/        # FailureAnalysis, FailurePattern, FileSnapshot
│   ├── SubAgent/              # SubAgentTask, SubAgentResult, SubAgentError
│   └── PrefixCachedExtractor  # KV cache reuse for long-lived extraction (HTTP-based)
├── Interfaces/                # 18 interfaces (IEngine, IInferenceEngine, IKvCacheController, etc.)
├── Memory/                    # EMemoryManager, VectorMemoryStore
├── Services/                  # Logger, ContextManager, InferenceParamsFactory, etc.
│   └── Http/                  # HTTP-based services: HttpStreamingEngine, RemoteKvCacheController,
│       │                        RemoteModelLoader, RemoteTokenizer, LlmServerClient, ServerLauncher
├── Session/                   # AgentSession, SessionManager, SessionBuilder, SessionDiscovery
├── Testing/                   # TestRunner, MockEngine, TestScenario, EcaTests
├── Tools/                     # EToolBase + 12 built-in tools
│   ├── EBackground/           # Background process execution
│   ├── Build/                 # BuildErrorParser
│   ├── ECode/                 # Code editor (create, patch, diff, search)
│   ├── EDotnet/               # dotnet build/restore/test
│   ├── EGit/                  # Git operations
│   ├── EResearch/             # File research
│   ├── EShell/                # Shell command execution
│   ├── EWeb/                  # Web search + fetch
│   ├── Policy/                # ToolPermission, ToolPolicyDecision
│   ├── Reader/                # File reader
│   └── SubAgent/              # Sub-agent tool
├── Transport/                 # OpenAIClient (HttpClient wrapper), SseParser (SSE token stream)
└── UI/                        # EGuiBase, IGuiConsole, ITerminalOutput

ECAssistantTUI/                # Terminal UI layer (12 files + 9 test files)
├── Controller/                # AppController
└── UI/                        # BaseLayer, SessionLayer, ConfigLayer, HelpLayer

ECAssistantConsole/            # Console entry point (1 file)
└── Program.cs                 # Uses EcaCompositionRoot, runs AppController
```

## Dependency Flow

```
ECAssistantCore (no LLamaSharp, only Microsoft.Extensions.Logging.Abstractions)
        ▼
ECAssistantTUI  ←── [Core DLL]
        ▼
ECAssistantConsole ←── [Core DLL, TUI DLL]
        ▼
    HTTP/SSE → ECAssistantLLM (separate process, owns LLamaSharp + model)
```

## Key Interfaces (18)

| Interface | Implementation | Purpose |
|---|---|---|
| IEngine | EAgentEngine | Engine lifecycle (Start, Run, Dispose) |
| IInferenceEngine | HttpStreamingEngine | LLM streaming + generation via HTTP |
| IKvCacheController | RemoteKvCacheController | Server-side KV cache control (prefill, rewind, reset) |
| ILlmServerClient | LlmServerClient | Client lifecycle (register, heartbeat, disconnect) |
| IModelLoader | RemoteModelLoader | Remote model load/unload via HTTP |
| ILogger | Logger | Structured file logging |
| IMemoryService | MemoryService | Persistent memory with search |
| IContextManager | ContextManager | Context window management |
| IConfigProvider | ConfigProvider | Config value access |
| IFileSystem | FileSystemAdapter | File operations abstraction |
| IHttpClient | HttpClientAdapter | HTTP requests |
| ITerminal | TerminalAdapter | Terminal operations |
| IProcessRunner | ProcessRunner | Process execution |
| IVectorEmbedder | TfidfEmbedder | Vector embeddings (fallback) |
| IVectorStore | InMemoryVectorStore | Vector storage |
| IOutputRenderer | ConsoleUiRenderer | Output rendering |
| IToolPolicyEvaluator | ToolPolicy | Tool permission evaluation |
| ISessionContext | AgentSession | Session surface for external consumers |

## HTTP Transport Layer

```
Core Engine
    ├── IInferenceEngine → HttpStreamingEngine → OpenAIClient → HTTP POST /v1/chat/completions
    ├── IKvCacheController → RemoteKvCacheController → OpenAIClient → HTTP /eca/sessions/*
    ├── IModelLoader → RemoteModelLoader → OpenAIClient → HTTP /eca/models/*
    ├── RemoteTokenizer → OpenAIClient → HTTP POST /eca/tokenize
    └── ILlmServerClient → LlmServerClient → OpenAIClient → HTTP /eca/clients/*

OpenAIClient: HttpClient wrapper (JSON + SSE streaming, X-Client-Id header)
SseParser: Parses SSE "data:" lines, extracts token content from OpenAI chunks
ServerLauncher: Detects if ECAssistantLLM is running, auto-starts if needed
```

## Session Architecture

Each `AgentSession` has:
- Own EAgentEngine (own server-side KV cache via HTTP, own session ID)
- Own orchestrator, tools, memory, output buffer, prompt queue, runner thread
- Sessions share the same ECAssistantLLM server (one model in VRAM)
- No shared state, no inter-session communication
- `SessionBuilder` — public API for external consumers (e.g., ECSQL)
- `ISessionContext` exposes session surface for external consumers

## Background Tasks

Uses `HttpStreamingEngine` in stateless mode (no session_id) for one-shot LLM calls:

```
ECAssistantLLM Server (one model in VRAM)
├── Main session → own KV cache (persistent, prefilled)
├── Sub-agent sessions → own KV cache each
├── Background tasks → stateless HTTP calls (no session, fresh each time)
│   ├── DecomposeTaskAsync() — use_llm toggle (keyword fallback)
│   ├── IsConversationalAsync() — 1-token TASK/CHAT classification
│   └── WireSummaryService() — use_llm toggle (extractive fallback)
└── PrefixCachedExtractor → HTTP KV cache control (prefill/rewind via IKvCacheController)
    ├── PatternExtractor (ECSQL) — SQL pattern extraction
    └── Future: summarizer, intent classifier
```

## Provider Modes (v10.30)

Config in `llm_provider` section of appsettings.json:

### Local Mode (`mode: "local"`)
- Spawns ECAssistantLLM as child process (auto-start)
- Full KV cache support (prefill, rewind, save-state, reset)
- Server-side sessions with VRAM budget
- Client registration + heartbeat
- Tokenizer via `/eca/tokenize`
- Embeddings via server's embedding model

### Remote Mode (`mode: "remote"`)
- Connects to any OpenAI-compatible API (OpenAI, DeepSeek, etc.)
- No KV cache — uses `NopKvCacheController` (all ops return true, no-op)
- No server launch, no client registration, no heartbeat
- No tokenizer (falls back to char-based estimation)
- Embeddings via provider's embedding API (if available)
- Requires `api_key` in config

### Config
```json
// Local
"llm_provider": {
    "mode": "local",
    "endpoint": "http://localhost:8420",
    "model_id": "main",
    "auto_start": true
}

// Remote
"llm_provider": {
    "mode": "remote",
    "endpoint": "https://api.openai.com",
    "api_key": "sk-...",
    "model_id": "gpt-4o"
}
```

CLI: `--local` (local mode), `--remote <url> <key> <model>` (remote mode)
Builder: `.UseLocalLLM()`, `.UseRemoteLLM(url, key, model)`

## Output Modes (v10.30)

Config in `interface` section of appsettings.json:
- **`verbose`** (default true): Show token stream headers, per-token output, token counts, raw response dumps
- **`silent`** (default false): Suppress token stream noise — only show final parsed results and errors
- **`max_turns`** (default 10): Max agent iterations per user request

CLI: `--verbose`, `--silent`, `--maxturns <n>`
Builder: `.Verbose()`, `.Silent()`, `.MaxTurns(n)`

## Config

Core-side config (`appsettings.json`):
- `llm`: model_path (display/validation), context_size
- `llm_server`: endpoint, auto_start, server_executable_path, heartbeat_interval
- `inference`: max_tokens, temperature, anti_prompts
- `sampling`: temperature, top_p, top_k, repeat_penalty
- `background_tasks`: decompose + summarize (use_llm, context_size, params)
- `subagent`: enabled, context_size, max_concurrent, max_turns, timeout
- `interface`: verbose, silent, max_turns, prompt_prefix, response_prefix
- `context_management`: strategy, shift_at_messages, keep_last
- `vector_memory` + `embedding`: vector search config
- `tools`: dynamic dict (any tool can add its config section)

Server-side config (`llm-server.json`): models (gpu_layers, threads, batch_size), server port, inference defaults. GPU/threads/batch are server concerns — NOT in Core config.

## Composition Root

`EcaCompositionRoot.Build()` wires:
- Logger, config (AgentConfigBuilder), model path resolution, directory creation
- Pre-flight model validation (path exists, context size sane)
- BackgroundProcessManager, FileWatcherService, SessionBuilder
- Returns `EcaServiceBundle` with all services
- CLI args: `--model`, `--ctx`, `--verbose`, `--silent`, `--maxturns`, `--temp`

## Key Constraints

- No static classes, no static mutable state
- One type per file, one interface = one concern
- Constructor injection throughout
- All config models init-only (2 documented exceptions)
- EAgentEngine implements IEngine (unsealed, for MockEngine)
- MockEngine uses protected mock-mode constructor (no static flags)
- EGuiConsole uses ITerminalOutput (no direct Console.Write)
- EcaCompositionRoot is the single wiring point
- No LLamaSharp dependency — all inference via HTTP
- GPU layers, threads, batch_size are server-side concerns (ECAssistantLLM config)