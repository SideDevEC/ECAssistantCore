# AGENTS.md — ECAssistantCore (AI-consumable)

Compact orientation for AI agents working in this repo. Humans: read README.md → ARCHITECTURE.md.

## Identity
- **Package:** `ECAssistant.Core` v15.0.0 · net8.0 · namespace `ECAssistant.Core.*`
- **Purpose:** The embeddable .NET agent library — sessions, tools, memory, streaming inference. Any .NET 8 app becomes a tool-using agent.
- **Boundary (hard rule):** Core talks to the LLM server EXCLUSIVELY over OpenAI-compatible HTTP. It must NEVER know ECAssistantLLM internals (no LLamaSharp, no model loading, no server types). LLM is a self-contained, separately-versioned product.

## Fast orientation
- 552 types / 107 edges — do NOT read the whole repo. Start with:
  1. `API-INDEX.md` — every public type, one line each (~7.7K tokens)
  2. `RELATIONSHIP-GRAPH.md` — dependency edges (~1.8K tokens)
  3. `docs/packages/<Package>.API.md` — the bounded context for the package you're touching
- Full blueprint: `ARCHITECTURE.md`. Raw logs: `SUMMARY.md`.

## Package map (folder = package)
| Folder | Responsibility |
|---|---|
| `Composition/` | `EcaCompositionRoot` — one call wires config, model resolution, tools, memory, inference |
| `Config/` | `AppConfig` + section configs. JSON names via `JsonPropertyName` (snake_case) |
| `Engine/` | `AgentEngine` — decision loop, structured (GBNF) decoding, self-correction, model-tier profiles |
| `Orchestrator.cs` | The agent brain: multi-step execution, stale-answer guard, verification gates |
| `Session/` | `AgentSession`, `SessionManager`, `IOutputListener`/`ISessionOutput` (streaming), context management |
| `Tools/` | 11 built-in `E*` tools (see below) + MCP client + policy + sub-agents |
| `Memory/`, `ContextPinning/`, `Playbooks/` | Vector memory, daily notes, long-term memory; compaction-surviving pins; replayable playbooks |
| `Setup/` | First-run orchestration: `ServerInstallCoordinator` (server version pinning), `NuGetServerFetcher` |
| `Transport/` | `OpenAIClient` — the ONLY door to the LLM server |
| `Tests/` | xUnit suite (unit + E2E journey tests). Excluded from the packed assembly |

## The 11 built-in tools (all permission-gated, all extend `EToolBase`)
`EShellAgent` (shell) · `EFileReaderTool` · `EFileResearchTool` · `ECodeEditorTool` (fuzzy edits) · `EGitTool` · `EDotnetBuildTool` · `EBackgroundExecTool` · `ESubAgentTool` (child sessions) · `EUserAskTool` (interactive checkpoints) · `EVisionStructureTool` (image/PDF → versioned JSON) · `EHandoffTool` (tier handoff)
- Per-tool model-facing projections: override one virtual method on `EToolBase`.
- Dataflow: `{{0}}`-style refs to earlier tool outputs in the same decision (large models).
- MCP: config-driven external tool servers (stdio or HTTP/SSE), pure JSON-RPC 2.0, zero deps.

## Key types you'll touch most
`AgentSession` (Prompt/ExecuteMultiStep entry) · `Orchestrator` · `AgentEngine` (+ structured decision envelope) · `IOutputListener` (streaming UI hook) · `EToolBase` (custom tools: implement one interface, register) · `SessionBuilder` (wires tools into a session) · `AppConfig` (everything config: `llm_provider`, `model_tier`, `tools`, `sampling`, `context_management`…)

## Config essentials (appsettings.json, snake_case)
```jsonc
{
  "llm_provider": { "mode": "local", "model_id": "qwen35-4b" },          // or "remote" + "endpoint"
  "model_tier":    { "mode": "small" },                                   // small = scaffolding + tight sampling; large = slim profile
  "tools":         { "shell": { "default_policy": "approve" } }           // approve / always / never, per tool
}
```

## Build & test
```bash
export EcaUseProjectRefs=true      # local dev: sibling project refs, no NuGet auth needed
export DOTNET_NODE_REUSE=false
dotnet build ECAssistantCore.sln

# quick unit tests ONLY (never full dotnet test — DB/DataGrid suites are slow)
dotnet test --filter "FullyQualifiedName~StructuredDecision"

# journey E2E (needs a real server; see TestSupport AGENTS.md for env vars)
dotnet test ECAssistantCore/Tests --filter "FullyQualifiedName~JourneySuiteE2E"
```

## Versioning & release
- LOCKSTEP: all ECAssistant packages share one version (currently 15.0.0).
- `Setup/ServerInstallCoordinator.RequiredServerVersion` MUST equal the shipped LLM.Server version.
- Release = tag `core-v15.0.0` → CI publishes Core + TestSupport-linked packages. NEVER tag without Emre's explicit "ship it".
- Interface signature changes: grep ALL implementations across ALL repos, single commit.

## Non-negotiables
- Strict OOP: interfaces + constructor injection, no statics (pure functions/factories excepted), one type per file.
- Tests alongside code: `{ClassName}Tests.cs`.
- No personal info in commits/issues (public + scrubbed repos).
