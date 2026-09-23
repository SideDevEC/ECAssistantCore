using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Session;

namespace ECAssistant.Core.Tests.Session;

/// <summary>
/// v14.19 (Emre): sessions run VERBOSE by default — users see tool status,
/// playbook captures, verify lines, policy flow. Silent stays available via
/// the TUI /verbosity toggle.
/// </summary>
public sealed class SessionVerbosityDefaultTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "ECAVerb_" + Guid.NewGuid().ToString("N")[..8]);

    public SessionVerbosityDefaultTests()
    {
        Directory.CreateDirectory(_tempDir);
        // SessionManager validates the model file at construction (>1MB .gguf).
        _modelPath = Path.Combine(_tempDir, "mock.gguf");
        using (var fs = File.Create(_modelPath))
            fs.SetLength(2 * 1024 * 1024); // sparse 2MB file — passes size sanity
    }

    private readonly string _modelPath;

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, true); } catch { }
    }

    [Fact]
    public async Task CreateSession_DefaultsToVerbose()
    {
        var config = JsonSerializer.Deserialize<AppConfig>(
            """{ "llm_provider": { "mode": "local", "model_id": "mock" } }""")!;
        var manager = new SessionManager(config, _modelPath, _tempDir);
        try
        {
            var session = manager.CreateSession("verbosity-test");
            Assert.Equal(SessionVerbosity.Verbose, session.Verbosity);
        }
        finally
        {
            if (manager.Main != null) await manager.Main.DisposeAsync();
            // SessionManager is IAsyncDisposable — dispose it too, otherwise the
            // idle watchdog Timer and the OpenAIClient HTTP infra leak past the test.
            await manager.DisposeAsync();
        }
    }
}
