using System.Text.Json;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Engine;

/// <summary>
/// v14 Parses a grammar-forced decision envelope ({"thinking", "answer"|"toolcalls"})
/// directly into an <see cref="LLMDecision"/>. No intermediate tag text — the tag IR
/// has been removed; the envelope JSON is the only structured wire format.
/// Stateless utility — no mutable state.
/// </summary>
public static class StructuredDecisionAdapter
{
    /// <summary>Parse raw envelope JSON into an LLMDecision. Throws JsonException on invalid input.</summary>
    public static LLMDecision ParseDecision(string envelopeJson)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        var root = doc.RootElement;

        var thinking = root.TryGetProperty("thinking", out var t) ? t.GetString() ?? "" : "";

        if (root.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String)
         {
            return LLMDecision.FromEnvelope(thinking, answer.GetString(), null);
         }

        if (root.TryGetProperty("toolcalls", out var calls) && calls.ValueKind == JsonValueKind.Array)
         {
            var toolCalls = new List<(string Name, Dictionary<string, string> Args)>();
            foreach (var call in calls.EnumerateArray())
             {
                var name = call.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(name)) continue;

                var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (call.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object)
                 {
                    foreach (var arg in a.EnumerateObject())
                     {
                        var value = arg.Value.ValueKind == JsonValueKind.String
                            ? arg.Value.GetString() ?? ""
                            : arg.Value.GetRawText();
                        args[arg.Name] = value;
                     }
                 }

                toolCalls.Add((name, args));
             }

            return LLMDecision.FromEnvelope(thinking, null, toolCalls);
         }

        return LLMDecision.FromEnvelope(thinking, null, null);
    }

    /// <summary>
    /// v14.10.2: envelope-tolerant extraction for stateless side-channel calls
    /// (decompose, plan, summarize). Envelope-trained models often wrap their
    /// plain-text reply in {"thinking","answer"} even when the prompt asks for
    /// free text. Returns the answer text when <paramref name="raw"/> is (or
    /// contains) a valid decision envelope; null when it is plain text.
    /// Stateless utility — no mutable state.
    /// </summary>
    public static string? TryExtractAnswer(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith('{'))
            return null;

        // Find the matching closing brace of the top-level object (string-aware).
        int end = -1;
        var inString = false;
        var escape = false;
        var depth = 0;
        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            if (escape) { escape = false; continue; }
            if (ch == '\\') { escape = true; continue; }
            if (ch == '"') inString = !inString;
            if (inString) continue;
            if (ch == '{') depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0) { end = i; break; }
            }
        }
        if (end < 0)
            return null;

        try
        {
            var decision = ParseDecision(trimmed.Substring(0, end + 1));
            return decision.WantsDirectAnswer && !string.IsNullOrWhiteSpace(decision.AnswerText)
                ? decision.AnswerText
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
