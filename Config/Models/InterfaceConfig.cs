using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// UI and output configuration. Verbose/silent controls token stream visibility.
/// </summary>
public class InterfaceConfig
{
    [JsonPropertyName("history_max_messages")]
    public int HistoryMaxMessages { get; init; } = 50;
    [JsonPropertyName("show_elapsed_time")]
    public bool ShowElapsedTime { get; init; } = true;
    [JsonPropertyName("prompt_prefix")]
    public string PromptPrefix { get; init; } = "[You]: ";
    [JsonPropertyName("response_prefix")]
    public string ResponsePrefix { get; init; } = "[Agent]: ";
    [JsonPropertyName("auto_clear_history_after")]
    public object? AutoClearHistoryAfter { get; init; } = null;

    /// <summary>
    /// Verbose mode: show token stream, debug info, KV cache status, raw outputs.
    /// Default: false (silent mode — only user input, answers, tool results, warnings
    /// and errors reach the UI; diagnostics stay in the transcript).
    /// </summary>
    [JsonPropertyName("verbose")]
    public bool Verbose { get; init; } = false;

    /// <summary>
    /// Silent mode: suppress token stream noise (the ── Token Stream ── headers,
    /// per-token output, token counts). Only show final parsed results and errors.
    /// When true, overrides verbose for token stream output.
    /// Default: false.
    /// </summary>
    [JsonPropertyName("silent")]
    public bool Silent { get; init; } = false;

    /// <summary>
    /// Max agent turns (iterations) per user request. 0 = unlimited.
    /// Default: 10.
    /// </summary>
    [JsonPropertyName("max_turns")]
    public int MaxTurns { get; init; } = 10;

    /// <summary>
    /// Turns budgeted per decomposed sub-task when the orchestrator expands the
    /// turn limit for multi-step goals. Default: 2.
    /// </summary>
    [JsonPropertyName("turns_per_subtask")]
    public int TurnsPerSubtask { get; init; } = 2;

    /// <summary>
    /// Extra buffer turns added on top of (subtasks × turns_per_subtask).
    /// Default: 2.
    /// </summary>
    [JsonPropertyName("subtask_turn_buffer")]
    public int SubtaskTurnBuffer { get; init; } = 2;

    /// <summary>
    /// Run the LLM decomposition + step-mapping pre-planning pass before the main
    /// loop (2-3 extra inference calls). Modern harnesses plan in-loop instead.
    /// Default: false (in-loop planning) — enable for small local models that
    /// benefit from explicit pre-compiled step lists.
    /// </summary>
    [JsonPropertyName("preplanning")]
    public bool Preplanning { get; init; } = false;

    /// <summary>
    /// Verifier contract: the command the agent runs to verify its work after code
    /// edits (act → observe → verify loop). Injected into the system prompt and
    /// surfaced by the orchestrator when edits happened but it was never run.
    /// Empty = no verifier wiring. Example: "dotnet test --filter Fast".
    /// </summary>
    [JsonPropertyName("verify_command")]
    public string VerifyCommand { get; init; } = "";
}