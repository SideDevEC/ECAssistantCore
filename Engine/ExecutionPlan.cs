using System.Text;
namespace ECAssistant.Core.Engine;

/// <summary>
/// An execution plan — the output of the mapping phase.
/// Contains grouped/batched tool calls mapped from sub-tasks.
/// </summary>
public class ExecutionPlan
{
     /// <summary>Ordered list of planned tool calls (already batched/grouped).</summary>
    public List<PlannedToolCall> Calls { get; set; } = new();

     /// <summary>Whether mapping succeeded.</summary>
    public bool IsValid { get; set; } = false;

     /// <summary>Error message if mapping failed.</summary>
    public string Error { get; set; } = "";

     /// <summary>Get a formatted string for injection into the LLM prompt.</summary>
    public string ToPromptString()
     {
        if (!IsValid || Calls.Count == 0) return "(No execution plan)";

        var sb = new StringBuilder();
        sb.AppendLine("[EXECUTION PLAN] Follow this plan exactly. Each item is a concrete tool call to make.");
        sb.AppendLine();
        for (int i = 0; i < Calls.Count; i++)
         {
            var call = Calls[i];
            sb.AppendLine($"Call {i + 1}: {call.ToolName}");
            if (!string.IsNullOrEmpty(call.Description))
                sb.AppendLine($"  Purpose: {call.Description}");
            foreach (var arg in call.Args)
                sb.AppendLine($"  <{arg.Key}>{arg.Value}</{arg.Key}>");
            if (call.CoversSubTasks.Count > 0)
                sb.AppendLine($"  Covers steps: {string.Join(", ", call.CoversSubTasks.Select(s => s + 1))}");
            sb.AppendLine();
         }
        sb.AppendLine("Execute each call in order. You can batch multiple tool calls in one response.");
        return sb.ToString();
     }
}
