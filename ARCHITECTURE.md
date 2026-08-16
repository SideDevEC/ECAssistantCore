# ECAssistant Core — Architecture

**Updated:** 2026-08-16 (v10.25)
**Build:** 0 errors, 0 warnings
**Tests:** 857/857 passing (Core only)

## Project Structure

```
ECAssistantCore.sln
├── ECAssistant.Core.csproj       ← Class library (DLL)
│     OutputType=Library, AssemblyName=ECAssistant.Core
│     NuGet: LLamaSharp 0.27.0, LLamaSharp.Backend.Cpu/Vulkan/Cuda12
│     Embedded resources: SystemPrompt.md, SystemPrompt.Windows.md,
│       SystemPrompt.Mac.md, appsettings.json
│     Self-contained: no external files needed
│
└── Tests/ECAssistant.Core.Tests.csproj
      ProjectReference → ECAssistant.Core.csproj
      857 tests (Engine, Session, Tools, Services, Config, Memory, Integration)
```

## What Core Contains

- **Engine/** — LLM inference, context window, KV cache, orchestrator, sub-agents
- **Session/** — AgentSession, SessionBuilder, SessionManager (headless, no UI)
- **Tools/** — 10 built-in tools, all extend EToolBase
- **Services/** — Logger, LlamaInferenceEngine, InferenceParamsFactory, ResourceLoader
- **Config/** — AgentConfigBuilder, ConfigLoader, EAgentConfig
- **Memory/** — EMemoryManager, VectorMemoryStore
- **Interfaces/** — IProcessRunner, IFileSystem, IHttpClient, ILogger, IInferenceEngine, etc.
- **Testing/** — TestRunner, MockEngine, EGuiTestHarness
- **UI/** — EGuiBase (abstract only), EColor (ANSI codes for UI bridge)
- **SystemPromptBuilder.cs** — fluent prompt builder with `<lm>` tag rules

## What Core Does NOT Contain

- No `Program.cs` (entry point lives in App)
- No `EGuiConsole` (concrete TUI lives in App)
- No `ConsoleUiRenderer`, `HelpLayer`, `SessionLayer`, `LoadingIndicator` (App UI)
- No `IGuiLayer` (App — references EGuiConsole in signatures)

## Dependency Flow

```
Any .NET 8 project
  │
  └── references ECAssistant.Core.dll
        ├── AgentConfigBuilder → Build() → EAgentConfig
        ├── SessionManager(config, modelPath, workingDir, logger)
        ├── SessionBuilder → BuildAsync(session)
        ├── AgentSession → Prompt(input)
        ├── IOutputListener → receive live output
        ├── EToolBase → subclass for custom tools
        └── EGuiBase → implement for custom UI
```

## Library Integration

### Integration Points
- **`AgentConfigBuilder`** — fluent config, JSON-first, generates `appsettings.json`
- **`SystemPromptBuilder`** — required `<lm>` tag rules + OS detection + domain context
- **`SessionBuilder`** — initializes sessions with standard tools
- **`EGuiBase`** (abstract) — implement for custom UI (Avalonia, web, etc.)
- **`IOutputListener`** — implement to receive live output events
- **`EToolBase`** (abstract) — subclass for custom domain-specific tools
- **`AgentSession`** — central hub: create, register tools, attach listeners, `Prompt()`
- **`EAgentEngine.SystemPromptPath` / `SystemPromptText`** — inject custom system prompt

### Self-Contained Resources (v10.25)
- System prompts and default config embedded as `EmbeddedResource` in DLL
- `ResourceLoader` class reads embedded resources at runtime
- `EAgentEngine` loads from embedded first, falls back to file for custom overrides
- `ConfigLoader` falls back to embedded `appsettings.json` when user file missing
- No external files required — just reference the DLL

### InferenceParamsFactory (v10.25)
- Centralizes all LLamaSharp `InferenceParams` construction
- `Create(EAgentConfig)` — from config
- `Create(maxTokens, antiPrompts, temperature, ...)` — explicit values
- Consumers never touch LLamaSharp types directly

### Config Flow (JSON is source of truth)

```
AgentConfigBuilder.Build()
  ├── 1. Resolve working dir: given path + "eca-data"
  ├── 2. appsettings.json exists?
  │     ├── YES → load it (code values IGNORED)
  │     └── NO  → generate from code values + defaults
  └── Result: EAgentConfig
```

### Tool Registration Flow

```
session.RegisterTool(new SqlQueryTool(db, config))
  ├── tool.Name = "SqlQuery"
  ├── config.Tools.ContainsKey("SqlQuery")?
  │     ├── YES → tool reads existing config from JSON
  │     └── NO  → tool.GetConfigSection() → add to config → persist
  ├── tool.IsEnabled checked → skip if false
  └── tool registered on engine
```

### Usage Example (from any .NET 8 app)
```csharp
var config = AgentConfigBuilder.Create()
    .WithModel("/path/to/model.gguf")
    .ContextSize(16384)
    .GpuLayers(15)
    .Build();

var sessionManager = new SessionManager(config, config.Llm.ModelPath, workingDir, logger);
var session = sessionManager.Main;

session.Engine.SystemPromptText = SystemPromptBuilder.Create()
    .WithAgentName("My Assistant")
    .WithDescription("You help with X.")
    .Build();

var builder = new SessionBuilder(config, workingDir, workingDir, logger);
await builder.BuildAsync(session);

session.RegisterTool(new MyCustomTool());
session.AddListener(new MyUiListener());
session.Prompt("Do something");
```

## Tool System

### EToolBase (abstract)
```csharp
public abstract class EToolBase
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string UsageExample { get; }
    public virtual bool IsEnabled { get; protected set; } = true;
    public virtual object GetConfigSection() => new { enabled = true };
    public abstract Task<EToolResult> ExecuteAsync(
        Dictionary<string, string?> arguments, CancellationToken ct = default);
}
```

### EToolResult
```csharp
public record EToolResult(bool Succeeded, string ToolName, string Output, string Error)
{
    public static EToolResult Success(string name, string output);
    public static EToolResult Failure(string name, string error);
}
```

### Built-in Tools (10)
| Tool | Name | Config Section |
|------|------|----------------|
| EShellAgent | EShellAgent | enabled, use_pwsh_core, fallback_to_powershell_exe, max_output_chars |
| EBackgroundExecTool | EBackgroundExec | enabled, workingDir |
| EWebSearchTool | EWebSearch | enabled |
| EDotnetBuildTool | DotnetBuild | enabled |
| EGitTool | EGitTool | enabled, workingDir |
| ECodeEditorTool | ECodeEditor | enabled |
| EFileReaderTool | EFileReader | enabled |
| EFileResearchTool | EFileResearch | enabled, default_extensions, max_chars_per_file, max_files_to_scan, query_limit |
| EWebFetchTool | EWebFetch | enabled |
| ESubAgentTool | ESubAgent | enabled |

## Layers

### Session Layer (`Session/`)
- `AgentSession` — central hub, implements `ISessionOutput`
- `SessionBuilder` — public API for session initialization
- `SessionManager` — multi-session lifecycle, shared weights

### Engine Layer (`Engine/`)
- `EAgentEngine` — LLM inference, context window, KV cache
- `AgentOrchestrator` — multi-step execution, tool dispatch, `<toolcall>` parsing
- `SubAgentManager` — child agents, config injected
- `ParallelToolsExecutor` — dependency-ordered parallel tool execution
- `SecondaryModelLoader` — secondary LLM

### Config Layer (`Config/`)
- `EAgentConfig` — `Tools` is `Dictionary<string, JsonElement>` (dynamic)
- `AgentConfigBuilder` — fluent, JSON-first, `Update()` for persistence
- `ConfigLoader` — JSON deserialization, embedded fallback

### Services Layer (`Services/`)
- `Logger`, `LlamaInferenceEngine`, `InMemoryVectorStore`
- `InferenceParamsFactory` — centralizes LLamaSharp InferenceParams construction
- `ResourceLoader` — reads embedded resources from DLL

### Tools Layer (`Tools/`)
- 10 built-in tools, all extend `EToolBase`
- Subclass `EToolBase` to add custom tools

## Key Constraints
- Engine, tools, memory, services: ZERO references to UI/color/Console
- No hardcoded `~/ECAssistant/` paths in Core
- `<lm>` tag format rules always included via `SystemPromptBuilder`
- All tools extend `EToolBase` — no `ITool` interface
- `AgentConfigBuilder`: JSON is source of truth
- System prompts and appsettings.json embedded in DLL (v10.25)
- LLamaSharp types never leak through public API to consumers