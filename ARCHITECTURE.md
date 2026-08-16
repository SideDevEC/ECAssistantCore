# ECAssistant Core — Architecture

**Updated:** 2026-08-16 (v11.1)
**Build:** 0 errors, 0 warnings
**Tests:** 857/857 passing (Core only)
**Namespace:** `ECAssistant.Core.*`

## Project Structure

```
ECAssistantCore.sln
├── ECAssistant.Core.csproj       ← Class library (DLL)
│     OutputType=Library, AssemblyName=ECAssistant.Core
│     RootNamespace=ECAssistant.Core
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
- **Session/** — AgentSession, SessionBuilder, SessionManager, ISessionContext (headless, no UI)
- **Tools/** — 10 built-in tools, all extend EToolBase
- **Services/** — Logger, LlamaInferenceEngine, InferenceParamsFactory, ResourceLoader
- **Config/** — AgentConfigBuilder, ConfigLoader, EAgentConfig
- **Memory/** — EMemoryManager, VectorMemoryStore
- **Interfaces/** — IProcessRunner, IFileSystem, IHttpClient, ILogger, IInferenceEngine, etc.
- **Testing/** — TestRunner, MockEngine, EGuiTestHarness
- **UI/** — EGuiBase (abstract only), EColor (ANSI codes for UI bridge)
- **SystemPromptBuilder.cs** — fluent prompt builder with `<lm>` tag rules

## What Core Does NOT Contain

- No `Program.cs` (entry point lives in ECAssistantConsole)
- No `EGuiConsole` (concrete TUI lives in ECAssistantTUI)
- No `ConsoleUiRenderer`, `HelpLayer`, `SessionLayer`, `LoadingIndicator` (TUI project)
- No `IGuiConsole` (TUI project — interface for hosting TUI externally)

## Dependency Flow

```
Any .NET 8 project
  │
  └── references ECAssistant.Core.dll
        ├── AgentConfigBuilder → Build() → EAgentConfig
        ├── SessionManager(config, modelPath, workingDir, logger)
        ├── SessionBuilder → ExternalTools → BuildAsync(session, externalTools)
        ├── AgentSession → Prompt(input)
        ├── IOutputListener → receive live output
        ├── EToolBase → subclass for custom tools (has ISessionContext access)
        ├── ISessionContext → session info, memory, secondary LLM for tools
        └── EGuiBase → implement for custom UI
```

## Library Integration

### Integration Points
- **`AgentConfigBuilder`** — fluent config, JSON-first, generates `appsettings.json`
- **`SystemPromptBuilder`** — required `<lm>` tag rules + OS detection + domain context
- **`SessionBuilder`** — initializes sessions with standard + external tools
- **`EGuiBase`** (abstract) — implement for custom UI (Avalonia, web, etc.)
- **`IOutputListener`** — implement to receive live output events
- **`EToolBase`** (abstract) — subclass for custom domain-specific tools
- **`ISessionContext`** — session info, memory, secondary LLM (no main engine access)
- **`AgentSession`** — central hub: create, register tools, attach listeners, `Prompt()`
- **`EAgentEngine.SystemPromptPath` / `SystemPromptText`** — inject custom system prompt

### External Tool Registration (v11.1)

```
Host creates tool instances with dependencies
  ↓
SessionBuilder.ExternalTools = new() { tool1, tool2, ... }
  — OR —
await builder.BuildAsync(session, externalTools);
  ↓
RegisterBuiltInToolsAsync(session, externalTools)
  ├── External tools first:
  │     ├── EnsureToolConfigSection(tool) — write GetConfigSection() if missing
  │     └── If tool.IsEnabled → session.RegisterTool(tool)
  │           ├── tool.Session = session (ISessionContext)
  │           ├── Config section check (already exists)
  │           └── engine.RegisterTool(tool)
  └── Native tools (same flow)
```

### ISessionContext (v11.1)

Tools receive read-only session access via `EToolBase.Session`:
```csharp
public interface ISessionContext
{
    string Key { get; }
    string? Label { get; }
    ToolPolicy Policy { get; }
    int ContextTokens { get; }
    uint MaxTokens { get; }
    SecondaryModelLoader? SecondaryModel { get; }  // tools can ask LLM questions
    EMemoryManager Memory { get; }
    VectorMemoryStore? VectorMemory { get; }
}
```
- Set in `AgentSession.RegisterTool()` before engine registration
- No access to main engine's `GenerateAsync` or `Prompt` — prevents orchestration interference
- `SecondaryModelLoader.GenerateAsync` is thread-safe via `SemaphoreSlim`

### Config Flow (JSON is source of truth)

```
AgentConfigBuilder.Build()
  ├── 1. Resolve working dir
  ├── 2. appsettings.json exists?
  │     ├── YES → load it (code values IGNORED)
  │     └── NO  → generate from code values + defaults
  └── Result: EAgentConfig
```

### Tool Registration & Config Persistence

```
session.RegisterTool(tool)
  ├── tool.Session = this (ISessionContext set)
  ├── config.Tools.ContainsKey(tool.Name)?
  │     ├── YES → tool already has config
  │     └── NO  → tool.GetConfigSection() → add to config → AgentConfigBuilder.Update() → persist to appsettings.json
  └── engine.RegisterTool(tool)
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

var builder = new SessionBuilder(config, workingDir, workingDir, logger);
builder.ExternalTools.Add(new MyCustomTool(myDependency));
await builder.BuildAsync(session);

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
    public ISessionContext? Session { get; internal set; }  // v11.1
    public abstract Task<EToolResult> ExecuteAsync(
        Dictionary<string, string?> arguments, CancellationToken ct = default);
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

## Key Constraints
- Engine, tools, memory, services: ZERO references to UI/color/Console
- No hardcoded `~/ECAssistant/` paths in Core
- `<lm>` tag format rules always included via `SystemPromptBuilder`
- All tools extend `EToolBase` — no `ITool` interface
- `AgentConfigBuilder`: JSON is source of truth
- System prompts and appsettings.json embedded in DLL (v10.25)
- LLamaSharp types never leak through public API to consumers
- `ISessionContext` exposes no main engine inference — tools can't call `Prompt()` or `GenerateAsync`