using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Tests.Tools;

public class EToolResultImagesTests
{
    // ── Images field ──

    [Fact]
    public void Success_Default_HasEmptyImagesList()
    {
        var result = EToolResult.Success("Tool", "ok");

        Assert.NotNull(result.Images);
        Assert.Empty(result.Images);
    }

    [Fact]
    public void Failure_Default_HasEmptyImagesList()
    {
        var result = EToolResult.Failure("Tool", "err");

        Assert.NotNull(result.Images);
        Assert.Empty(result.Images);
    }

    [Fact]
    public void Success_WithImages_StoresImages()
    {
        var images = new List<ToolImageRef>
        {
            new("data1", "image/png", "source1"),
            new("data2", "image/jpeg", "source2")
        };

        var result = EToolResult.Success("Tool", "ok", images);

        Assert.Equal(2, result.Images.Count);
        Assert.Equal("data1", result.Images[0].Base64Data);
        Assert.Equal("data2", result.Images[1].Base64Data);
    }

    [Fact]
    public void Success_WithImages_AndMetadata_StoresBoth()
    {
        var images = new List<ToolImageRef> { new("d", "image/png", "s") };
        var meta = new Dictionary<string, string> { ["k"] = "v" };

        var result = EToolResult.Success("Tool", "ok", images, meta);

        Assert.Single(result.Images);
        Assert.NotNull(result.Metadata);
        Assert.Equal("v", result.Metadata!["k"]);
    }

    [Fact]
    public void Success_WithEmptyImages_HasEmptyList()
    {
        var result = EToolResult.Success("Tool", "ok", new List<ToolImageRef>());

        Assert.Empty(result.Images);
    }
}