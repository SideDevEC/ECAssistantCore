using ECAssistant.Core.Services;

namespace ECAssistant.Core.Tests.Services;

public class ReadableContentExtractorTests
{
    private readonly ReadableContentExtractor _extractor = new();

    [Fact]
    public void Extract_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", _extractor.Extract(""));
        Assert.Equal("", _extractor.Extract(null!));
    }

    [Fact]
    public void Extract_ArticleTag_PrioritizedOverBody()
    {
        var html = "<html><body><nav>Navigation</nav><article><p>Main Content</p></article><footer>Footer</footer></body></html>";

        var result = _extractor.Extract(html);

        Assert.Contains("Main Content", result);
        Assert.DoesNotContain("Navigation", result);
        Assert.DoesNotContain("Footer", result);
    }

    [Fact]
    public void Extract_MainTag_UsedWhenNoArticle()
    {
        var html = "<html><body><nav>Nav</nav><main><p>Main Content</p></main><aside>Sidebar</aside></body></html>";

        var result = _extractor.Extract(html);

        Assert.Contains("Main Content", result);
        Assert.DoesNotContain("Nav", result);
    }

    [Fact]
    public void Extract_RoleMain_UsedWhenNoArticleOrMain()
    {
        var html = "<html><body><div role=\"main\"><p>Main Content</p></div><div class=\"sidebar\">Sidebar</div></body></html>";

        var result = _extractor.Extract(html);

        Assert.Contains("Main Content", result);
    }

    [Fact]
    public void Extract_NoSemanticTags_StripsBoilerplateFromBody()
    {
        var html = "<html><body><nav>Nav</nav><aside>Aside</aside><footer>Footer</footer><p>Content</p></body></html>";

        var result = _extractor.Extract(html);

        Assert.Contains("Content", result);
        Assert.DoesNotContain("Nav", result);
        Assert.DoesNotContain("Aside", result);
        Assert.DoesNotContain("Footer", result);
    }

    [Fact]
    public void Extract_BoilerplateClass_RemovedFromBody()
    {
        var html = "<html><body>" +
                   "<div class=\"nav-bar\">Menu items</div>" +
                   "<div class=\"content\"><p>Real content</p></div>" +
                   "<div class=\"footer-links\">Copyright</div>" +
                   "</body></html>";

        var result = _extractor.Extract(html);

        Assert.Contains("Real content", result);
        Assert.DoesNotContain("Menu items", result);
        Assert.DoesNotContain("Copyright", result);
    }

    [Fact]
    public void Extract_ScriptInArticle_Removed()
    {
        var html = "<html><body><article><p>Text</p><script>var x = 1;</script></article></body></html>";

        var result = _extractor.Extract(html);

        Assert.DoesNotContain("var x", result);
        Assert.Contains("Text", result);
    }

    [Fact]
    public void Extract_SocialShareInsideArticle_Removed()
    {
        var html = "<html><body><article>" +
                   "<p>Main article text</p>" +
                   "<div class=\"share-buttons\">Share on Twitter</div>" +
                   "<nav class=\"related-posts\">Related: other article</nav>" +
                   "</article></body></html>";

        var result = _extractor.Extract(html);

        Assert.Contains("Main article text", result);
        Assert.DoesNotContain("Share on Twitter", result);
        Assert.DoesNotContain("Related", result);
    }

    [Fact]
    public void Extract_FormTag_Removed()
    {
        var html = "<html><body><form><input type=\"text\"></form><p>Content</p></body></html>";

        var result = _extractor.Extract(html);

        Assert.DoesNotContain("input", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content", result);
    }

    [Fact]
    public void Extract_Iframe_Removed()
    {
        var html = "<html><body><iframe src=\"ads.html\"></iframe><p>Content</p></body></html>";

        var result = _extractor.Extract(html);

        Assert.DoesNotContain("ads.html", result);
        Assert.Contains("Content", result);
    }

    [Fact]
    public void Extract_NoBodyTag_ReturnsRawStripped()
    {
        var html = "<article><p>Content</p></article>";

        var result = _extractor.Extract(html);

        Assert.Contains("Content", result);
    }
}