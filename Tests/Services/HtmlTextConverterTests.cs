using ECAssistant.Core.Services;

namespace ECAssistant.Core.Tests.Services;

public class HtmlTextConverterTests
{
    private readonly HtmlTextConverter _converter = new();

    [Fact]
    public void Convert_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal("", _converter.Convert(""));
        Assert.Equal("", _converter.Convert(null!));
    }

    [Fact]
    public void Convert_SimpleParagraph_PreservesText()
    {
        var html = "<html><body><p>Hello World</p></body></html>";

        var result = _converter.Convert(html);

        Assert.Contains("Hello World", result);
    }

    [Fact]
    public void Convert_MultipleParagraphs_PreservesLineBreaks()
    {
        var html = "<html><body><p>First</p><p>Second</p><p>Third</p></body></html>";

        var result = _converter.Convert(html);

        Assert.Contains("First", result);
        Assert.Contains("Second", result);
        Assert.Contains("Third", result);
        // Each paragraph should be on its own line
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 3);
    }

    [Fact]
    public void Convert_HeadingTags_ProduceNewlines()
    {
        var html = "<html><body><h1>Title</h1><p>Body text</p></body></html>";

        var result = _converter.Convert(html);

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 2);
        Assert.Contains("Title", lines[0]);
        Assert.Contains("Body text", lines[1]);
    }

    [Fact]
    public void Convert_ScriptTag_Removed()
    {
        var html = "<html><body><script>alert('xss')</script><p>Content</p></body></html>";

        var result = _converter.Convert(html);

        Assert.DoesNotContain("alert", result);
        Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Content", result);
    }

    [Fact]
    public void Convert_StyleTag_Removed()
    {
        var html = "<html><head><style>body { color: red; }</style></head><body><p>Text</p></body></html>";

        var result = _converter.Convert(html);

        Assert.DoesNotContain("color: red", result);
        Assert.Contains("Text", result);
    }

    [Fact]
    public void Convert_HeadSection_Removed()
    {
        var html = "<html><head><title>Page Title</title><meta charset=\"utf-8\"></head><body><p>Body</p></body></html>";

        var result = _converter.Convert(html);

        Assert.DoesNotContain("Page Title", result);
        Assert.DoesNotContain("meta", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Body", result);
    }

    [Fact]
    public void Convert_HtmlEntities_Decoded()
    {
        var html = "<html><body><p>Hello &amp; Goodbye</p></body></html>";

        var result = _converter.Convert(html);

        Assert.Contains("Hello & Goodbye", result);
    }

    [Fact]
    public void Convert_BrTags_ProduceLineBreaks()
    {
        var html = "<html><body><p>Line1<br>Line2<br/>Line3</p></body></html>";

        var result = _converter.Convert(html);

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 2);
    }

    [Fact]
    public void Convert_ListItems_ProduceSeparateLines()
    {
        var html = "<html><body><ul><li>Apple</li><li>Banana</li><li>Cherry</li></ul></body></html>";

        var result = _converter.Convert(html);

        Assert.Contains("Apple", result);
        Assert.Contains("Banana", result);
        Assert.Contains("Cherry", result);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 3);
    }

    [Fact]
    public void Convert_HtmlComment_Removed()
    {
        var html = "<html><body><!-- a comment --><p>Text</p></body></html>";

        var result = _converter.Convert(html);

        Assert.DoesNotContain("a comment", result);
        Assert.Contains("Text", result);
    }

    [Fact]
    public void Convert_NoscriptTag_Removed()
    {
        var html = "<html><body><noscript>Enable JS</noscript><p>Content</p></body></html>";

        var result = _converter.Convert(html);

        Assert.DoesNotContain("Enable JS", result);
        Assert.Contains("Content", result);
    }

    [Fact]
    public void Convert_CollapsesExcessiveBlankLines()
    {
        var html = "<html><body><p>A</p><div></div><div></div><div></div><p>B</p></body></html>";

        var result = _converter.Convert(html);

        // Should not have more than 2 consecutive newlines
        Assert.DoesNotContain("\n\n\n", result);
    }

    [Fact]
    public void Convert_TableRows_ProduceSeparateLines()
    {
        var html = "<html><body><table><tr><td>A</td></tr><tr><td>B</td></tr></table></body></html>";

        var result = _converter.Convert(html);

        Assert.Contains("A", result);
        Assert.Contains("B", result);
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length >= 2);
    }
}