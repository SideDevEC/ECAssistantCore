# Refactor: Remove Secondary Model, Use Shared Main Weights (2026-08-17)

**Summary:** Remove SecondaryModelLoader as a separate model load. Replace with StatelessExecutor using main engine's shared weights. Restructure config so `secondary_model` becomes `background_tasks` with per-task settings (decompose, summarize) including `use_llm` toggle for fallback control.

## Config Structure

Old config:
```json
"secondary_model": {
    "enabled": true,
    "model_path": "/path/to/model.gguf",
    "context_size": 4096,
    "gpu_layers": 0,
    "temperature": 0.1,
    "top_p": 0.8,
    "top_k": 40,
    "repeat_penalty": 1.1,
    "max_tokens": 512,
    "anti_prompts": [...]
}
```

New config:
```json
"background_tasks": {
    "decompose": {
        "use_llm": true,
        "context_size": 4096,
        "max_tokens": 256,
        "temperature": 0.1,
        "top_p": 0.8,
        "top_k": 40,
        "repeat_penalty": 1.1,
        "anti_prompts": ["User:", "Question:"]
    },
    "summarize": {
        "use_llm": true,
        "context_size": 4096,
        "max_tokens": 200,
        "temperature": 0.1,
        "top_p": 0.8,
        "top_k": 40,
        "repeat_penalty": 1.1,
        "anti_prompts": ["User:", "Question:"]
    }
}
```

**Fallback behavior:**
- `decompose.use_llm: false` → keyword-based `TaskPlanner.Decompose()` (instant, no inference)
- `decompose.use_llm: true` → StatelessExecutor with shared main weights
- `summarize.use_llm: false` → simple truncation (keep first + last N messages, drop middle)
- `summarize.use_llm: true` → StatelessExecutor with shared main weights

## Changes

### 1. Config Models
**File:** `Config/Models/SecondaryModelConfig.cs` → delete
**File:** `Config/Models/BackgroundTaskConfig.cs` → new
- `DecomposeConfig`: use_llm, context_size, max_tokens, temperature, top_p, top_k, repeat_penalty, anti_prompts
- `SummarizeConfig`: use_llm, context_size, max_tokens, temperature, top_p, top_k, repeat_penalty, anti_prompts

**File:** `Config/EAgentConfig.cs`
- Replace `SecondaryModel` property with `BackgroundTasks` containing `Decompose` + `Summarize`
- JSON key: `background_tasks`

**File:** `Config/AgentConfigBuilder.cs`
- Remove `EnableSecondaryModel()`, `WithSecondaryModel()` methods
- Add `BackgroundTasks(decomposeConfig, summarizeConfig)` builder method
- Update `Build()` to emit `background_tasks` section

**File:** `appsettings.json`
- Replace `secondary_model` block with `background_tasks` block

### 2. Engine — StatelessExecutor for background tasks
**File:** `Engine/EAgentEngine.cs`
- Remove `_secondaryModel` field, `SecondaryModel` property, `SetSecondaryModel()` method
- `WireSummaryService()`: 
  - Check `config.BackgroundTasks.Summarize.UseLlm`
  - If true → StatelessExecutor with `_weights` + summarize config params
  - If false → simple truncation fallback (keep first 5 + last 5 messages)
- Add `DecomposeTaskAsync(string userRequest)` method:
  - Check `config.BackgroundTasks.Decompose.UseLlm`
  - If true → StatelessExecutor with `_weights` + decompose config params
  - If false → keyword-based `TaskPlanner.Decompose()`
- `GeneratePlanAsync()` — already uses StatelessExecutor ✅ no change

### 3. SessionBuilder — Remove secondary model loading
**File:** `Session/SessionBuilder.cs`
- Remove `EnableSecondaryModel` property
- Remove `LoadSecondaryModel()` method
- Remove the secondary model loading block in `BuildAsync()`

### 4. ISessionContext — Replace SecondaryModel with SharedWeights
**File:** `Session/ISessionContext.cs`
- Remove `SecondaryModelLoader? SecondaryModel` property
- Add `LLamaWeights? SharedWeights` — for tools that need LLM access
- Add `ModelParams? SharedModelParams` — needed to create StatelessExecutor
- Add `BackgroundTaskConfig? BackgroundTasks` — so tools can access decompose/summarize settings

### 5. AgentSession — Wire shared weights
**File:** `Session/AgentSession.cs`
- Remove `SecondaryModel` property from ISessionContext impl
- Remove `SetSecondaryModel()` method
- Add `SharedWeights` returning engine's `_weights`
- Add `SharedModelParams` returning engine's `_modelParams`
- Add `BackgroundTasks` returning config's background task settings

### 6. SecondaryModelLoader → BackgroundTaskRunner
**File:** `Engine/SecondaryModelLoader.cs` → rename to `Engine/BackgroundTaskRunner.cs`
- Remove model loading (no `LLamaWeights.LoadFromFile`)
- Constructor takes: `LLamaWeights sharedWeights`, `ModelParams sharedModelParams`, `BackgroundTaskConfig config`
- Creates StatelessExecutor internally
- Keeps `StripThinkingAndExtractLm()`, `GenerateAsync()`, `SummarizeAsync()`, `DecomposeTaskAsync()`
- `UseLlm` checked inside methods — if false, return fallback result
- No `IsLoaded` property needed (always "loaded" if weights are available)

### 7. Composition Root — Config key awareness
**File:** `Composition/EcaCompositionRoot.cs`
- No model loading changes — just config key rename awareness

### 8. Orchestrator — Use engine decomposition
**File:** `Orchestrator.cs`
- Remove `_engine.SecondaryModel` check for decomposition
- Call `_engine.DecomposeTaskAsync()` instead of `secondary.DecomposeTaskAsync()`
- Fallback to keyword-based is handled inside engine method

### 9. Testing — Update references
**File:** `Testing/TestRunner.cs`
- Remove secondary model loading block (lines 367-386)

**File:** `Tests/Config/EAgentConfigTests.cs`
- Update `SecondaryModel` → `BackgroundTasks` references

## Key Principle
One model load. One set of weights. Background tasks (summarize, decompose) use StatelessExecutor with shared weights + per-task config for context size/max tokens/temperature. Each task has `use_llm` toggle for cheap fallback when LLM isn't needed.

**Status:** Not started — pending approval
**Added:** 2026-08-17