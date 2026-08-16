namespace ECAssistant.Core.Orchestration;

/// <summary>Result from the orchestrator after execution completes.</summary>
public class OrchestratorResult
{
    public string FinalOutput { get; set; } = "";
    public int ToolCallsMade { get; set; }
    public OrchestratorStatus Status { get; set; }
}
