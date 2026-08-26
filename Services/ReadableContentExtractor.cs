using System;
using System.Text.RegularExpressions;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Services;

/// <summary>
/// Extracts the main readable content from a full HTML page.
/// Strategy:
///   1. If &lt;article&gt; exists, use its content.
///   2. Else if &lt;main&gt; or role="main" exists, use that.
///   3. Else strip boilerplate tags (nav, aside, footer, header, form,
///      button containers) from &lt;body&gt; and use what remains.
/// Always removes script/style/noscript/iframe/svg/canvas/svg before returning.
/// </summary>
public class ReadableContentExtractor : IReadableContentExtractor
{
    // Tags that are almost always boilerplate, not main content
    private static readonly string[] BoilerplateTags =
        { "nav", "aside", "footer", "script", "style", "noscript",
          "iframe", "svg", "canvas", "form", "button" };

    // Common boilerplate class/id patterns
    private static readonly Regex BoilerplateClassPattern = new(
        @"class\s*=\s*[""'](?:[^""']*\b(?:nav|sidebar|footer|header|menu|breadcrumb|cookie|banner|advert|promo|social|share|subscribe|newsletter|popup|modal|skip-link)\b[^""']*)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BoilerplateIdPattern = new(
        @"\sid\s*=\s*[""'](?:[^""']*\b(?:nav|sidebar|footer|header|menu|breadcrumb|cookie|banner|advert|promo|social|share|subscribe|newsletter|popup|modal|skip-link)\b[^""']*)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    public string Extract(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        // 1. Try <article> — most specific content container
        var article = ExtractFirstTag(html, "article");
        if (!string.IsNullOrWhiteSpace(article))
            return CleanBoilerplate(article);

        // 2. Try <main> or role="main"
        var main = ExtractFirstTag(html, "main");
        if (!string.IsNullOrWhiteSpace(main))
            return CleanBoilerplate(main);

        var roleMain = ExtractRoleMain(html);
        if (!string.IsNullOrWhiteSpace(roleMain))
            return CleanBoilerplate(roleMain);

        // 3. Fall back to <body> with boilerplate stripped
        var body = ExtractFirstTag(html, "body");
        if (string.IsNullOrWhiteSpace(body))
            body = html; // no body tag, use raw

        return StripBoilerplateTags(body);
    }

    /// <summary>
    /// Extract the inner HTML of the first occurrence of a given tag.
    /// </summary>
    private static string ExtractFirstTag(string html, string tag)
    {
        var pattern = $@"<{tag}[^>]*>(.*?)</{tag}>";
        var match = Regex.Match(html, pattern,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// Extract content from element with role="main".
    /// </summary>
    private static string ExtractRoleMain(string html)
    {
        // Match <div ... role="main" ...> or <section ... role="main" ...>
        var pattern = @"<(?:div|section|article)[^>]*\brole\s*=\s*[""']main[""'][^>]*>(.*?)</(?:div|section|article)>";
        var match = Regex.Match(html, pattern,
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// Remove boilerplate elements (by tag, class, or id patterns) from an HTML fragment.
    /// </summary>
    private static string StripBoilerplateTags(string html)
    {
        // Remove entire boilerplate tag blocks
        foreach (var tag in BoilerplateTags)
        {
            html = Regex.Replace(html, $@"<{tag}[^>]*>.*?</{tag}>", "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }

        // Remove elements with boilerplate class/id patterns
        // Match entire <div ... class="nav-bar ..."> ... </div> blocks (greedy to closing div)
        html = Regex.Replace(html,
            @"<div[^>]*(?:class|id)\s*=\s*[""'](?:[^""']*\b(?:nav|sidebar|footer|header|menu|breadcrumb|cookie|banner|advert|promo|social|share|subscribe|newsletter|popup|modal|skip-link)\b[^""']*)[""'][^>]*>.*?</div>",
            "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Also handle non-div elements with boilerplate classes (p, ul, etc.)
        html = Regex.Replace(html,
            @"<(?!div)[a-z0-9]+[^>]*class\s*=\s*[""'](?:[^""']*\b(?:cookie|banner|advert|promo|social|subscribe|newsletter|popup|modal|skip-link)\b[^""']*)[""'][^>]*>.*?</[a-z0-9]+>",
            "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return html;
    }

    /// <summary>
    /// Remove script/style/iframe/etc. but keep content structure intact.
    /// Used when we already have a good container (article/main).
    /// </summary>
    private static string CleanBoilerplate(string html)
    {
        var tags = new[] { "script", "style", "noscript", "iframe", "svg", "canvas" };
        foreach (var tag in tags)
        {
            html = Regex.Replace(html, $@"<{tag}[^>]*>.*?</{tag}>", "",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
        }

        // Remove share/social/nav sub-elements that may be inside articles
        html = Regex.Replace(html,
            @"<(?:nav|aside|div)[^>]*class\s*=\s*[""'](?:[^""']*\b(?:share|social|nav|breadcrumb|related|comments)\b[^""']*)[""'][^>]*>.*?</(?:nav|aside|div)>",
            "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        return html;
    }
}