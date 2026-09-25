namespace ECAssistant.Core.Services;

/// <summary>Log severity levels (ordered low to high). None disables all logging.</summary>
public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    None = 99
}