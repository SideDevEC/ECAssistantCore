using System.Text;
using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Tools.EVision;
using ECAssistant.Core.Vision;
using Moq;

namespace ECAssistant.Core.Tests.Tools;

public class EVisionStructureToolTests : IDisposable
{
    private readonly Mock<IInferenceEngine> _engine = new();
    private readonly Mock<IPdfPageRenderer> _pdfRenderer = new();
    private readonly EAgentConfig _config = new();
    private readonly string _tempDir;

    private const string ValidVisionJson = """
        {
          "schemaVersion": "1.0",
          "source": { "kind": "screenshot", "page": 1, "width": 400, "height": 300 },
          "elements": [
            { "id": "e1", "type": "label", "text": "Name", "bbox": { "x": 0, "y": 0, "width": 40, "height": 10 }, "confidence": 1, "associatedWith": ["e2"] },
            { "id": "e2", "type": "input", "text": "", "bbox": { "x": 50, "y": 0, "width": 100, "height": 10 }, "confidence": 1, "associatedWith": ["e1"] }
          ],
          "groups": [ { "id": "g1", "role": "form", "memberIds": ["e1", "e2"] } ],
          "warnings": []
        }
        """;

    public EVisionStructureToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eca-visiontool-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _engine.Setup(e => e.Endpoint).Returns("http://test");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private EVisionStructureTool CreateTool() =>
        new(_engine.Object, _pdfRenderer.Object, _config);

    private string WriteImageFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    // ── Metadata ──

    [Fact]
    public void Name_IsEVisionStructure()
    {
        Assert.Equal("EVisionStructure", CreateTool().Name);
    }

    [Fact]
    public void ParameterSchema_IncludesPath()
    {
        Assert.Contains("\"path\"", CreateTool().GetParameterSchema());
    }

    // ── Argument validation ──

    [Fact]
    public async Task ExecuteAsync_MissingPath_Fails()
    {
        var result = await CreateTool().ExecuteAsync(new Dictionary<string, string?>());

        Assert.False(result.Succeeded);
        Assert.Contains("Missing required argument: path", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_FileNotFound_Fails()
    {
        var result = await CreateTool().ExecuteAsync(new Dictionary<string, string?> { ["path"] = "/nope/missing.png" });

        Assert.False(result.Succeeded);
        Assert.Contains("File not found", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedExtension_Fails()
    {
        var path = Path.Combine(_tempDir, "doc.xyz");
        File.WriteAllText(path, "x");

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, string?> { ["path"] = path });

        Assert.False(result.Succeeded);
        Assert.Contains("Unsupported file type", result.Error);
    }

    // ── Image path ──

    [Fact]
    public async Task ExecuteAsync_ImageFile_SendsDataUriAndReturnsStructuredJson()
    {
        var path = WriteImageFile("ui.png");
        _engine.Setup(e => e.GenerateAsync(It.IsAny<string>(), It.IsAny<InferenceRequestParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidVisionJson);
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string?> { ["path"] = path });

        Assert.True(result.Succeeded, result.Error);
        _engine.Verify(e => e.GenerateAsync(
            It.Is<string>(p => p.Contains("\"screenshot\"")),
            It.Is<InferenceRequestParams>(rp => rp.ImageDataUris.Count == 1 && rp.ImageDataUris[0].StartsWith("data:image/png;base64,")),
            It.IsAny<CancellationToken>()), Times.Once);

        using var doc = JsonDocument.Parse(result.Output);
        Assert.Equal("1.0", doc.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(2, doc.RootElement.GetProperty("elements").GetArrayLength());
        Assert.Equal("2", result.Metadata!["elements"]);
    }

    [Fact]
    public async Task ExecuteAsync_InferenceThrows_ReturnsFailure()
    {
        var path = WriteImageFile("ui.png");
        _engine.Setup(e => e.GenerateAsync(It.IsAny<string>(), It.IsAny<InferenceRequestParams>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, string?> { ["path"] = path });

        Assert.False(result.Succeeded);
        Assert.Contains("Vision inference failed", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_MalformedModelOutput_ReturnsFailureWithDetails()
    {
        var path = WriteImageFile("ui.jpg");
        _engine.Setup(e => e.GenerateAsync(It.IsAny<string>(), It.IsAny<InferenceRequestParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("I see a nice UI, but no JSON for you.");

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, string?> { ["path"] = path });

        Assert.False(result.Succeeded);
        Assert.Contains("did not produce a valid structure", result.Error);
    }

    // ── PDF path ──

    [Fact]
    public async Task ExecuteAsync_Pdf_RendersPageAndReportsPdfPageSource()
    {
        var path = WriteImageFile("scan.pdf");
        _pdfRenderer.Setup(r => r.RenderPageToPngAsync(path, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 9, 9 });
        _engine.Setup(e => e.GenerateAsync(It.IsAny<string>(), It.IsAny<InferenceRequestParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidVisionJson);

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, string?> { ["path"] = path });

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("pdf-page", result.Metadata!["source"]);
        _engine.Verify(e => e.GenerateAsync(
            It.Is<string>(p => p.Contains("pdf-page") && p.Contains("source.page to 1")),
            It.IsAny<InferenceRequestParams>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_PdfRendererFails_ReturnsFailure()
    {
        var path = WriteImageFile("scan.pdf");
        _pdfRenderer.Setup(r => r.RenderPageToPngAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, string?> { ["path"] = path });

        Assert.False(result.Succeeded);
        Assert.Contains("Could not render page", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_PdfPageZero_Fails()
    {
        var path = WriteImageFile("scan.pdf");

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, string?> { ["path"] = path, ["page"] = "0" });

        Assert.False(result.Succeeded);
        Assert.Contains("page must be >= 1", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_PdfPageTwo_PassesPageToRenderer()
    {
        var path = WriteImageFile("scan.pdf");
        _pdfRenderer.Setup(r => r.RenderPageToPngAsync(path, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 9 });
        _engine.Setup(e => e.GenerateAsync(It.IsAny<string>(), It.IsAny<InferenceRequestParams>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ValidVisionJson);

        var result = await CreateTool().ExecuteAsync(new Dictionary<string, string?> { ["path"] = path, ["page"] = "2" });

        _pdfRenderer.Verify(r => r.RenderPageToPngAsync(path, 2, It.IsAny<CancellationToken>()), Times.Once);
    }
}
