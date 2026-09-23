namespace ECAssistant.Core.Tools.Code;

/// <summary>
/// Priority 3: line-anchored match on significant content only — ALL whitespace
/// is stripped before comparing lines. Catches the sloppiest model patches
/// ("int x=1;" vs "int x = 1;") while the replacement still lands at the
/// matched block with the file's original indentation restored.
/// </summary>
public sealed class LineAnchoredMatchStrategy : LineMatchStrategyBase
{
    public LineAnchoredMatchStrategy() : base("line-anchored") { }

    // Pure function: strip all whitespace from a line.
    protected override string Normalize(string line)
        => string.Concat(line.Where(c => !char.IsWhiteSpace(c)));
}