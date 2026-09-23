namespace ECAssistant.Core.Engine;

/// <summary>
/// v14.17: tier-aware sub-agent brief. Builds the child orchestrator's execution
/// prompt from a SubAgentTask. Every brief carries an explicit output contract so
/// the parent receives a structured, usable result instead of free-form rambling.
/// Small-tier children get step-by-step scaffolding; large-tier children get a
/// terse objective + contract only (slim profile — scaffolding hinders them).
/// </summary>
// Stateless utility — no mutable state; pure text composition
public static class SubAgentBriefBuilder
{
    /// <summary>
    /// Build the execution prompt for a sub-agent run.
    /// Pure function of (task, isLargeTier) — no side effects.
    /// </summary>
    public static string Build(SubAgentTask task, bool isLargeTier)
    {
        var objective = (task?.Description ?? "").Trim();
        if (objective.Length == 0)
            objective = (task?.Prompt ?? "").Trim();
        if (objective.Length == 0)
            objective = "(no objective given)";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<objective>");
        sb.AppendLine(objective);
        sb.AppendLine("</objective>");
        sb.AppendLine();
        sb.AppendLine("OUTPUT CONTRACT — your final answer MUST state, in this order:");
        sb.AppendLine("1. What you did (1-3 sentences).");
        sb.AppendLine("2. Files created or modified (paths, or \"none\").");
        sb.AppendLine("3. Caveats or blockers (or \"none\").");

        if (!isLargeTier)
        {
            sb.AppendLine();
            sb.AppendLine("GUIDANCE:");
            sb.AppendLine("- Work step by step; use only the tools you were given.");
            sb.AppendLine("- Do NOT re-read files you already read.");
            sb.AppendLine("- If a tool fails twice, note it and finish with what you have.");
        }

        return sb.ToString().TrimEnd();
    }
}
