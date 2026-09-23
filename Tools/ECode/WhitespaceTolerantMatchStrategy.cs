namespace ECAssistant.Core.Tools.Code;

/// <summary>
/// Priority 2: whitespace-tolerant match — indentation-insensitive anchoring.
/// Each line is trimmed and internal whitespace runs are collapsed to a single
/// space, so wrong indentation or sloppy intra-line spacing still matches.
/// </summary>
public sealed class WhitespaceTolerantMatchStrategy : LineMatchStrategyBase
{
    public WhitespaceTolerantMatchStrategy() : base("whitespace-tolerant") { }

    // Pure function: trim + collapse whitespace runs to a single space.
    protected override string Normalize(string line)
        => string.Join(" ", line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}