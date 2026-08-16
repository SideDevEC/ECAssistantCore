namespace ECAssistant.Core.Session;

public enum OutputState
{
    /// <summary>Informational message</summary>
    Info,
    /// <summary>Operation succeeded</summary>
    Success,
    /// <summary>Caution / non-critical</summary>
    Warning,
    /// <summary>Failure / critical</summary>
    Error,
    /// <summary>De-emphasized text</summary>
    Dim,
    /// <summary>Emphasized text</summary>
    Bold,
    /// <summary>Plain text / token stream (no styling)</summary>
    Raw,
    /// <summary>System-level message</summary>
    System
}
