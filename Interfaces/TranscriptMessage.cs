namespace ECAssistant.Core.Interfaces;

public record TranscriptMessage(
    string Role,
    string Content,
    string? ToolCallId = null
);