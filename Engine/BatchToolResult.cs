using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Engine;

/// <summary>Combined result of an entire batch execution.</summary>
public class BatchToolResult
{
    public List<SingleToolResult> Results { get; set; } = new();
    public List<DependencyGroup> Groups { get; set; } = new();

    public bool AllSucceeded => Results.All(r => r.Succeeded);
    public bool AnySucceeded => Results.Any(r => r.Succeeded);
    public int SuccessCount => Results.Count(r => r.Succeeded);
}
