using ECAssistant.Core.Setup;

namespace ECAssistant.Core.Tests.Setup;

public class VectorMemorySetupWriterTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public VectorMemorySetupWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "eca-vmw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "appsettings.json");
    }

    [Fact]
    public void SetEnabled_OnMissingFile_CreatesSection()
    {
        new VectorMemorySetupWriter(_path).SetEnabled(false);

        var json = File.ReadAllText(_path);
        Assert.Contains("\"enabled\": false", json);
        Assert.Contains("vector_memory", json);
    }

    [Fact]
    public void SetEnabled_PreservesOtherFields_AndFlipsValue()
    {
        File.WriteAllText(_path,
            """{"root_path":"/tmp/eca","llm":{"model_path":"x.gguf"},"vector_memory":{"enabled":true,"directory":"vecmem"}}""");

        new VectorMemorySetupWriter(_path).SetEnabled(false);

        var json = File.ReadAllText(_path);
        Assert.Contains("/tmp/eca", json);
        Assert.Contains("x.gguf", json);
        Assert.Contains("vecmem", json);            // sibling field untouched
        Assert.Contains("\"enabled\": false", json); // flipped
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
