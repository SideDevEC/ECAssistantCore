namespace ECAssistant.Core.Vision;

/// <summary>
/// One detected element in a vision-structured analysis.
/// Never-null design: Text defaults to "" (empty means no visible text),
/// AssociatedWith defaults to an empty list.
/// </summary>
public sealed record VisionElement(
    string Id,
    VisionElementType Type,
    string Text,
    VisionBoundingBox BoundingBox,
    double Confidence,
    IReadOnlyList<string> AssociatedWith);
