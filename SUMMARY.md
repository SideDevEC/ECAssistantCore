# ECAssistant Core — Summary

**Updated:** 2026-08-16 (v11.1)
**Build:** 0 errors, 0 warnings
**Tests:** 857/857 passing
**Repo:** https://github.com/LLamaDudeX/ECAssistantCore.git
**Namespace:** `ECAssistant.Core.*`

## What It Is

A self-contained .NET 8 class library providing a local, offline AI agent engine built on LLamaSharp. Loads GGUF models — no API calls, no cloud. Uses `<lm>` container tag for response parsing with XML-style tool calling. Multi-step autonomous loops, dual memory, sliding context windows, self-correction, sub-agents, parallel tool execution.

## Self-Contained DLL

Everything ships inside `ECAssistant.Core.dll`:
- **LLamaSharp** — inference engine (NuGet packages)
- **System prompts** — SystemPrompt.md, SystemPrompt.Windows.md, SystemPrompt.Mac.md (embedded)
- **Default config** — appsettings.json (embedded)
- **All engine logic** — inference, orchestration, tools, memory, sessions

Any .NET 8 project referencing this DLL gets everything — no additional packages, no external files.

## Project Structure

```
ECAssistantCore.sln
├── ECAssistant.Core.csproj       ← Class library (DLL), RootNamespace=ECAssistant.Core
├── Tests/ECAssistant.Core.Tests.csproj ← 857 tests
├── ARCHITECTURE.md               ← full architecture + library integration guide
├── SUMMARY.md                    ← this file
├── SystemPrompt.md               ← embedded resource
├── SystemPrompt.Mac.md           ← embedded resource
├── SystemPrompt.Windows.md       ← embedded resource
├── appsettings.json              ← embedded resource (default config)
├── Engine/                       ← inference, orchestrator, sub-agents
├── Session/                      ← AgentSession, SessionBuilder, SessionManager, ISessionContext
├── Tools/                        ← 10 built-in tools (EToolBase)
├── Services/                     ← Logger, InferenceParamsFactory, ResourceLoader
├── Config/                       ← AgentConfigBuilder, ConfigLoader
├── Memory/                       ← EMemoryManager, VectorMemoryStore
├── Interfaces/                   ← all abstractions
├── Testing/                      ← TestRunner, MockEngine, EGuiTestHarness
└── UI/                           ← EGuiBase (abstract only)
```

## Library Integration API

- **`AgentConfigBuilder`** — fluent config, JSON-first
- **`SystemPromptBuilder`** — `<lm>` tag rules + OS detect + domain context
- **`SessionBuilder`** — initializes sessions with standard + external tools
- **`EGuiBase`** — abstract UI base for custom UIs
- **`IOutputListener`** — receive live session output
- **`EToolBase`** — subclass for custom tools, has `ISessionContext` access
- **`ISessionContext`** — session info, memory, secondary LLM for tools (no main engine)
- **`AgentSession`** — central hub: create, register tools, attach listeners, `Prompt()`

## Key Stats
- **Project type:** Class library (net8.0)
- **Namespace:** `ECAssistant.Core.*`
- **.cs files:** ~195 (excluding tests)
- **Test files:** ~62
- **Tests:** 857 passing
- **Tools:** 10 built-in + unlimited custom via `EToolBase`
- **Dependencies:** LLamaSharp 0.27.0, Microsoft.Extensions.Logging.Abstractions
- **Resources:** 4 embedded (system prompts + default config)

## Recent Changes (2026-08-16)
- **v11.1: Namespace rename + external tools + ISessionContext**
  - All namespaces: `ECAssistant.*` → `ECAssistant.Core.*`
  - `SessionBuilder.ExternalTools` — inject external tool list via property or `BuildAsync(session, externalTools)` overload
  - `RegisterBuiltInToolsAsync(session, externalTools)` — external tools first, then native
  - `EnsureToolConfigSection()` — writes config defaults for all tools (enabled or disabled)
  - `IsEnabled` check — disabled tools get config but aren't registered
  - `ISessionContext` interface — tools get session info, memory, secondary LLM (no main engine)
  - `EToolBase.Session` property — set before registration via `AgentSession.RegisterTool()`
  - `AgentSession` implements `ISessionContext`
  - `SecondaryModelLoader` — `SemaphoreSlim` for thread-safe `GenerateAsync`
- **v10.25: Repo split + self-contained DLL**
  - Extracted from ECAssistant repo as standalone `ECAssistantCore` repo
  - System prompts + appsettings.json embedded as `EmbeddedResource` in DLL
  - `ResourceLoader` class for reading embedded resources
  - `InferenceParamsFactory` — centralizes all LLamaSharp InferenceParams construction
  - 857 tests moved from App repo to Core repo
  - `ConfigLoader` falls back to embedded config when user file missing