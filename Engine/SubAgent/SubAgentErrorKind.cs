using System.Text;

namespace ECAssistant.Core.Engine;

public enum SubAgentErrorKind
{
    None,
    Timeout,
    TurnsExhausted,
    ToolFailure,
    ResourceLimitExceeded,
    CancelledByMainAgent,
    Exception,
    MaxRetriesExceeded
}