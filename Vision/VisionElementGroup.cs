namespace ECAssistant.Core.Vision;

/// <summary>
/// A semantic group of related elements (e.g. a form, a toolbar, a section).
/// MemberIds reference VisionElement.Id values; validated by the parser —
/// dangling ids are removed with a warning.
/// </summary>
public sealed record VisionElementGroup(
    string Id,
    VisionGroupRole Role,
    IReadOnlyList<string> MemberIds);
