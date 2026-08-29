namespace ECAssistant.Core.Engine;

/// <summary>
/// Local mirror of server-side KV cache status for one engine session.
/// Extracted from EAgentEngine to group related mutable state (v12 refactor).
/// </summary>
internal sealed class KvCacheState
{
    public bool IsPrefilled { get; set; }
    public uint ContextSize { get; set; }
    public int ApproxTokens { get; set; }
    public double EstimatedMB { get; set; }
    public bool SessionActive { get; set; }
    public int ConsecutiveRewindFailures { get; set; }
}
