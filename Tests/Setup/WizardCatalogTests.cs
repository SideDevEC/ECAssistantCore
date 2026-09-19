using ECAssistant.Core.Setup;
using Xunit;

namespace ECAssistant.Core.Tests.Setup;

/// <summary>
/// Wizard rework units: remote catalog fetch fallback, local model discovery.
/// </summary>
public sealed class WizardCatalogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wizardcat").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task CatalogFetcher_UnreachableEndpoint_ReturnsNull()
    {
        // Invalid host — fetch must fail fast and return null (embedded fallback kicks in)
        using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:9/") };
        var doc = await new CatalogFetcher(http).TryFetchAsync();

        Assert.Null(doc);
    }

    [Fact]
    public async Task CatalogFetcher_InvalidPayload_ReturnsNull()
    {
        // Local HTTP server that answers 200 with non-catalog JSON → validation fails
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                using var s = client.GetStream();
                var body = "{\"not\":\"a catalog\"}";
                var bytes = System.Text.Encoding.UTF8.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\n" +
                    $"Content-Length: {System.Text.Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                await s.WriteAsync(bytes);
                client.Close();
            }
        });

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
        var doc = await new CatalogFetcher(http).TryFetchAsync();

        Assert.Null(doc);
    }

    [Fact]
    public void BuildLocalModelEntry_CreatesSelectableEntry_WithSiblingMmproj()
    {
        File.WriteAllText(Path.Combine(_dir, "my-model.gguf"), "x");
        File.WriteAllText(Path.Combine(_dir, "mmproj-my-model.gguf"), "x");
        var installer = CreateInstaller();

        var entry = installer.BuildLocalModelEntry("my-model.gguf");

        Assert.Equal("local-my-model", entry.Id);
        Assert.Equal(CatalogModelCategory.Chat, entry.Category);
        Assert.Equal("mmproj-my-model.gguf", entry.MmprojFile); // sibling detected → vision
        Assert.Equal("my-model.gguf", entry.Files.Single().Filename);
    }

    [Fact]
    public void BuildLocalModelEntry_NoSibling_NoVision()
    {
        File.WriteAllText(Path.Combine(_dir, "lonely.gguf"), "x");
        var installer = CreateInstaller();

        var entry = installer.BuildLocalModelEntry("lonely.gguf");

        Assert.Null(entry.MmprojFile);
    }

    private ModelInstallerService CreateInstaller()
    {
        Directory.CreateDirectory(_dir);
        return new ModelInstallerService(
            new HttpClient(),
            _dir,
            Path.Combine(_dir, "llm-server.json"),
            appsettingsPath: null);
    }
}
