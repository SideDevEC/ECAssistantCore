namespace ECAssistant.Core.Session;

/// <summary>
/// v14.10.2: tri-state approval decision (Claude-Code-style remember-decision).
/// AllowOnce = approve this call only; AllowSession = approve and remember the
/// tool+pattern for the rest of the session; Deny = refuse.
/// </summary>
public enum ApprovalScope
{
    AllowOnce,
    AllowSession,
    Deny
}
