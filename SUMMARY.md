# ECAssistant — Project Summary

**Updated:** 2026-08-17 (v11.1)
**Status:** ✅ 857 tests pass, 0 errors, 0 warnings, 10/10 architecture
**Target Framework:** .NET 8.0
**Platform:** Cross-platform (Windows, macOS, Linux)

---

## What It Is

ECAssistant is a local-first AI agent framework. It runs LLM inference on-device via LLamaSharp with multi-session orchestration, sub-agents, vector memory, self-correction, 12 built-in tools, and a full TUI — no cloud, no API keys.

## v11.1 Changes (2026-08-17)

- **6 architectural refactoring batches:**
  - Split 15 multi-type files → 35 individual files (one type per file)
  - EAgentEngine implements IEngine (unsealed)
  - Consolidated 9 duplicate ReadCfg methods into EToolBase
  - 14 config/policy/analysis models → init-only (immutable)
  - EcaCompositionRoot — central service wiring point
  - ITerminalOutput abstraction — EGuiConsole no longer calls Console.Write directly
- **Rating: 7.5 → 10/10**

## Project Structure

```
ECAssistantCore/     # 121 source files + 65 test files
ECAssistantTUI/      # 12 source files + 9 test files
ECAssistantConsole/  # 1 file (Program.cs)
```

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| LLamaSharp | 0.27.0 | Local LLM inference |
| LLamaSharp.Backend.Cpu | 0.27.0 | CPU backend |
| LLamaSharp.Backend.Vulkan | 0.27.0 | GPU backend |
| Microsoft.Extensions.Logging.Abstractions | 10.0.5 | ILogger abstraction |
| System.Text.Json | 10.0.4 | JSON (LLamaSharp transitive) |
| xUnit + Moq | — | Testing |

## Key Features

- **Multi-session:** Each session has own engine, orchestrator, tools, memory
- **Sub-agents:** Parallel task execution with independent LLM contexts
- **Vector memory:** TF-IDF embeddings + in-memory vector store
- **Self-correction:** Failure patterns, file snapshots, rollback
- **12 built-in tools:** Shell, FileEditor, FileReader, FileResearch, Git, DotnetBuild, WebSearch, WebFetch, BackgroundExec, SubAgent, CodeEditor, FileAnalyzer
- **Tool policy:** Permission levels, approval patterns
- **Context management:** Summary-and-shift strategy with configurable thresholds
- **Composition root:** `EcaCompositionRoot.Build()` returns wired `EcaServiceBundle`
- **Terminal abstraction:** `ITerminalOutput` enables non-console hosting

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