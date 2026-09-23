using ECAssistant.Core.Session;
using ECAssistant.Core.Services;
using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Interface for building and initializing AgentSessions with standard tools.
/// </summary>
public interface ISessionBuilder
{
    List<ToolBase> ExternalTools { get; set; }
    bool RegisterBuiltInTools { get; set; }
    bool? EnableVectorMemory { get; set; }
    bool? EnableSubAgents { get; set; }
    bool EnableBackgroundTasks { get; set; }
    BackgroundProcessManager BackgroundManager { get; }
    Task BuildAsync(AgentSession session);
    Task BuildAsync(AgentSession session, List<ToolBase>? externalTools);
    void RegisterBuiltInToolsAsync(AgentSession session, List<ToolBase>? externalTools = null);
}