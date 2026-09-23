using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Tests.Tools;

public class ToolImageRefTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var img = new ToolImageRef("base64data", "image/png", "mcp:server:tool");

        Assert.Equal("base64data", img.Base64Data);
        Assert.Equal("image/png", img.MimeType);
        Assert.Equal("mcp:server:tool", img.Source);
    }

    [Fact]
    public void ToDataUri_ReturnsCorrectFormat()
    {
        var img = new ToolImageRef("abc123", "image/jpeg", "test");

        var uri = img.ToDataUri();

        Assert.Equal("data:image/jpeg;base64,abc123", uri);
    }

    [Fact]
    public void ToDataUri_Png_ReturnsPngMime()
    {
        var img = new ToolImageRef("data", "image/png", "test");

        Assert.Equal("data:image/png;base64,data", img.ToDataUri());
    }
}