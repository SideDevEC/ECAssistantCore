namespace ECAssistant.Core.Interfaces;

public record VectorResult(
    string Content,
    string Metadata,
    float Score
);