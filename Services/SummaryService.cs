using System.Text;
using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Services;

/// <summary>
/// Handles LLM-based summarization of old conversation context.
/// Called by ContextWindow when the sliding window exceeds budget.
/// 
/// v2: Takes a Func&lt;string, Task&lt;string&gt;&gt; so the caller provides the prompt
/// and gets back a summary. Falls back to extractive summary if no LLM available.
/// v3 (warm-session): Optional second generator bound to the main session's KV cache.
/// When the main session is active, summarization runs inside it (conversation already
/// prefilled → decode-only cost) instead of a cold stateless call.
/// and gets back a summary. Falls back to extractive summary if no LLM available.
/// </summary>
public class SummaryService
{
    private readonly Func<string, Task<string>>? _generateAsync;
    private readonly Func<string, Task<string>>? _warmSessionGenerateAsync;

    /// <summary>
    /// Create with an async function that takes a prompt string and returns the LLM's response.
    /// Pass null to use extractive (non-LLM) fallback summarization.
    /// </summary>
    /// <param name="generateAsync">Stateless generator (no session / cold prefill).</param>
    /// <param name="warmSessionGenerateAsync">Generator bound to the live main session (warm KV cache). Optional.</param>
    public SummaryService(
        Func<string, Task<string>>? generateAsync = null,
        Func<string, Task<string>>? warmSessionGenerateAsync = null)
    {
        _generateAsync = generateAsync;
        _warmSessionGenerateAsync = warmSessionGenerateAsync;
    }

    /// <summary>
    /// Summarize a list of messages into a concise summary using the LLM.
    /// When <paramref name="preferWarmSession"/> is true and a warm-session generator is wired,
    /// runs in the live session's KV cache (decode-only); any failure falls back to the
    /// stateless generator, then to the extractive summary.
    /// </summary>
    public async Task<string> SummarizeAsync(List<TranscriptMessage> messages, bool preferWarmSession = false)
    {
        if (messages == null || messages.Count == 0)
            return "(No messages to summarize.)";

        if (preferWarmSession && _warmSessionGenerateAsync != null)
        {
            try
            {
                var warmSummary = await InvokeAndCleanAsync(_warmSessionGenerateAsync, BuildPrompt(messages), messages);
                if (warmSummary != null) return warmSummary;
            }
            catch { /* fall through to stateless / extractive */ }
        }

        // If no LLM engine is configured, fall back to extractive summary
        if (_generateAsync == null)
        {
            var extracted = BuildExtractiveSummary(messages);
            return $"[Summary of {messages.Count} messages (extractive)]\n{extracted}";
        }

        // Build the summarization prompt from the messages
        try
        {
            var summary = await _generateAsync.Invoke(BuildPrompt(messages));
            if (string.IsNullOrWhiteSpace(summary))
                return $"[Summary of {messages.Count} messages (extractive — LLM returned empty)]\n{BuildExtractiveSummary(messages)}";
            // v10.7.4: Escape angle brackets to prevent fake XML tags in context
            summary = summary.Trim().Replace("<", "&lt;").Replace(">", "&gt;");
            return $"[Summary of {messages.Count} messages]\n{summary}";
        }
        catch (Exception ex)
        {
            return $"[Summary of {messages.Count} messages (extractive — LLM error)]\n{BuildExtractiveSummary(messages)}\nError: {ex.Message}";
        }
    }

    /// <summary>Run a generator on the shared prompt and normalize the result. Null result means fall back.</summary>
    private async Task<string?> InvokeAndCleanAsync(Func<string, Task<string>> generator, string prompt, List<TranscriptMessage> messages)
    {
        var summary = await generator.Invoke(prompt);
        if (string.IsNullOrWhiteSpace(summary))
            return null;
        // v10.7.4: Escape angle brackets to prevent fake XML tags in context
        summary = summary.Trim().Replace("<", "&lt;").Replace(">", "&gt;");
        return $"[Summary of {messages.Count} messages]\n{summary}";
    }

    /// <summary>Build the shared summarization prompt from messages.</summary>
    private string BuildPrompt(List<TranscriptMessage> messages)
    {
        var oldContent = BuildBlockString(messages);
        return $"You are a summarization assistant. Provide a concise summary of the conversation.\nSTRICT RULES:\n- Keep facts, decisions, and tool results only\n- Do NOT add opinions, suggestions, or extra context\n- Do NOT add greetings, conclusions, or meta-commentary\n- Maximum 3 sentences\n- Plain text only, no formatting\n\nConversation:\n{oldContent}";
    }

    /// <summary>Build an extractive (non-LLM) summary from message content.</summary>
    private string BuildExtractiveSummary(List<TranscriptMessage> messages)
    {
        var sb = new StringBuilder();
        // Take the first 3 messages for context (capped when fewer exist)
        var toTake = Math.Min(3, messages.Count);
        foreach (var msg in messages.Take(toTake))
        {
            if (!string.IsNullOrEmpty(msg.Content))
            {
                sb.Append($"[{msg.Role}");
                if (!string.IsNullOrEmpty(msg.Source))
                    sb.Append($":{msg.Source}");
                sb.Append("] ");
                sb.AppendLine(msg.Content.Length > 200
                    ? msg.Content.Substring(0, 200) + "..."
                    : msg.Content);
            }
        }
        return sb.ToString();
    }

    /// <summary>Build a formatted block from messages for summary prompts.</summary>
    private string BuildBlockString(List<TranscriptMessage> msgs)
    {
        var sb = new StringBuilder();
        foreach (var msg in msgs)
        {
            sb.Append($"[{msg.Role}");
            if (!string.IsNullOrEmpty(msg.Source))
                sb.Append($":{msg.Source}");
            sb.Append("] ");
            // Truncate very long messages to avoid prompt bloat
            var content = msg.Content.Length > 500
                ? msg.Content.Substring(0, 500) + "..."
                : msg.Content;
            sb.AppendLine(content);
        }
        return sb.ToString();
    }
}