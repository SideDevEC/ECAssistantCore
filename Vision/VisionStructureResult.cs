namespace ECAssistant.Core.Vision;

/// <summary>
/// The one-and-only output shape of the EVisionStructure tool.
/// Every property is always present (never-null design): absence is expressed
/// as empty arrays / explicit Unknown / confidence 0 — never missing keys.
/// Downstream consumers can rely on a fixed, versioned structure.
/// </summary>
public sealed record VisionStructureResult(
    string SchemaVersion,
    VisionSourceInfo Source,
    IReadOnlyList<VisionElement> Elements,
    IReadOnlyList<VisionElementGroup> Groups,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Current schema version emitted by this Core build.</summary>
    public const string CurrentSchemaVersion = "1.0";
}
