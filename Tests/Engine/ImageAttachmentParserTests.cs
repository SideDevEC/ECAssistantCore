using ECAssistant.Core.Engine;
using Xunit;

namespace ECAssistant.Core.Tests.Engine;

/// <summary>[image:path] attachment extraction — file resolution, mime mapping, error tolerance.</summary>
public class ImageAttachmentParserTests : IDisposable
{
    private readonly string _dir;

    public ImageAttachmentParserTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "imgparser-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "photo.png"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(_dir, "doc.txt"), new byte[] { 4 });
    }

    [Fact]
    public void NoTokens_PromptUnchanged()
    {
        var (prompt, imgs) = ImageAttachmentParser.Extract("just text", _dir);
        Assert.Equal("just text", prompt);
        Assert.Empty(imgs);
    }

    [Fact]
    public void ValidImage_ExtractedAsDataUri()
    {
        var (prompt, imgs) = ImageAttachmentParser.Extract("look at [image:photo.png] please", _dir);
        Assert.Single(imgs);
        Assert.Equal("data:image/png;base64,AQID", imgs[0].DataUri);
        Assert.DoesNotContain("[image:", prompt);
        Assert.Contains("look at", prompt);
        Assert.Contains("please", prompt);
    }

    [Fact]
    public void MissingFile_StrippedWithPromptIntact()
    {
        var (prompt, imgs) = ImageAttachmentParser.Extract("see [image:nope.png]", _dir);
        Assert.Empty(imgs);
        Assert.DoesNotContain("[image:", prompt);
        Assert.Contains("(image(s) omitted", prompt);
    }

    [Fact]
    public void UnsupportedExtension_Rejected()
    {
        var (prompt, imgs) = ImageAttachmentParser.Extract("see [image:doc.txt]", _dir);
        Assert.Empty(imgs);
        Assert.Contains("unsupported type", prompt);
    }

    [Fact]
    public void MultipleImages_AllExtracted()
    {
        File.WriteAllBytes(Path.Combine(_dir, "b.jpg"), new byte[] { 9 });
        var (_, imgs) = ImageAttachmentParser.Extract("[image:photo.png] and [image:b.jpg]", _dir);
        Assert.Equal(2, imgs.Count);
        Assert.Equal("image/png", MimeTypeOf(imgs[0].DataUri));
        Assert.Equal("image/jpeg", MimeTypeOf(imgs[1].DataUri));
    }

    private static string MimeTypeOf(string dataUri)
        => dataUri[(dataUri.IndexOf(':') + 1)..dataUri.IndexOf(';')];

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
