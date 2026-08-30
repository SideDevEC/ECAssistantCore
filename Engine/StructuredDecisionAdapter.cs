using System.Text.Json;

namespace ECAssistant.Core.Engine;

/// <summary>
/// v13 Converts a grammar-forced decision envelope ({"thinking", "answer"|"toolcalls"})
/// into the engine's internal decision text. During v13 the internal IR remains the
/// legacy tag text so all downstream parsing/behavior is unchanged; the model itself
/// no longer needs to produce tags — the server's GBNF grammar enforces the envelope.
/// Stateless utility — no mutable state.
/// </summary>
public static class StructuredDecisionAdapter
{
    /// <summary>Convert raw envelope JSON into internal decision text. Throws JsonException on invalid input.</summary>
    public static string Convert(string envelopeJson)
    {
        using var doc = JsonDocument.Parse(envelopeJson);
        var root = doc.RootElement;

        var thinking = root.TryGetProperty("thinking", out var t) ? t.GetString() ?? "" : "";
        var sb = new System.Text.StringBuilder();
        sb.Append("<lm><thinking>").Append(thinking).Append("</thinking>");

        if (root.TryGetProperty("answer", out var answer) && answer.ValueKind == JsonValueKind.String)
        {
            sb.Append("<output>").Append(answer.GetString()).Append("</output></lm>");
            return sb.ToString();
        }

        if (root.TryGetProperty("toolcalls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in calls.EnumerateArray())
            {
                var name = call.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                sb.Append("<toolcall>").Append(name);
                if (call.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
                {
                    foreach (var arg in args.EnumerateObject())
                    {
                        sb.Append('<').Append(arg.Name).Append('>');
                        sb.Append(arg.Value.ValueKind == JsonValueKind.String ? arg.Value.GetString() : arg.Value.GetRawText());
                        sb.Append("</").Append(arg.Name).Append('>');
                    }
                }
                sb.Append("</toolcall>");
            }
            sb.Append("</lm>");
            return sb.ToString();
        }

        // Neither answer nor toolcalls — treat thinking as the answer (defense in depth;
        // the server's StructuredDecoder normally rejects this first).
        sb.Append("<output>").Append(thinking).Append("</output></lm>");
        return sb.ToString();
    }
}
