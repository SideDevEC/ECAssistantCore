using System.Text.Json;
namespace ECAssistant.Core.Engine;

/// <summary>
/// A single message in the conversation transcript.
/// Typed roles for structured context management.
/// </summary>
public class TranscriptMessage
{
     /// <summary>Role: system, user, assistant, or tool_output</summary>
    public string Role { get; set; } = "";

     /// <summary>Display name of the message source (e.g., tool name, "user")</summary>
    public string Source { get; set; } = "";

     /// <summary>The actual content/text of the message</summary>
    public string Content { get; set; } = "";

     /// <summary>Estimated token count for this message (pre-computed)</summary>
    public int EstimatedTokens { get; set; }

     /// <summary>When this message was created</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

     // ─── Factories ──────────────────────────────

    // Stateless factory — immutable data class
    public static TranscriptMessage System(string content)
          => new() { Role = "system", Content = content, EstimatedTokens = 0 };

    // Stateless factory — immutable data class
    public static TranscriptMessage User(string content, string source = "user")
          => new() { Role = "user", Source = source, Content = content, EstimatedTokens = 0 };

    // Stateless factory — immutable data class
    public static TranscriptMessage Assistant(string content, string source = "assistant")
          => new() { Role = "assistant", Source = source, Content = content, EstimatedTokens = 0 };

    // Stateless factory — immutable data class
    public static TranscriptMessage ToolOutput(string content, string toolName = "")
          => new() { Role = "tool_output", Source = toolName, Content = content, EstimatedTokens = 0 };

     /// <summary>Serialize to JSON for disk persistence.</summary>
    public string ToJson()
         => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = false });

     /// <summary>Deserialize from JSON.</summary>
    // Stateless factory — immutable data class
    public static TranscriptMessage FromJson(string json)
          => JsonSerializer.Deserialize<TranscriptMessage>(json)!;
}
