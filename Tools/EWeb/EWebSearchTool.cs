using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Tools.Web;

/// <summary>
/// Web Search Tool — lets the LLM search the web using Bing search results.
/// Scrapes Bing's HTML SERP, parses b_algo result blocks for title, URL, and snippet.
/// No API key needed, no authentication.
/// </summary>
public class EWebSearchTool : EToolBase
{
    private readonly IHttpClient _httpClient;
    private readonly JsonElement? _toolConfig;

    public override string Name => "EWebSearch";

    public override string Description =>
        "Search the web using Bing. Returns search results with titles, URLs, and snippets. " +
        "Use ONLY when the answer genuinely requires up-to-date internet information — " +
        "documentation, API lookups, releases, prices, news. No authentication needed.\n" +
        "Do NOT use: for the current date or time (use EShellAgent), for facts answerable from local files " +
        "or earlier tool results, for questions your own knowledge already covers, or when you are unsure " +
        "what to search. Never call without a specific, meaningful query.";

    public override string UsageExample =>
        "EWebSearch(query:dotnet 8 async streams)\n" +
        "EWebSearch(query:bitcoin price today, max_results:3)";

    public override string GetToolRules() =>
        "Provide a specific search query — never an empty or vague one. " +
        "The results are INPUT for your reasoning, NOT the answer: read the titles and snippets, " +
        "fetch 1-2 promising pages with EWebFetch if needed, then answer in your own words. " +
        "NEVER reply to the user with a list of links. " +
        "If the results are irrelevant, do NOT keep re-searching reworded queries — " +
        "at most try ONE different query, then provide your final answer describing what you found and that it did not answer the question. " +
        "max_results is optional (default 5, max 10).";

    public override bool IsEnabled { get; protected set; } = true;

    private const int DefaultMaxResults = 5;
    private const int MaxAllowedResults = 10;

    // Matches Bing SERP result blocks: <li class="b_algo"> ... </li>
    // We extract title, URL, and snippet from each block.
    private static readonly Regex BAlgoPattern = new(
        @"<li\s+class=""b_algo""[^>]*>(.*?)</li>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Extracts the title text from the first <h2><a ...>Title</a></h2> inside a block
    private static readonly Regex TitlePattern = new(
        @"<h2[^>]*>\s*<a[^>]*>(.*?)</a>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Extracts the href URL from the first <a> inside <h2>
    private static readonly Regex HrefPattern = new(
        @"<h2[^>]*>\s*<a[^>]*href=""([^""]+)""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Extracts the snippet from <p class="b_lineclamp..."> or <div class="b_caption"><p>...</p></div>
    private static readonly Regex SnippetPattern = new(
        @"<p\s+class=""b_lineclamp[^""]*""[^>]*>(.*?)</p>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Bing wraps URLs in redirect links: https://www.bing.com/ck/a?...&u=a1<base64-encoded-url>&...
    // The actual URL is base64-encoded after &u=a1 (URL-safe base64).
    private static readonly Regex BingRedirectPattern = new(
        @"[?&]u=a1([^&]+)",
        RegexOptions.Compiled);

    // User agent for requests
    private const string UserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36";

    public EWebSearchTool(IHttpClient httpClient, EAgentConfig config)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ = config ?? throw new ArgumentNullException(nameof(config));
        config.Tools.TryGetValue(Name, out var tc);
        _toolConfig = tc.ValueKind == JsonValueKind.Undefined ? null : tc;
        IsEnabled = ReadCfg(_toolConfig, "enabled", true);
    }

    public override object GetConfigSection() => new { enabled = true };

    public override async Task<EToolResult> ExecuteAsync(
        Dictionary<string, string?> arguments,
        CancellationToken cancellationToken = default)
    {
        var query = arguments.GetValueOrDefault("query")?.Trim() ?? "";
        var maxResults = int.TryParse(arguments.GetValueOrDefault("max_results"), out var mr) ? mr : DefaultMaxResults;
        maxResults = Math.Clamp(maxResults, 1, MaxAllowedResults);

        if (string.IsNullOrWhiteSpace(query))
            return EToolResult.Failure(Name, "Missing 'query' argument.");

        try
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var searchUrl = $"https://www.bing.com/search?q={encodedQuery}&count=20";

            var headers = new Dictionary<string, string>
            {
                ["User-Agent"] = UserAgent,
                ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
                ["Accept-Language"] = "en-US,en;q=0.9"
            };

            var html = await _httpClient.GetAsync(searchUrl, headers, cancellationToken);

            var results = ParseBingResults(html, maxResults);

            if (results.Count == 0)
                return EToolResult.Success(Name, $"No results found for '{query}'.");

            var sb = new StringBuilder();
            sb.AppendLine($"Search results for: {query}");
            sb.AppendLine(new string('-', 50));
            sb.AppendLine();

            for (var i = 0; i < results.Count; i++)
            {
                var r = results[i];
                sb.AppendLine($"{i + 1}. {r.Title}");
                sb.AppendLine($"   URL: {r.Url}");
                if (!string.IsNullOrEmpty(r.Snippet))
                    sb.AppendLine($"   {r.Snippet}");
                sb.AppendLine();
            }

            return EToolResult.Success(Name,
                $"Found {results.Count} results for '{query}'\n\n{sb.ToString().Trim()}");
        }
        catch (Exception ex)
        {
            return EToolResult.Failure(Name, $"Search error: {ex.Message}");
        }
    }

    private List<BingSearchResult> ParseBingResults(string html, int maxResults)
    {
        var results = new List<BingSearchResult>();

        var matches = BAlgoPattern.Matches(html);
        foreach (Match match in matches)
        {
            if (results.Count >= maxResults)
                break;

            var block = match.Groups[1].Value;

            var title = ExtractTitle(block);
            var url = ExtractUrl(block);
            var snippet = ExtractSnippet(block);

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(url))
            {
                results.Add(new BingSearchResult(title, url, snippet));
            }
        }

        return results;
    }

    private static string ExtractTitle(string block)
    {
        var m = TitlePattern.Match(block);
        if (!m.Success)
            return string.Empty;

        return DecodeHtml(m.Groups[1].Value);
    }

    private static string ExtractUrl(string block)
    {
        var m = HrefPattern.Match(block);
        if (!m.Success)
            return string.Empty;

        var rawUrl = m.Groups[1].Value;

        // Unescape HTML entities
        rawUrl = System.Net.WebUtility.HtmlDecode(rawUrl);

        // Decode Bing redirect URL if present
        var redirectMatch = BingRedirectPattern.Match(rawUrl);
        if (redirectMatch.Success)
        {
            var encoded = redirectMatch.Groups[1].Value;
            // URL-safe base64: replace - with + and _ with /
            encoded = encoded.Replace('-', '+').Replace('_', '/');
            // Pad to multiple of 4
            var padding = (4 - encoded.Length % 4) % 4;
            encoded += new string('=', padding);
            try
            {
                var decoded = Convert.FromBase64String(encoded);
                return System.Text.Encoding.UTF8.GetString(decoded);
            }
            catch
            {
                // If decode fails, return the raw URL
                return rawUrl;
            }
        }

        return rawUrl;
    }

    private static string ExtractSnippet(string block)
    {
        var m = SnippetPattern.Match(block);
        if (!m.Success)
            return string.Empty;

        return DecodeHtml(m.Groups[1].Value);
    }

    /// <summary>
    /// Strip HTML tags and decode common HTML entities from a text fragment.
    /// </summary>
    private static string DecodeHtml(string html)
    {
        if (string.IsNullOrEmpty(html))
            return string.Empty;

        // Remove all HTML tags
        var text = Regex.Replace(html, @"<[^>]+>", "");

        // Decode common HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);

        // Collapse whitespace
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // Truncate very long snippets
        if (text.Length > 300)
            text = text[..297] + "...";

        return text;
    }

    private sealed record BingSearchResult(string Title, string Url, string Snippet);
}