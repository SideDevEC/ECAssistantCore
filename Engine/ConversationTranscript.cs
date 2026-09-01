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

    // Guards all list mutations + SaveToDisk enumeration (thread-safe persistence).
    private readonly object _lock = new();

    /// <summary>Timestamp when the conversation started</summary>
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Total estimated tokens across all messages</summary>
    public int TotalTokens => Messages.Sum(m => m.EstimatedTokens);

    /// <summary>Current number of messages</summary>
    public int MessageCount => Messages.Count;

    // ─── Add / Append ──────────────────────────────

    public void Add(TranscriptMessage msg)
        { lock (_lock) Messages.Add(msg); }

    public void AddRange(List<TranscriptMessage> msgs)
        { lock (_lock) Messages.AddRange(msgs); }

    /// <summary>Add a user message.</summary>
    public void AddUser(string content, string source = "user")
         { lock (_lock) Messages.Add(TranscriptMessage.User(content, source)); }

    /// <summary>Add an assistant (LLM) response.</summary>
    public void AddAssistant(string content, string source = "assistant")
        { lock (_lock) Messages.Add(TranscriptMessage.Assistant(content, source)); }

    /// <summary>Add a tool output result.</summary>
    public void AddToolOutput(string content, string toolName = "")
         { lock (_lock) Messages.Add(TranscriptMessage.ToolOutput(content, toolName)); }

    /// <summary>
    /// Add the system prompt. Consistent with ContextWindow.AddSystemMessage: if the
    /// first message is already a system message it is UPDATED in place (no duplicates);
    /// otherwise the message is inserted at the beginning.
    /// </summary>
    public void AddSystem(string content)
    {
        lock (_lock)
        {
            if (Messages.Count > 0 && Messages[0].Role == "system")
                Messages[0].Content = content;
            else
                Messages.Insert(0, TranscriptMessage.System(content));
        }
    }

    // ─── Persistence ──────────────────────────────

    /// <summary>Serialize the full transcript to a JSON string.</summary>
    public string ToJson()
    {
        // Snapshot under lock so SaveToDisk never enumerates a list being mutated.
        lock (_lock)
            return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Deserialize from a JSON string.</summary>
    // Stateless factory — immutable data class
    public static ConversationTranscript? FromJson(string json)
    {
        // Corrupt/hand-edited JSON must not crash the agent — return null so the
        // caller can fall back to a fresh transcript.
        try
        {
            return JsonSerializer.Deserialize<ConversationTranscript>(json) ?? new ConversationTranscript();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Save to disk at the given path.</summary>
    public void SaveToDisk(string path)
         => File.WriteAllText(path, ToJson());

    /// <summary>Load from disk at the given path. Returns null if file doesn't exist.</summary>
   // Stateless factory — immutable data class
    public static ConversationTranscript? LoadFromDisk(string path)
         => !File.Exists(path) ? null : FromJson(File.ReadAllText(path));
}
