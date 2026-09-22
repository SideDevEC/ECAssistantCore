namespace ECAssistant.Core.Vision;

/// <summary>
/// Approximate bounding box of a detected element, in image pixels.
/// Coordinates are model-estimated regions — NOT pixel-accurate. Consumers
/// must treat them as rough placement, not click targets.
/// All values are non-negative integers.
/// </summary>
public sealed record VisionBoundingBox(int X, int Y, int Width, int Height)
{
    public static VisionBoundingBox Empty { get; } = new(0, 0, 0, 0);
}
