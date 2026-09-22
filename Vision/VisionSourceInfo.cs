namespace ECAssistant.Core.Vision;

/// <summary>
/// Describes the analyzed source (screenshot or PDF page).
/// Never-null design: always present on VisionStructureResult.
/// </summary>
public sealed record VisionSourceInfo(
    VisionSourceKind Kind,
    int Page,
    int Width,
    int Height);
