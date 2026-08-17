# ECAssistant — Architecture

**Updated:** 2026-08-17 (v11.2)
**Status:** ✅ 857 tests pass, 0 errors, 13 warnings (pre-existing xUnit analyzers)

## Overview

ECAssistant is a local-first AI agent framework running LLM inference on-device via LLamaSharp. It supports multi-session orchestration, sub-agents, vector memory, self-correction, 12 built-in tools, and a TUI layer — all in-process, no cloud.

## OOP Principles

- **Encapsulation:** Config models are init-only (immutable); 2 documented exceptions for builder-mutated properties
- **No globals or statics:** Dependencies injected via constructors
- **No cross-dependencies:** Layers depend only on the layer below
- **Single responsibility:** One type per file, one interface = one concern
- **Modular & interchangeable:** Every service behind an interface, mockable via Moq
- **Testable by design:** 857 unit + integration tests, MockEngine for model-independent testing
- **Composition root:** Central `EcaCompositionRoot` wires all services
- **Terminal abstraction:** `ITerminalOutput` decouples TUI from `System.Console`

## Project Structure

```
ECAssistantCore/           # Core engine, tools, sessions, memory (210 files + 65 test files)
├── Analysis/              # Project context analysis (EContextAnalyzer)
├── Composition/           # EcaCompositionRoot + EcaServiceBundle
├── Config/                # AgentConfigBuilder, ConfigLoader, 13 config models (init-only)
├── Engine/                # EAgentEngine (IEngine), Orchestrator, SubAgentManager, TaskPlanner
│   ├── SelfCorrection/    # FailureAnalysis, FailurePattern, FileSnapshot
│   └── SubAgent/          # SubAgentTask, SubAgentResult, SubAgentError
├── Interfaces/            # 15 interfaces (IEngine, IInferenceEngine, ILogger, IMemoryService, etc.)
├── Memory/                # EMemoryManager, VectorMemoryStore
├── Services/              # 12 service implementations (Logger, ContextManager, ModelLoader, etc.)
├── Session/               # AgentSession, SessionManager, SessionBuilder, SessionDiscovery
│   └── ISessionContext     # Exposes SharedWeights + BackgroundTasks (not SecondaryModel)
├── Testing/               # TestRunner, MockEngine, TestScenario, EcaTests
└── Tools/                 # EToolBase + 12 built-in tools
    ├── EBackground/       # Background process execution
    ├── ECode/             # Code editor (create, patch, diff, search, insert, delete)
    ├── EDotnet/           # dotnet build/restore/test
    ├── EGit/              # Git operations
    ├── EResearch/         # File research
    ├── EShell/            # Shell command execution
    ├── EWeb/              # Web search + fetch
    ├── Policy/            # ToolPermission, ToolPolicyDecision
    ├── Reader/            # File reader
    └── SubAgent/          # Sub-agent tool

ECAssistantTUI/            # Terminal UI layer (12 files + 9 test files)
├── Controller/            # AppController
└── UI/                    # BaseLayer, SessionLayer, ConfigLayer, HelpLayer
    ├── IGuiConsole.cs     # Console interface
    ├── EGuiConsole.cs     # Full TUI implementation (uses ITerminalOutput)
    ├── ITerminalOutput.cs # Terminal output abstraction
    └── ConsoleTerminalOutput.cs  # Default System.Console implementation

ECAssistantConsole/        # Console entry point (1 file)
└── Program.cs             # Uses EcaCompositionRoot, runs AppController
```

## Dependency Flow

```
ECAssistantCore (no external project deps, only NuGet)
        ▼
ECAssistantTUI  ←── [Core DLL, LLamaSharp]
        ▼
ECAssistantConsole ←── [Core DLL, TUI DLL, LLamaSharp]
```

## Key Interfaces (15)

| Interface | Implementation | Purpose |
|-----------|---------------|---------|
| IEngine | EAgentEngine | Engine lifecycle (Start, Run, Dispose) |
| IInferenceEngine | LlamaInferenceEngine | LLM text generation |
| ILogger | Logger | Structured file logging |
| IMemoryService | MemoryService | Persistent memory with search |
| IContextManager | ContextManager | Context window management |
| IConfigProvider | ConfigProvider | Config value access |
| IFileSystem | FileSystemAdapter | File operations abstraction |
| IHttpClient | HttpClientAdapter | HTTP requests |
| ITerminal | TerminalAdapter | Terminal operations |
| IProcessRunner | ProcessRunner | Process execution |
| IVectorEmbedder | TfidfEmbedder | Vector embeddings |
| IVectorStore | InMemoryVectorStore | Vector storage |
| IOutputRenderer | ConsoleUiRenderer | Output rendering |
| IToolPolicyEvaluator | ToolPolicy | Tool permission evaluation |
| IModelLoader | ModelLoader | Model loading |

## Session Architecture

Each `AgentSession` has:
- Own EAgentEngine (KV cache via own LLamaContext, shares model weights)
- Own orchestrator, tools, memory, output buffer, prompt queue, runner thread
- Sessions share model weights (one GGUF in RAM), nothing else
- `SessionBuilder` — public API for external consumers (e.g., ECSQL)
- `ISessionContext` exposes `SharedWeights` + `SharedModelParams` + `BackgroundTasks` (not SecondaryModel)

## Background Tasks (v11.2)

Replaces the secondary model with StatelessExecutor using shared main weights:

```
LLamaWeights (22 GB, shared, read-only)
├── Main engine → LLamaContext (own KV cache, InteractiveExecutor)
├── Sub-agent engines → own LLamaContext (own KV cache, shared weights)
└── Background tasks → StatelessExecutor (no KV cache, fresh per call)
    ├── DecomposeTaskAsync() — use_llm toggle (keyword fallback)
    └── WireSummaryService() — use_llm toggle (extractive fallback)
```

Config:
```json
"background_tasks": {
    "decompose": { "use_llm": true, "context_size": 4096, "max_tokens": 256, ... },
    "summarize": { "use_llm": true, "context_size": 4096, "max_tokens": 200, ... }
}
```

## Composition Root

`EcaCompositionRoot.Build()` wires:
- Logger, config (AgentConfigBuilder), model path resolution, directory creation
- BackgroundProcessManager, FileWatcherService, SessionBuilder
- Returns `EcaServiceBundle` with all services

## Test Coverage

| Area | Test Files | Tests |
|------|-----------|-------|
| Engine | 18 | ~250 |
| Services | 12 | ~150 |
| Tools | 13 | ~200 |
| Session | 3 | ~50 |
| Memory | 2 | ~40 |
| Config | 2 | ~30 |
| Integration | 9 | ~100 |
| Analysis | 1 | ~20 |
| UI | 9 | ~17 |
| **Total** | **65+9** | **857** |

## Key Constraints

- No statics, no globals, no service locators
- Constructor injection throughout
- One type per file
- All config models init-only (2 documented exceptions)
- All ReadCfg logic consolidated in EToolBase
- EAgentEngine implements IEngine (unsealed)
- EGuiConsole uses ITerminalOutput (no direct Console.Write)
- EcaCompositionRoot is the single wiring point
- One model load — background tasks use StatelessExecutor with shared weights