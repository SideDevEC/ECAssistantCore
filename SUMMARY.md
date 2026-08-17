# ECAssistant — Project Summary

**Updated:** 2026-08-18 (v11.4 — conversational gate + system prompt improvements)
**Status:** ✅ 857 tests pass, 0 errors, 13 warnings (pre-existing xUnit analyzers)
**Target Framework:** .NET 8.0
**Platform:** Cross-platform (Windows, macOS, Linux)

---

## What It Is

ECAssistant is a local-first AI agent framework. It runs LLM inference on-device via LLamaSharp with multi-session orchestration, sub-agents, vector memory, self-correction, 12 built-in tools, and a full TUI — no cloud, no API keys.

## v11.4 Changes (2026-08-18)

- **Conversational gate before decomposition:** Skip task decomposition for simple chat questions
  - Fast path: action-verb + step-indicator heuristic (instant, zero cost) — catches ~70-80% of conversational questions
  - LLM fallback: 1-token TASK/CHAT classification (~0.15s) for ambiguous cases
  - `IsConversationalAsync()` on EAgentEngine — StatelessExecutor with shared weights, 2-token output
  - `LooksConversational()` on Orchestrator — static heuristic, no LLM needed
- **System prompt improvement:** Teach LLM to learn from failed thinking in conversation history
  - Added guidance: review past `<thinking>` from failed tool calls, adjust approach, don't repeat failed reasoning
- **CUDA backend fix:** `` condition corrected from `'WINDOWS'` to `'Windows_NT'` (was silently skipping CUDA on Windows)
- **Vulkan backend:** Re-added as fallback for non-NVIDIA Windows GPUs

## v11.3 Changes (2026-08-17 — OOP compliance refactor, complete)

Full OOP compliance audit and refactor — all static methods removed except factory methods on immutable data classes:
- **Split multi-type files:** `EToolBase.cs` → `EToolBase.cs` + `EToolResult.cs`; `BackgroundTasksConfig.cs` → 3 files
- **Removed static mutable state:** `_sForceMockMode` → protected mock-mode constructor on EAgentEngine
- **Deleted deprecated dead code:** `SecondaryModelLoader.cs` + `SecondaryModelConfig.cs` removed
- **Converted static classes to instance:** `StringUtil`, `InferenceParamsFactory`, `ResourceLoader`, `AgentConfigBuilder` — now instance classes with `Default` shared instance
- **SystemPromptBuilder:** private static methods → instance methods
- **EToolBase config helpers:** `ReadConfig<T>`, `ReadCfg<T>`, `IsToolEnabled` → protected instance methods (no more static)
- **AgentConfigBuilder.Update:** static → instance method with `Default` shared instance
- **SubAgentManager:** service dependencies (IProcessRunner, IFileSystem, IHttpClient, BackgroundProcessManager) now injected via constructor
- **EAgentEngine:** optional constructor injection for EMemoryManager, SelfCorrectionManager, ProjectContextManager, TaskPlanner
- **BuildErrorParser:** extracted from EDotnetBuildTool into dedicated instance class
- **ParallelToolExecutor:** CombineResults/FormatConsoleSummary moved from static to instance methods
- **ProcessRunner.CommandExists:** static → instance method

## v11.2 Changes (2026-08-17)

- **Model upgrade:** Qwen3-8B → Qwen3.6-35B-A3B MoE (3B active, 40 tok/s on M3 Ultra)
- **Secondary model removed:** Replaced with StatelessExecutor using shared main weights
  - `secondary_model` config → `background_tasks` with `decompose` + `summarize` sections
  - Each section has `use_llm` toggle for cheap fallback (keyword-based / extractive)
  - One model load in RAM (~22 GB), no duplicate weights
- **Context size reduced:** 16384 → 8192 (per optimization analysis)
- **GPU layers:** Set to 0 (Metal auto-detects on macOS)
- **ISessionContext:** Exposes `SharedWeights` + `SharedModelParams` instead of `SecondaryModel`
- **Engine:** Added `DecomposeTaskAsync()` using StatelessExecutor with shared weights
- **Build fix:** Excluded `TestModelLoad/` from project compilation (moved outside project)

## v11.1 Changes (2026-08-17)

- **6 architectural refactoring batches:**
  - Split 15 multi-type files → 35 individual files (one type per file)
  - EAgentEngine implements IEngine (unsealed)
  - Consolidated 9 duplicate ReadCfg methods into EToolBase
  - 14 config/policy/analysis models → init-only (immutable)
  - EcaCompositionRoot — central service wiring point
  - ITerminalOutput abstraction — EGuiConsole no longer calls Console.Write directly

## Project Structure

```
ECAssistantCore/     # 210 source files + 65 test files
ECAssistantTUI/      # 12 source files + 9 test files
ECAssistantConsole/  # 1 file (Program.cs)
TestModelLoad/       # Standalone model load test (outside project)
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| LLamaSharp | 0.27.0 | Local LLM inference |
| LLamaSharp.Backend.Cpu | 0.27.0 | CPU backend |
| LLamaSharp.Backend.Vulkan | 0.27.0 | GPU backend (Vulkan) |
| LLamaSharp.Backend.Cuda12 | 0.27.0 | GPU backend (CUDA, Windows-only) |
| Microsoft.Extensions.Logging.Abstractions | 10.0.5 | ILogger abstraction |
| System.Text.Json | 10.0.4 | JSON (LLamaSharp transitive) |
| xUnit + Moq | — | Testing |

## Key Features

- **Multi-session:** Each session has own engine, orchestrator, tools, memory
- **Sub-agents:** Parallel task execution with independent LLM contexts
- **Shared weights:** One model load, multiple contexts (main + sub-agents + background tasks)
- **Background tasks:** Decompose + summarize via StatelessExecutor with `use_llm` toggle
- **Vector memory:** TF-IDF embeddings + in-memory vector store
- **Self-correction:** Failure patterns, file snapshots, rollback
- **12 built-in tools:** Shell, FileEditor, FileReader, FileResearch, Git, DotnetBuild, WebSearch, WebFetch, BackgroundExec, SubAgent, CodeEditor, FileAnalyzer
- **Tool policy:** Permission levels, approval patterns
- **Context management:** Summary-and-shift strategy with configurable thresholds
- **Composition root:** `EcaCompositionRoot.Build()` returns wired `EcaServiceBundle`
- **Terminal abstraction:** `ITerminalOutput` enables non-console hosting

## Current Model

- **Model:** Qwen3.6-35B-A3B (MoE, 256 experts, top-8, 3B active)
- **Quant:** Q4_K_M (Unsloth Dynamic 2.0, ~22 GB)
- **Context:** 8192 (main), 4096 (sub-agents), 4096 (background tasks)
- **Speed:** ~40 tok/s generation, ~1.2s prefill (300 chars) on M3 Ultra with Metal

## External Consumers

- **ECSQL** — SQL IDE with embedded ECAssistant via `SessionBuilder` + `AppController`
- Uses `IECAssistantRuntime` + `IECAssistantRuntimeFactory` interfaces (defined in ECSQL Core)

## Build & Run

```bash
# Build all projects
cd ~/Agent/ECAssistant/ECAssistantCore && dotnet build
cd ~/Agent/ECAssistant/ECAssistantTUI && dotnet build
cd ~/Agent/ECAssistant/ECAssistantConsole && dotnet build

# Run
dotnet run --project ~/Agent/ECAssistant/ECAssistantConsole

# Tests
dotnet test ~/Agent/ECAssistant/ECAssistantCore/ECAssistantCore.sln
dotnet test ~/Agent/ECAssistant/ECAssistantTUI/ECAssistantTui.sln
```