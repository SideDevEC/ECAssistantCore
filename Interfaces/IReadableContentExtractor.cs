using System;

namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Extracts the main readable content from an HTML page, discarding
/// navigation, sidebars, footers, cookie banners, and other boilerplate.
/// </summary>
public interface IReadableContentExtractor
{
    /// <summary>
    /// Given raw HTML, return only the primary content portion.
    /// Prefers &lt;article&gt;, &lt;main&gt;, or role="main" containers.
    /// Falls back to &lt;body&gt; with boilerplate tags stripped if no
    /// semantic container is found.
    /// </summary>
    /// <param name="html">Raw full-page HTML</param>
    /// <returns>HTML fragment containing only the main content</returns>
    string Extract(string html);
}