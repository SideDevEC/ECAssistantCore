namespace ECAssistant.Core.Vision;

/// <summary>
/// Semantic role of a detected element group (proximity cluster).
/// Serialized as lowercase strings; unknown values map to Other.
/// </summary>
public enum VisionGroupRole
{
    Form,
    Section,
    Toolbar,
    List,
    Table,
    Other
}
