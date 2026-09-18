# ECAssistant.Core

> **The embeddable .NET agent library.** Sessions, tools, memory, streaming inference — a few lines of code turn any .NET 8 app into an intelligent, tool-using agent.

[![NuGet](https://img.shields.io/nuget/v/ECAssistant.Core)](https://www.nuget.org/packages/ECAssistant.Core)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/net-8.0-blue)](https://dot.net)

Part of [ECAssistant](https://github.com/SideDevEC/ECAssistant) — small, lightweight, open source. Your models, your keys, your machine.

## Install

Public on nuget.org — no token, no auth:

```bash
dotnet add package ECAssistant.Core
```

## Hello, agent

```csharp
using ECAssistant.Core.Composition;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Session;

// One call wires config, model resolution, tools, memory and inference
var root = new EcaCompositionRoot(userConfigDir, args);
var services = root.Build();

// Create a session and register an output listener (streamed tokens + tool events)
var sessions = new SessionManager(services.Config, services.ModelPath, workingDir, services.Logger);
var session = sessions.CreateSession("main");
session.AddListener(myListener);          // implements IOutputListener

await services.SessionBuilder.BuildAsync(session, externalTools: null);

// Run the agent — it plans, calls tools, and streams its answer
var loop = new EDecisionLoop(session.Engine, session);
await loop.ExecuteInteractiveLoop("Summarize the docs in this folder");
```

**A complete, runnable wiring example lives in [ECAssistantConsole](https://github.com/SideDevEC/ECAssistantConsole/blob/main/ConsoleApplication.cs).**

## What you get

| Capability | What it means for your app |
|---|---|
| **Agent engine** | Multi-session orchestration, sub-agents, self-correction, task planning |
| **12 built-in tools** | Shell, file I/O, code editing, git, dotnet, web search/fetch — permission-gated (approve / always / never per tool) |
| **Custom tools** | Implement one interface, register it. That's the whole API. |
| **Memory** | Vector memory (embeddings) + daily notes + curated long-term memory |
| **Local or remote LLM** | GGUF via the bundled [LLM server](https://github.com/SideDevEC/ECAssistantLLM), or any OpenAI-compatible endpoint — identical code path |
| **Model catalog** | Data-driven (`model-catalog.json`) — add models without code changes |
| **First-run wizard** | Provisions server + models interactively; nothing downloads at chat time |

## Architecture boundary (by design)

Core talks to the [ECAssistantLLM server](https://github.com/SideDevEC/ECAssistantLLM) **exclusively over OpenAI-compatible HTTP**. It knows nothing about model loading or runtime internals — the server is a self-contained, separately versioned product. Point `llm_provider` at any OpenAI-compatible endpoint and Core doesn't care what's behind it.

- **No in-process LLamaSharp** — zero native model dependencies in your project
- **No embedded blobs** — even the ~170 MB server is fetched on demand at wizard time, never bundled
- **No telemetry** — nothing leaves your machine except the LLM calls you configured

## Docs & architecture

- [ARCHITECTURE.md](ARCHITECTURE.md) — full class map, dependency flow, setup state machine
- [API-INDEX.md](API-INDEX.md) — per-package API reference

## Related repos

| Repo | What it is |
|---|---|
| [ECAssistant](https://github.com/SideDevEC/ECAssistant) | Landing repo & docs |
| [ECAssistantLLM](https://github.com/SideDevEC/ECAssistantLLM) | Self-contained local LLM server (also standalone) |
| [ECAssistantTUI](https://github.com/SideDevEC/ECAssistantTUI) | Reusable terminal UI library |
| [ECAssistantConsole](https://github.com/SideDevEC/ECAssistantConsole) | Reference CLI host — `dotnet tool install -g ECAssistant.Console` |

## License

[MIT](LICENSE) — © 2026 SideDevEC
