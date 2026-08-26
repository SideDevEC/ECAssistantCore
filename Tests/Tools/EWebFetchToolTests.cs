using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Tools.Web;

namespace ECAssistant.Core.Tests.Tools;

public class EWebFetchToolTests
{
    private readonly Mock<IHttpClient> _httpClient = new();
    private readonly Mock<IReadableContentExtractor> _contentExtractor = new();
    private readonly Mock<IHtmlTextConverter> _htmlConverter = new();
    private readonly EAgentConfig _config = new();

    private EWebFetchTool CreateTool()
    {
        // Wire mocks with sensible default behavior: pass-through
        _contentExtractor.Setup(e => e.Extract(It.IsAny<string>()))
                         .Returns<string>(s => s);
        _htmlConverter.Setup(c => c.Convert(It.IsAny<string>()))
                      .Returns<string>(s => s);

        return new EWebFetchTool(
            _httpClient.Object,
            _contentExtractor.Object,
            _htmlConverter.Object,
            _config);
    }

    [Fact]
    public void Name_ReturnsEWebFetch()
    {
        var tool = CreateTool();
        Assert.Equal("EWebFetch", tool.Name);
    }

    [Fact]
    public void Description_ContainsFetch()
    {
        var tool = CreateTool();
        Assert.Contains("fetch", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    // ── Constructor null checks ──

    [Fact]
    public void Constructor_NullHttpClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EWebFetchTool(null!, _contentExtractor.Object, _htmlConverter.Object, _config));
    }

    [Fact]
    public void Constructor_NullContentExtractor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EWebFetchTool(_httpClient.Object, null!, _htmlConverter.Object, _config));
    }

    [Fact]
    public void Constructor_NullHtmlConverter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EWebFetchTool(_httpClient.Object, _contentExtractor.Object, null!, _config));
    }

    // ── ExecuteAsync — missing/invalid url ──

    [Fact]
    public async Task ExecuteAsync_MissingUrl_ReturnsFailed()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>());

        Assert.Contains("Missing required argument: url", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidUrl_ReturnsFailed()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["url"] = "not-a-url" });

        Assert.Contains("Invalid URL", result.Error);
    }

    // ── ExecuteAsync — success ──

    [Fact]
    public async Task ExecuteAsync_ValidUrl_ReturnsContent()
    {
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync("<html><body><p>Hello World</p></body></html>");
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["url"] = "https://example.com" });

        Assert.True(result.Succeeded);
        Assert.Contains("Hello World", result.Output + result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_ScriptContent_RemovedByConverter()
    {
        var rawHtml = "<html><body><script>alert('xss')</script><p>Content</p></body></html>";
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(rawHtml);
        // Simulate converter stripping script
        _contentExtractor.Setup(e => e.Extract(rawHtml)).Returns("<p>Content</p>");
        _htmlConverter.Setup(c => c.Convert("<p>Content</p>")).Returns("Content");
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["url"] = "https://example.com" });

        Assert.True(result.Succeeded);
        Assert.DoesNotContain("alert", result.Output + result.Error);
        Assert.Contains("Content", result.Output + result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_UsesContentExtractor()
    {
        var rawHtml = "<html><body><nav>Nav</nav><article><p>Main</p></article></body></html>";
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(rawHtml);
        _contentExtractor.Setup(e => e.Extract(rawHtml)).Returns("<p>Main</p>");
        _htmlConverter.Setup(c => c.Convert("<p>Main</p>")).Returns("Main");
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["url"] = "https://example.com" });

        Assert.True(result.Succeeded);
        Assert.Contains("Main", result.Output);
        Assert.DoesNotContain("Nav", result.Output);
        _contentExtractor.Verify(e => e.Extract(rawHtml), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_UsesHtmlConverter()
    {
        var html = "<html><body><p>Text</p></body></html>";
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(html);
        _contentExtractor.Setup(e => e.Extract(html)).Returns(html);
        _htmlConverter.Setup(c => c.Convert(html)).Returns("Converted Text");
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["url"] = "https://example.com" });

        Assert.True(result.Succeeded);
        Assert.Contains("Converted Text", result.Output);
        _htmlConverter.Verify(c => c.Convert(html), Times.Once);
    }

    // ── Offset paging ──

    [Fact]
    public async Task ExecuteAsync_Offset_ReturnsContentFromOffset()
    {
        var longText = new string('A', 100) + new string('B', 100);
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync("<html><body><p>" + longText + "</p></body></html>");
        _contentExtractor.Setup(e => e.Extract(It.IsAny<string>())).Returns<string>(s => s);
        _htmlConverter.Setup(c => c.Convert(It.IsAny<string>())).Returns(longText);
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["url"] = "https://example.com",
            ["offset"] = "100",
            ["maxchars"] = "50"
        });

        Assert.True(result.Succeeded);
        Assert.Contains("BBBBB", result.Output);
        Assert.DoesNotContain("AAAA", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_OffsetBeyondContent_ReturnsNoMoreContent()
    {
        var text = "Short content";
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync("<html><body>" + text + "</body></html>");
        _contentExtractor.Setup(e => e.Extract(It.IsAny<string>())).Returns<string>(s => s);
        _htmlConverter.Setup(c => c.Convert(It.IsAny<string>())).Returns(text);
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["url"] = "https://example.com",
            ["offset"] = "1000"
        });

        Assert.True(result.Succeeded);
        Assert.Contains("no more content", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_TruncatedOutput_ContainsOffsetHint()
    {
        var longText = new string('X', 500);
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync("<html><body><p>" + longText + "</p></body></html>");
        _contentExtractor.Setup(e => e.Extract(It.IsAny<string>())).Returns<string>(s => s);
        _htmlConverter.Setup(c => c.Convert(It.IsAny<string>())).Returns(longText);
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["url"] = "https://example.com",
            ["maxchars"] = "50"
        });

        Assert.True(result.Succeeded);
        Assert.Contains("[truncated", result.Output);
        Assert.Contains("offset 50", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_EndOfPage_ContainsEndMarker()
    {
        var text = "Short content here";
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync("<html><body>" + text + "</body></html>");
        _contentExtractor.Setup(e => e.Extract(It.IsAny<string>())).Returns<string>(s => s);
        _htmlConverter.Setup(c => c.Convert(It.IsAny<string>())).Returns(text);
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?>
        {
            ["url"] = "https://example.com",
            ["maxchars"] = "1000"
        });

        Assert.True(result.Succeeded);
        Assert.Contains("[End of page]", result.Output);
    }

    // ── Error handling ──

    [Fact]
    public async Task ExecuteAsync_TaskCanceledException_ReturnsTimeoutMessage()
    {
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new TaskCanceledException());
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["url"] = "https://example.com" });

        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_GeneralException_ReturnsErrorMessage()
    {
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new HttpRequestException("Connection refused"));
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["url"] = "https://example.com" });

        Assert.Contains("Connection refused", result.Error);
    }

    // ── CancellationToken ──

    [Fact]
    public async Task ExecuteAsync_PassesCancellationToken()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), token))
                   .ReturnsAsync("<html><body>OK</body></html>");
        var tool = CreateTool();

        await tool.ExecuteAsync(new Dictionary<string, string?> { ["url"] = "https://example.com" }, token);

        _httpClient.Verify(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), token), Times.Once);
    }

    // ── Empty HTML ──

    [Fact]
    public async Task ExecuteAsync_EmptyHtml_ReturnsEmptyContent()
    {
        _httpClient.Setup(h => h.GetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync("");
        _contentExtractor.Setup(e => e.Extract("")).Returns("");
        _htmlConverter.Setup(c => c.Convert("")).Returns("");
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["url"] = "https://example.com" });

        Assert.True(result.Succeeded);
    }

    // ── Default maxchars ──

    [Fact]
    public async Task ExecuteAsync_DefaultMaxChars_Is12000()
    {
        // Verify the default is 12000 by checking tool config section
        var tool = CreateTool();
        var configSection = tool.GetConfigSection();

        // The config should mention 12000
        Assert.Contains("12000", configSection.ToString());
    }

    // ── Tool rules ──

    [Fact]
    public void GetToolRules_ContainsOffsetGuidance()
    {
        var tool = CreateTool();
        var rules = tool.GetToolRules();

        Assert.Contains("offset", rules, StringComparison.OrdinalIgnoreCase);
    }
}