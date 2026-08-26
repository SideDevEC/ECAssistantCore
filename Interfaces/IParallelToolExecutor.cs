using ECAssistant.Core.Engine;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Interface for parallel tool execution with dependency-aware scheduling.
/// </summary>
public interface IParallelToolExecutor
{
    Task<BatchToolResult> ExecuteAsync(List<ToolCallRequest> toolCalls, CancellationToken ct = default);
    string CombineResults(BatchToolResult batch);
    string FormatConsoleSummary(BatchToolResult batch);
}