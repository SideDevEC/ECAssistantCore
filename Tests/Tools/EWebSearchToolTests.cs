using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Tools.Web;

namespace ECAssistant.Core.Tests.Tools;

public class EWebSearchToolTests
{
    private readonly Mock<IHttpClient> _httpClient = new();
    private readonly EAgentConfig _config = new();

    private EWebSearchTool CreateTool()
    {
        return new EWebSearchTool(_httpClient.Object, _config);
    }

    private const string BingSampleHtml = """
        <html><body>
        <ol id="b_results">
        <li class="b_algo"><h2><a href="https://www.bing.com/ck/a?&&p=abc&u=a1aHR0cHM6Ly9jb2lubWFya2V0Y2FwLmNvbS9jdXJyZW5jaWVzL2JpdGNvaW4v&ntb=1">Bitcoin price today, BTC to USD live price</a></h2><div class="b_caption"><p class="b_lineclamp2">The live Bitcoin price today is $79,086.17 USD with a 24-hour trading volume of $39,103,907,221.84 USD.</p></div></li>
        <li class="b_algo"><h2><a href="https://www.bing.com/ck/a?&&p=def&u=a1aHR0cHM6Ly9iaXRzdGFtcC5vcmcv&ntb=1">Bitcoin Price and Chart - Bitstamp</a></h2><div class="b_caption"><p class="b_lineclamp2">The price of Bitcoin (BTC) is $79,033.03 today as of Aug 26, 2026.</p></div></li>
        <li class="b_algo"><h2><a href="https://example.com/direct-link">Direct Link Result</a></h2><div class="b_caption"><p class="b_lineclamp2">Some snippet without a Bing redirect.</p></div></li>
        </ol>
        </body></html>
        """;

    public EWebSearchToolTests()
    {
        _httpClient.Setup(h => h.GetAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(BingSampleHtml);
    }

    [Fact]
    public void Name_ReturnsEWebSearch()
    {
        var tool = CreateTool();
        Assert.Equal("EWebSearch", tool.Name);
    }

    [Fact]
    public void Description_ContainsSearch()
    {
        var tool = CreateTool();
        Assert.Contains("search", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    // ── Constructor null checks ──

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EWebSearchTool(null!, _config));
    }

    [Fact]
    public void Constructor_NullConfig_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EWebSearchTool(_httpClient.Object, (EAgentConfig)null!));
    }

    // ── ExecuteAsync — missing query ──

    [Fact]
    public async Task ExecuteAsync_MissingQuery_ReturnsFailed()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>());

        Assert.Contains("Missing 'query'", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceQuery_ReturnsFailed()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "   " });

        Assert.Contains("Missing 'query'", result.Error);
    }

    // ── ExecuteAsync — success with results ──

    [Fact]
    public async Task ExecuteAsync_WithResults_ReturnsParsedTitles()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "bitcoin price" });

        Assert.True(result.Succeeded);
        var text = result.Output + result.Error;
        Assert.Contains("Bitcoin price today", text);
        Assert.Contains("Bitstamp", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithResults_ReturnsDecodedUrls()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "bitcoin price" });

        Assert.True(result.Succeeded);
        var text = result.Output + result.Error;
        // The Bing redirect URL for the first result decodes to coinmarketcap.com/currencies/bitcoin/
        Assert.Contains("coinmarketcap.com", text);
        // The second result decodes to bitstamp.org
        Assert.Contains("bitstamp.org", text);
        // Direct (non-redirect) URL should appear as-is
        Assert.Contains("example.com/direct-link", text);
    }

    [Fact]
    public async Task ExecuteAsync_WithResults_ReturnsSnippets()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "bitcoin price" });

        Assert.True(result.Succeeded);
        var text = result.Output + result.Error;
        Assert.Contains("$79,086.17", text);
        Assert.Contains("$79,033.03", text);
    }

    // ── ExecuteAsync — max_results ──

    [Fact]
    public async Task ExecuteAsync_MaxResults_LimitsOutput()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["query"] = "bitcoin price",
            ["max_results"] = "1"
        });

        Assert.True(result.Succeeded);
        var text = result.Output + result.Error;
        Assert.Contains("Bitcoin price today", text);
        Assert.DoesNotContain("Bitstamp", text);
    }

    [Fact]
    public async Task ExecuteAsync_MaxResultsClampedToAtLeast1()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["query"] = "bitcoin price",
            ["max_results"] = "0"
        });

        Assert.True(result.Succeeded);
        var text = result.Output + result.Error;
        // Should still return at least 1 result
        Assert.Contains("Bitcoin price today", text);
    }

    [Fact]
    public async Task ExecuteAsync_MaxResultsClampedToAtMost10()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["query"] = "bitcoin price",
            ["max_results"] = "50"
        });

        Assert.True(result.Succeeded);
        // Sample HTML has only 3 results, so all 3 should appear
        var text = result.Output + result.Error;
        Assert.Contains("Bitcoin price today", text);
        Assert.Contains("Bitstamp", text);
        Assert.Contains("Direct Link Result", text);
    }

    // ── ExecuteAsync — no results ──

    [Fact]
    public async Task ExecuteAsync_NoResults_ReturnsNoResultsMessage()
    {
        _httpClient.Setup(h => h.GetAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("<html><body><ol id='b_results'></ol></body></html>");
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "test" });

        Assert.True(result.Succeeded);
        Assert.Contains("No results found", result.Output + result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyHtml_ReturnsNoResults()
    {
        _httpClient.Setup(h => h.GetAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("");
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "test" });

        Assert.True(result.Succeeded);
        Assert.Contains("No results found", result.Output + result.Error);
    }

    // ── ExecuteAsync — error handling ──

    [Fact]
    public async Task ExecuteAsync_HttpException_ReturnsFailed()
    {
        _httpClient.Setup(h => h.GetAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Network error"));
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "test" });

        Assert.Contains("Network error", result.Error);
    }

    // ── ExecuteAsync — URL construction ──

    [Fact]
    public async Task ExecuteAsync_BuildsBingSearchUrl()
    {
        _httpClient.Setup(h => h.GetAsync(
            It.Is<string>(u => u.StartsWith("https://www.bing.com/search?q=")),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(BingSampleHtml);
        var tool = CreateTool();

        await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "test" });

        _httpClient.Verify(h => h.GetAsync(
            It.Is<string>(u => u.StartsWith("https://www.bing.com/search?q=")),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EncodesQueryInUrl()
    {
        _httpClient.Setup(h => h.GetAsync(
            It.Is<string>(u => u.Contains("hello%20world") || u.Contains("hello+world")),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(BingSampleHtml);
        var tool = CreateTool();

        await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "hello world" });

        _httpClient.Verify(h => h.GetAsync(
            It.Is<string>(u => u.Contains("hello") && (u.Contains("+") || u.Contains("%20"))),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SendsUserAgentHeader()
    {
        Dictionary<string, string>? capturedHeaders = null;
        _httpClient.Setup(h => h.GetAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, string>?, CancellationToken>((_, h, _) => capturedHeaders = h)
            .ReturnsAsync(BingSampleHtml);
        var tool = CreateTool();

        await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "test" });

        Assert.NotNull(capturedHeaders);
        Assert.True(capturedHeaders!.ContainsKey("User-Agent"));
        Assert.Contains("Mozilla", capturedHeaders["User-Agent"]);
    }

    // ── ExecuteAsync — CancellationToken ──

    [Fact]
    public async Task ExecuteAsync_PassesCancellationToken()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        _httpClient.Setup(h => h.GetAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(),
            token))
            .ReturnsAsync(BingSampleHtml);
        var tool = CreateTool();

        await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "test" }, token);

        _httpClient.Verify(h => h.GetAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(),
            token), Times.Once);
    }

    // ── ExecuteAsync — HTML entity decoding ──

    [Fact]
    public async Task ExecuteAsync_DecodesHtmlEntitiesInSnippet()
    {
        var html = """
            <html><body><ol id="b_results">
            <li class="b_algo"><h2><a href="https://example.com">Test &amp; Co</a></h2>
            <div class="b_caption"><p class="b_lineclamp2">Price &gt; $100 &amp; rising</p></div></li>
            </ol></body></html>
            """;
        _httpClient.Setup(h => h.GetAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(html);
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "test" });

        Assert.True(result.Succeeded);
        var text = result.Output + result.Error;
        Assert.Contains("Test & Co", text);
        Assert.Contains("Price > $100 & rising", text);
    }

    // ── ExecuteAsync — HTML tags stripped from snippet ──

    [Fact]
    public async Task ExecuteAsync_StripsHtmlTagsFromSnippet()
    {
        var html = """
            <html><body><ol id="b_results">
            <li class="b_algo"><h2><a href="https://example.com">Result</a></h2>
            <div class="b_caption"><p class="b_lineclamp2">The <strong>best</strong> price is <em>here</em></p></div></li>
            </ol></body></html>
            """;
        _httpClient.Setup(h => h.GetAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(html);
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "test" });

        Assert.True(result.Succeeded);
        var text = result.Output + result.Error;
        Assert.Contains("The best price is here", text);
        Assert.DoesNotContain("<strong>", text);
        Assert.DoesNotContain("<em>", text);
    }

    // ── ExecuteAsync — Bing redirect URL decoding ──

    [Fact]
    public async Task ExecuteAsync_DecodesBingRedirectUrl()
    {
        // aHR0cHM6Ly9leGFtcGxlLmNvbS9wYXRo == base64("https://example.com/path")
        var html = """
            <html><body><ol id="b_results">
            <li class="b_algo"><h2><a href="https://www.bing.com/ck/a?&&p=xyz&u=a1aHR0cHM6Ly9leGFtcGxlLmNvbS9wYXRo&ntb=1">Test</a></h2>
            <div class="b_caption"><p class="b_lineclamp2">Snippet</p></div></li>
            </ol></body></html>
            """;
        _httpClient.Setup(h => h.GetAsync(
            It.IsAny<string>(),
            It.IsAny<Dictionary<string, string>?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(html);
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["query"] = "test" });

        Assert.True(result.Succeeded);
        var text = result.Output + result.Error;
        Assert.Contains("https://example.com/path", text);
    }
}