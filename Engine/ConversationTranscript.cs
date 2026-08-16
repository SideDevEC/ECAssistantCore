using System.Text.Json;
namespace ECAssistant.Core.Engine;

/// <summary>
/// Full conversation transcript — all messages across all turns.
/// Saved/loaded to disk as a single JSON array for session resumption.
/// </summary>
public class ConversationTranscript
{
    /// <summary>All messages in chronological order</summary>
    public List<TranscriptMessage> Messages { get; set; } = new();

    /// <summary>Timestamp when the conversation started</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Total estimated tokens across all messages</summary>
    public int TotalTokens => Messages.Sum(m => m.EstimatedTokens);

    /// <summary>Current number of messages</summary>
    public int MessageCount => Messages.Count;

    // ─── Add / Append ──────────────────────────────

    public void Add(TranscriptMessage msg)
        { Messages.Add(msg); }

    public void AddRange(List<TranscriptMessage> msgs)
        { Messages.AddRange(msgs); }

    /// <summary>Add a user message.</summary>
    public void AddUser(string content, string source = "user")
         => Messages.Add(TranscriptMessage.User(content, source));

    /// <summary>Add an assistant (LLM) response.</summary>
    public void AddAssistant(string content, string source = "assistant")
        => Messages.Add(TranscriptMessage.Assistant(content, source));

    /// <summary>Add a tool output result.</summary>
    public void AddToolOutput(string content, string toolName = "")
         => Messages.Add(TranscriptMessage.ToolOutput(content, toolName));

    /// <summary>Add system messages (only first call persists).</summary>
    public void AddSystem(string content)
         => Messages.Insert(0, TranscriptMessage.System(content));

    // ─── Persistence ──────────────────────────────

    /// <summary>Serialize the full transcript to a JSON string.</summary>
    public string ToJson()
        => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>Deserialize from a JSON string.</summary>
   // Stateless factory — immutable data class
    public static ConversationTranscript FromJson(string json)
        => JsonSerializer.Deserialize<ConversationTranscript>(json)!;

    /// <summary>Save to disk at the given path.</summary>
    public void SaveToDisk(string path)
         => File.WriteAllText(path, ToJson());

    /// <summary>Load from disk at the given path. Returns null if file doesn't exist.</summary>
   // Stateless factory — immutable data class
    public static ConversationTranscript? LoadFromDisk(string path)
         => !File.Exists(path) ? null : FromJson(File.ReadAllText(path));
}
