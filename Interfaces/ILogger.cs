using ECAssistant.Core.Services;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Structured logging interface — file only, headless.
/// </summary>
public interface ILogger
{
    void Initialize(string logFilePath, LogLevel minLevel, Func<string, bool>? componentFilter = null);
    void SetLevel(LogLevel level);
    bool IsDebugEnabled { get; }
    void Debug(string tag, string message);
    void Info(string tag, string message);
    void Warn(string tag, string message);
    void Error(string tag, string message);
    void Error(string tag, string message, Exception ex);
    string GetRecentLines(int count);
    string LogFilePath { get; }
    long LogFileSize { get; }
}