using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// Converts HTML to plain text while preserving block-level structure.
/// Block tags (p, div, h1-h6, li, br, tr, etc.) produce line breaks
/// BEFORE tag stripping, so the output has readable paragraph/line
/// separation instead of one giant unbroken string.
/// </summary>
public class HtmlTextConverter : IHtmlTextConverter
{
    // Block-level tags that should produce a line break when closed
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "li", "tr", "blockquote", "pre", "hr",
        "ul", "ol", "table", "thead", "tbody", "tfoot",
        "figure", "figcaption", "address", "dd", "dt", "dl"
    };

    // Inline tags that should NOT produce line breaks
    private static readonly HashSet<string> InlineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "span", "strong", "em", "b", "i", "u", "code", "small",
        "sub", "sup", "mark", "abbr", "cite", "q", "time", "var", "kbd"
    };

    /// <inheritdoc />
    public string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = html;

        // 1. Remove script and style blocks entirely
        text = Regex.Replace(text, @"<script[^>]*>.*?</script>", "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<style[^>]*>.*?</style>", "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<noscript[^>]*>.*?</noscript>", "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<head[^>]*>.*?</head>", "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 2. Remove HTML comments
        text = Regex.Replace(text, @"<!--.*?-->", "",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // 3. Convert block-level closing tags to newlines BEFORE stripping
        foreach (var tag in BlockTags)
            text = Regex.Replace(text, $"</{tag}>", "\n",
                RegexOptions.IgnoreCase);

        // 4. Convert <br> variants to newlines
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        // 5. Convert block-level OPENING tags to newlines too (heading starts, list items, etc.)
        foreach (var tag in BlockTags)
            text = Regex.Replace(text, $"<{tag}[^>]*>", "\n",
                RegexOptions.IgnoreCase);

        // 6. Strip all remaining tags
        text = Regex.Replace(text, @"<[^>]+>", "");

        // 7. Decode HTML entities
        text = WebUtility.HtmlDecode(text);

        // 8. Normalize whitespace per line, collapse 3+ newlines to 2
        var lines = text.Split('\n');
        var result = new StringBuilder();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                result.AppendLine(trimmed);
        }

        // Collapse runs of 3+ blank lines to max 2
        var output = result.ToString().Trim();
        output = Regex.Replace(output, @"\n{3,}", "\n\n");

        return output;
    }
}