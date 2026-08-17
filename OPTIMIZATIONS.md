# Inference Pipeline Optimizations (2026-08-17)

**Summary:** Identified 10 optimization opportunities to reduce time-to-first-token and per-turn latency.

## High Impact

1. **Prefill without generating** — `PrefillStaticPrefix()` feeds static prefix through `InferAsync` and discards output. Use `context.Prefill()` or `MaxTokens=0` instead of generating+discarding tokens.
   - File: `Engine/EAgentEngine.cs` → `PrefillStaticPrefix()`
   - Effort: Low

2. **Reduce context size to 8K** — 16384 context allocates massive KV cache. Agent tool-call turns rarely need >4K. Drop main engine to 8192, sub-agents to 4096.
   - File: `Config/AgentConfigBuilder.cs` → `ContextSize(16384)` → `8192`
   - Effort: Config change

3. **Cache token counts** — `TokenCounter.Count()` called multiple times per `BuildFullPrompt()` on static content. Cache system block + memory token counts — they don't change between turns.
   - File: `Engine/EAgentEngine.cs` → `BuildFullPrompt()`
   - Effort: Low

## Medium Impact

4. **Memory injection turn 1 only** — `GetMemoryInjection()` runs TF-IDF + keyword search every turn. Only inject on turn 1 or when context resets.
   - File: `Engine/EAgentEngine.cs` → `BuildIncrementalInput()` / `BuildFullPrompt()`
   - Effort: Low

5. **Use incremental path consistently** — `BuildFullPrompt()` rebuilds entire prompt (system+memory+history+user) every turn. With KV cache, history is already in cache. Ensure orchestrator uses `BuildIncrementalInput()` on subsequent turns.
   - File: `Engine/EAgentEngine.cs` → `BuildFullPrompt()` vs `BuildIncrementalInput()`
   - Effort: Low

## Low Impact

6. **Reduce max_tokens for tool turns** — Config allows 8192 max tokens. Tool-call turns need 200-500. Only allow 8192 for final `<output>` turn.
   - File: `Config/Models/InferenceConfig.cs` → `MaxTokens`
   - Effort: Config change

7. **Trim anti-prompts** — 6 anti-prompts checked per token. `"<user>"` risks false triggers on tool output. Trim to `"</s>"` and `"<user>"` only.
   - File: `Config/Models/InferenceConfig.cs` → `AntiPrompts`
   - Effort: Config change

8. **Async transcript save** — `AddToolResult()` writes `transcript.json` to disk synchronously on every tool call. Move to async or batch-save.
   - File: `Engine/EAgentEngine.cs` → `AddToolResult()`
   - Effort: Low

9. **MoE thread tuning** — MoE with 3B active params needs fewer CPU threads than dense 8B. Try `Threads = 4-6` for MoE. More threads can be slower due to sync overhead.
   - File: `Config/AgentConfigBuilder.cs` → `Threads`
   - Effort: Config change

10. **Temperature for MoE** — Slightly higher temp (0.4-0.5) can improve expert routing diversity for MoE models. Speed-neutral.
    - File: `Config/Models/InferenceConfig.cs` → `Temperature`
    - Effort: Config change

## Priority Order

1. Prefill fix (biggest TTFT win)
2. Context size reduction (memory + speed)
3. Cache token counts (CPU)
4. Memory injection turn 1 only (CPU)
5. Async transcript save (I/O)
6. Config tweaks (max_tokens, anti-prompts, threads, temp)

**Status:** Not started — pending approval
**Added:** 2026-08-17