using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Tools.Web;

/// <summary>
/// EWebFetch — fetch a URL, extract main readable content, convert to
/// structured plain text. Supports offset-based paging for long pages.
/// Uses IReadableContentExtractor + IHtmlTextConverter for clean output
/// that an LLM can actually parse.
/// </summary>
public class EWebFetchTool : EToolBase
{
    private readonly IHttpClient _httpClient;
    private readonly IReadableContentExtractor _contentExtractor;
    private readonly IHtmlTextConverter _htmlConverter;
    private readonly JsonElement? _toolConfig;

    public override string Name => "EWebFetch";

    public override string Description =>
        "Fetch a web page URL and return its main readable content as structured plain text. " +
        "Strips navigation, sidebars, footers, and scripts. Preserves paragraph and heading structure. " +
        "Typical chain: EWebSearch → EWebFetch on a promising result URL → answer in your own words. " +
        "Use for reading documentation, articles, API reference pages, blog posts. " +
        "Supports offset to page through long content.";

    public override string GetParameterSchema() =>
        """
        {
          "type": "object", "required": ["url"],
          "properties": {
            "url": { "type": "string", "description": "URL to fetch" },
            "maxchars": { "type": "integer", "description": "Max characters to return" },
            "offset": { "type": "integer", "description": "Character offset to start from" }
          }
        }
        """;
    public override string UsageExample =>
        "EWebFetch(url:https://example.com)\n" +
        "EWebFetch(url:https://example.com/docs, offset:4000)";

    public override string GetToolRules() =>
        "Always provide url. Use offset (in chars) to get the next chunk of a long page. " +
        "Default maxchars is 12000. If output ends with [truncated], call again with offset " +
        "equal to the chars already received to get the next portion.";

    public override bool IsEnabled { get; protected set; } = true;

    private const int DefaultMaxChars = 12000;

    public EWebFetchTool(
        IHttpClient httpClient,
        IReadableContentExtractor contentExtractor,
        IHtmlTextConverter htmlConverter,
        EAgentConfig config)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _contentExtractor = contentExtractor ?? throw new ArgumentNullException(nameof(contentExtractor));
        _htmlConverter = htmlConverter ?? throw new ArgumentNullException(nameof(htmlConverter));
        config.Tools.TryGetValue(Name, out var tc);
        _toolConfig = tc.ValueKind == JsonValueKind.Undefined ? null : tc;
        IsEnabled = ReadCfg(_toolConfig, "enabled", true);
    }

    public override object GetConfigSection() => new { enabled = true, max_output_chars = DefaultMaxChars };

    public override async Task<EToolResult> ExecuteAsync(
        Dictionary<string, string?> arguments,
        CancellationToken cancellationToken = default)
    {
        var url = arguments.GetValueOrDefault("url")?.Trim() ?? "";
        var maxChars = int.TryParse(arguments.GetValueOrDefault("maxchars"), out var mc) ? mc : DefaultMaxChars;
        var offset = int.TryParse(arguments.GetValueOrDefault("offset"), out var off) ? off : 0;

        if (string.IsNullOrWhiteSpace(url))
            return EToolResult.Failure(Name, "Missing required argument: url");

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            return EToolResult.Failure(Name, $"Invalid URL: {url}");

        try
        {
            var html = await _httpClient.GetAsync(url, null, cancellationToken);

            // Pipeline: raw HTML → main content → structured text → page
            var contentHtml = _contentExtractor.Extract(html);
            var text = _htmlConverter.Convert(contentHtml);

            if (text.Length <= offset)
                return EToolResult.Success(Name,
                    $"Fetched {url} — no more content (offset {offset} >= total {text.Length} chars).");

            var available = text.Length - offset;
            var chunkSize = Math.Min(available, maxChars);
            var chunk = text.Substring(offset, chunkSize);

            var header = $"Fetched {url} (offset {offset}, returning {chunkSize} of {text.Length} total chars)\n\n";
            var trailer = chunkSize < available
                ? $"\n\n... [truncated — call EWebFetch with offset {offset + chunkSize} to continue]"
                : "\n\n[End of page]";

            return EToolResult.Success(Name, header + chunk + trailer);
        }
        catch (TaskCanceledException)
        {
            return EToolResult.Failure(Name, $"Request timed out: {url}");
        }
        catch (Exception ex)
        {
            return EToolResult.Failure(Name, $"Error fetching URL: {ex.Message}");
        }
    }
}