namespace ECAssistant.Core.Interfaces;

public record MemoryEntry(
    string Content,
    string Metadata,
    float[]? Embedding = null
);