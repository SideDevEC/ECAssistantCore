using System.Text.Json.Serialization;

namespace ECAssistant.Core.Config;

/// <summary>
/// v14.13: Tier-aware post-edit verification gate. After a file-modifying tool call
/// succeeds, the orchestrator runs a build (and optionally a quick filtered test)
/// and feeds failures back into the loop so the model can fix them.
/// Small tier: verify after EVERY file-modifying edit. Large tier: verify only once
/// per run and skip trivially-small edits. Set enabled=false to disable entirely.
/// </summary>
public class VerificationConfig
{
    /// <summary>Master switch for the post-edit verification gate.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Max consecutive failed verification rounds before the loop stops verifying
    /// and asks the model to report the issue. Small tier only (large tier is 1).
    /// </summary>
    [JsonPropertyName("max_rounds")]
    public int MaxRounds { get; set; } = 2;

    /// <summary>
    /// Large-tier trivial-edit skip threshold: single-line edits whose payload
    /// (new_text/content) is shorter than this many chars are not verified.
    /// </summary>
    [JsonPropertyName("trivial_edit_max_chars")]
    public int TrivialEditMaxChars { get; set; } = 200;

    /// <summary>Build command executed after a file-modifying edit.</summary>
    [JsonPropertyName("build_command")]
    public string BuildCommand { get; set; } = "dotnet build --nologo -v q";

    /// <summary>
    /// Optional quick filtered test command run after a successful build
    /// (e.g. "dotnet test --filter Fast"). Null = build only.
    /// </summary>
    [JsonPropertyName("test_command")]
    public string? TestCommand { get; set; }
}