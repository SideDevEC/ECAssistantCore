using System.Text.RegularExpressions;

namespace ECAssistant.Core.Engine;

/// <summary>
/// v14.20: dataflow toolchains — a multi-toolcall decision may reference an
/// earlier call's output inside a later call's arguments with the token
/// <c>{{N}}</c> (N = 0-based position within the decision's toolcalls list).
/// The orchestrator substitutes before execution and forces sequential order —
/// composition without model round-trips. Grammar is untouched (args stay
/// strings), so small models are unaffected; the pattern is taught only in the
/// large-tier prompt.
/// Stateless utility — no mutable state.
/// </summary>
public static partial class ToolCallChainSubstitution
{
    /// <summary>Matches {{N}} (optional inner whitespace), N = call position.</summary>
    [GeneratedRegex(@"\{\{\s*(\d+)\s*\}\}")]
    private static partial Regex ReferencePattern();

    /// <summary>Per-reference character cap — outputs can be large; args must stay bounded.</summary>
    public const int MaxSubstitutionChars = 4000;

    /// <summary>True when any argument contains a {{N}} reference.</summary>
    public static bool HasReferences(IReadOnlyDictionary<string, string?> args)
    {
        foreach (var value in args.Values)
        {
            if (value != null && value.Contains("{{", StringComparison.Ordinal))
                return ReferencePattern().IsMatch(value);
        }
        return false;
    }

    /// <summary>True when ANY call in the decision carries a reference — forces sequential execution.</summary>
    public static bool AnyCallHasReferences(IReadOnlyList<ToolCallRequest> toolCalls)
    {
        for (var i = 0; i < toolCalls.Count; i++)
        {
            if (HasReferences(toolCalls[i].Args)) return true;
        }
        return false;
    }

    /// <summary>
    /// Substitute {{N}} tokens in args with the output of prior call N
    /// (0-based position in the decision). Failed/unexecuted/unknown references
    /// become an explicit marker so the model sees why. Pure function.
    /// </summary>
    // Stateless utility — no mutable state
    public static Dictionary<string, string?> Substitute(
        IReadOnlyDictionary<string, string?> args,
        IReadOnlyList<string?> priorOutputs)
    {
        var result = new Dictionary<string, string?>(args.Count);
        foreach (var (key, value) in args)
        {
            if (value == null || !value.Contains("{{", StringComparison.Ordinal))
            {
                result[key] = value;
                continue;
            }
            result[key] = ReferencePattern().Replace(value, m =>
            {
                var idx = int.Parse(m.Groups[1].Value);
                if (idx < 0 || idx >= priorOutputs.Count || priorOutputs[idx] is null)
                    return "[reference {{" + idx + "}} unavailable: call failed or produced no output]";
                var output = priorOutputs[idx]!;
                if (output.Length > MaxSubstitutionChars)
                    output = output[..MaxSubstitutionChars] + "\n[... truncated to " + MaxSubstitutionChars + " chars for the argument]";
                return output;
            });
        }
        return result;
    }
}