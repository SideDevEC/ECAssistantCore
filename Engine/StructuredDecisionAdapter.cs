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

        // Neither answer nor toolcalls — treat thinking as the answer (defense in depth;
        // the server's StructuredDecoder normally rejects this first).
        return LLMDecision.FromEnvelope(thinking, null, null);
    }
}
