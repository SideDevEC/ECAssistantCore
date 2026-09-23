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

![ECAssistant in the terminal](demo.gif)

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
var result = await session.Orchestrator.ExecuteMultiStep("Summarize the docs in this folder");
```

**A complete, runnable wiring example lives in [ECAssistantConsole](https://github.com/SideDevEC/ECAssistantConsole/blob/main/ConsoleApplication.cs).**

## What you get

| Capability | What it means for your app |
|---|---|
| **Agent engine** | Multi-session orchestration, sub-agents, self-correction, task planning |
| **Model-tier adaptive harness** | One harness, every model size: small models get step-by-step scaffolding, tighter sampling and strict recipes; large models get a slim profile with more headroom — automatic via `model_tier.mode` |
| **Typed per-tool outputs** | Tools speak for themselves: each tool renders its own model-facing projection (builds collapse to verdict + parsed errors, never raw logs) — smaller context, sharper next decisions. Override one virtual method on your custom tools |
| **Dataflow toolchains** | A later tool call can reference an earlier call's output with `{{0}}` in the same decision — sequential execution and argument substitution without model round-trips (taught to large models only) |
| **Post-edit verification** | Every file-modifying edit is followed by a build/test gate (tier-aware depth); failures are fed back to the model to fix, before you ever see the result |
| **Playbook memory** | Successful multi-step goals are captured as reusable playbooks and replayed on similar future tasks |
| **Context pinning** | Goals, decisions and the touched-file map survive compaction — long sessions don't lose the plot |
| **11 built-in tools + MCP** | Shell, file I/O, code editing, git, dotnet, sub-agents, vision structure, handoff — permission-gated (approve once / always this session / deny — session-scoped, never persisted). Plus [MCP](https://modelcontextprotocol.io) server support: connect any external tool server via stdio or HTTP/SSE — zero dependencies, config-driven |
| **Custom tools** | Implement one interface, register it. That's the whole API. |
| **Memory** | Vector memory (embeddings) + daily notes + curated long-term memory |
| **Local or remote LLM** | GGUF via the bundled [LLM server](https://github.com/SideDevEC/ECAssistantLLM), or any OpenAI-compatible endpoint — identical code path |
| **Model catalog** | Data-driven and pulled live from GitHub at setup time (embedded fallback) — add/update models without code changes |
| **First-run wizard** | Provisions server + models interactively; nothing downloads at chat time |

## What makes it different

- **Vision structure extraction (`EVisionStructure`)** — point the agent at a screenshot, UI image, or PDF page and get back a **fixed, versioned JSON schema** (schemaVersion 1.0): elements (headers, labels, buttons, inputs...) with approximate bounding boxes, label↔control associations, and semantic groups. Never-null design: unknown enums map to `Other`, missing fields get defaults, dangling refs are stripped — downstream code can consume it blind. Server-side GBNF grammar enforcement makes the shape physically guaranteed, and a deterministic validator normalizes semantics on top.
- **Grammar-forced structured decisions** — the agent's act/answer/toolcall decisions are token-level constrained (GBNF), not prompt-asked. Valid tool calls with typed JSON Schema parameters, every time.
- **Interactive checkpoints (`EAskUser`)** — the model escalates genuine ambiguity to a real choice prompt instead of guessing; falls back to autonomous mode when unattended.
- **Self-correction with loop detection** — malformed outputs trigger error-feedback retries; long-range repeat loops are detected and stopped before they burn your budget.
- **Dataflow chains, grammar-free** — `{{N}}` output references ride inside plain string args, so the GBNF grammar doesn't change: small models keep single calls, large models compose multi-step pipelines in one decision.
- **MCP (Model Context Protocol) client** — connect any external tool server via stdio subprocess or HTTP/SSE. Zero NuGet dependencies — pure JSON-RPC 2.0. Tools discovered at runtime, wrapped as native `EToolBase` instances. Per-server approval policy, per-tool whitelist/blacklist, `{{keychain:name}}` secret resolution. Image content flows through the existing vision pipeline.
- **Fuzzy tool edits** — the code editor tolerates imperfect match text: exact → whitespace-tolerant → line-anchored matching with indentation restoration, and a structured ambiguity error instead of guessing.
- **Thinking never leaks** — empty or malformed model turns are retried with corrective feedback instead of surfacing the model's internal reasoning to your users.
- **Thin by design** — 2.8 MB tool, server fetched on demand; local-first privacy with a remote escape hatch in the same code path.

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
