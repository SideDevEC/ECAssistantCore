using ECAssistant.Core.Tools.Mcp;

namespace ECAssistant.Core.Tests.Tools;

public class McpToolAdapterTests
{
    // ── Constructor ──

    [Fact]
    public void Constructor_SetsNameAndDescription()
    {
        var descriptor = new McpToolDescriptor("search", "Search the web", "{}");
        var client = new FakeMcpClient("test-server");
        var adapter = new McpToolAdapter(client, descriptor);

        Assert.Equal("search", adapter.Name);
        Assert.Equal("Search the web", adapter.Description);
    }

    [Fact]
    public void Constructor_NullClient_Throws()
    {
        var descriptor = new McpToolDescriptor("t", "d", "{}");

        Assert.Throws<ArgumentNullException>(() => new McpToolAdapter(null!, descriptor));
    }

    [Fact]
    public void Constructor_NullDescriptor_Throws()
    {
        var client = new FakeMcpClient("srv");

        Assert.Throws<ArgumentNullException>(() => new McpToolAdapter(client, null!));
    }

    // ── GetParameterSchema ──

    [Fact]
    public void GetParameterSchema_ReturnsDescriptorSchema()
    {
        var schema = """{"type":"object","properties":{"query":{"type":"string"}}}""";
        var descriptor = new McpToolDescriptor("search", "Search", schema);
        var client = new FakeMcpClient("srv");
        var adapter = new McpToolAdapter(client, descriptor);

        Assert.Equal(schema, adapter.GetParameterSchema());
    }

    // ── UsageExample ──

    [Fact]
    public void UsageExample_AlwaysEmpty()
    {
        var adapter = new McpToolAdapter(
            new FakeMcpClient("srv"),
            new McpToolDescriptor("t", "d", "{}"));

        Assert.Equal("", adapter.UsageExample);
    }

    // ── GetConfigSection ──

    [Fact]
    public void GetConfigSection_ReturnsEnabled()
    {
        var adapter = new McpToolAdapter(
            new FakeMcpClient("srv"),
            new McpToolDescriptor("t", "d", "{}"));

        var section = adapter.GetConfigSection();

        Assert.NotNull(section);
    }

    // ── RenderForModel ──

    [Fact]
    public void RenderForModel_ShortOutput_Passthrough()
    {
        var adapter = new McpToolAdapter(
            new FakeMcpClient("srv"),
            new McpToolDescriptor("t", "d", "{}"));

        var result = adapter.RenderForModel("short text");

        Assert.Equal("short text", result);
    }

    [Fact]
    public void RenderForModel_LongOutput_Truncates()
    {
        var adapter = new McpToolAdapter(
            new FakeMcpClient("srv"),
            new McpToolDescriptor("t", "d", "{}"));

        var longOutput = new string('x', 5000);
        var result = adapter.RenderForModel(longOutput);

        Assert.True(result.Length < longOutput.Length);
        Assert.Contains("truncated", result);
        Assert.Contains("5000", result);
    }

    [Fact]
    public void RenderForModel_Exactly4000Chars_NoTruncation()
    {
        var adapter = new McpToolAdapter(
            new FakeMcpClient("srv"),
            new McpToolDescriptor("t", "d", "{}"));

        var output = new string('x', 4000);
        var result = adapter.RenderForModel(output);

        Assert.Equal(output, result);
    }

    // ── ExecuteAsync — text result ──

    [Fact]
    public async Task ExecuteAsync_TextContent_ReturnsSuccess()
    {
        var client = new FakeMcpClient("srv");
        client.SetToolResult("search", false, new[]
        {
            new McpContentItem("text", "Search results here", null, null)
        });
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("search", "Search", "{}"));

        var result = await adapter.ExecuteAsync(new Dictionary<string, string?> { ["q"] = "test" });

        Assert.True(result.Succeeded);
        Assert.Equal("Search results here", result.Output);
        Assert.Empty(result.Images);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleTextContent_Concatenated()
    {
        var client = new FakeMcpClient("srv");
        client.SetToolResult("search", false, new[]
        {
            new McpContentItem("text", "Line 1", null, null),
            new McpContentItem("text", "Line 2", null, null)
        });
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("search", "Search", "{}"));

        var result = await adapter.ExecuteAsync(new Dictionary<string, string?>());

        Assert.True(result.Succeeded);
        Assert.Contains("Line 1", result.Output);
        Assert.Contains("Line 2", result.Output);
    }

    // ── ExecuteAsync — error result ──

    [Fact]
    public async Task ExecuteAsync_IsErrorTrue_ReturnsFailure()
    {
        var client = new FakeMcpClient("srv");
        client.SetToolResult("search", true, new[]
        {
            new McpContentItem("text", "Something went wrong", null, null)
        });
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("search", "Search", "{}"));

        var result = await adapter.ExecuteAsync(new Dictionary<string, string?>());

        Assert.False(result.Succeeded);
        Assert.Equal("Something went wrong", result.Error);
    }

    // ── ExecuteAsync — image content ──

    [Fact]
    public async Task ExecuteAsync_ImageContent_ReturnsImage()
    {
        var client = new FakeMcpClient("srv");
        client.SetToolResult("screenshot", false, new[]
        {
            new McpContentItem("text", "Screenshot captured", null, null),
            new McpContentItem("image", null, "base64imagedata", "image/png")
        });
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("screenshot", "Screenshot", "{}"));

        var result = await adapter.ExecuteAsync(new Dictionary<string, string?>());

        Assert.True(result.Succeeded);
        Assert.Single(result.Images);
        Assert.Equal("base64imagedata", result.Images[0].Base64Data);
        Assert.Equal("image/png", result.Images[0].MimeType);
        Assert.Contains("mcp:srv:screenshot", result.Images[0].Source);
    }

    [Fact]
    public async Task ExecuteAsync_OnlyImage_ReturnsSuccessWithImage()
    {
        var client = new FakeMcpClient("srv");
        client.SetToolResult("capture", false, new[]
        {
            new McpContentItem("image", null, "imgdata", "image/jpeg")
        });
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("capture", "Capture", "{}"));

        var result = await adapter.ExecuteAsync(new Dictionary<string, string?>());

        Assert.True(result.Succeeded);
        Assert.Single(result.Images);
        Assert.Equal("", result.Output);
    }

    // ── ExecuteAsync — client throws ──

    [Fact]
    public async Task ExecuteAsync_ClientThrows_ReturnsFailure()
    {
        var client = new FakeMcpClient("srv", throwOnCall: true);
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("t", "d", "{}"));

        var result = await adapter.ExecuteAsync(new Dictionary<string, string?>());

        Assert.False(result.Succeeded);
        Assert.Contains("MCP call failed", result.Error);
    }

    // ── ExecuteAsync — empty content ──

    [Fact]
    public async Task ExecuteAsync_EmptyContent_ReturnsEmptySuccess()
    {
        var client = new FakeMcpClient("srv");
        client.SetToolResult("t", false, Array.Empty<McpContentItem>());
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("t", "d", "{}"));

        var result = await adapter.ExecuteAsync(new Dictionary<string, string?>());

        Assert.True(result.Succeeded);
        Assert.Equal("", result.Output);
    }

    // ── ExecuteAsync — unknown content type ──

    [Fact]
    public async Task ExecuteAsync_UnknownContentType_ExtractsTextIfExists()
    {
        var client = new FakeMcpClient("srv");
        client.SetToolResult("t", false, new[]
        {
            new McpContentItem("resource", "resource text", null, null)
        });
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("t", "d", "{}"));

        var result = await adapter.ExecuteAsync(new Dictionary<string, string?>());

        Assert.True(result.Succeeded);
        Assert.Equal("resource text", result.Output);
    }

    // ── Argument conversion ──

    [Fact]
    public async Task ExecuteAsync_NumberArg_PassedAsNumber()
    {
        var client = new FakeMcpClient("srv");
        client.SetToolResult("t", false, new[] { new McpContentItem("text", "ok", null, null) });
        var schema = """{"type":"object","properties":{"count":{"type":"integer"}}}""";
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("t", "d", schema));

        await adapter.ExecuteAsync(new Dictionary<string, string?> { ["count"] = "42" });

        // Verify the JSON arguments sent to the client
        Assert.Contains("42", client.LastJsonArguments);
        Assert.DoesNotContain("\"42\"", client.LastJsonArguments); // Should be number, not string
    }

    [Fact]
    public async Task ExecuteAsync_BooleanArg_PassedAsBoolean()
    {
        var client = new FakeMcpClient("srv");
        client.SetToolResult("t", false, new[] { new McpContentItem("text", "ok", null, null) });
        var schema = """{"type":"object","properties":{"flag":{"type":"boolean"}}}""";
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("t", "d", schema));

        await adapter.ExecuteAsync(new Dictionary<string, string?> { ["flag"] = "true" });

        Assert.Contains("true", client.LastJsonArguments);
    }

    [Fact]
    public async Task ExecuteAsync_StringArg_PassedAsString()
    {
        var client = new FakeMcpClient("srv");
        client.SetToolResult("t", false, new[] { new McpContentItem("text", "ok", null, null) });
        var schema = """{"type":"object","properties":{"name":{"type":"string"}}}""";
        var adapter = new McpToolAdapter(client, new McpToolDescriptor("t", "d", schema));

        await adapter.ExecuteAsync(new Dictionary<string, string?> { ["name"] = "hello" });

        Assert.Contains("hello", client.LastJsonArguments);
    }

    // ── Fake MCP client for testing ──

    private class FakeMcpClient : IMcpClient
    {
        private readonly bool _throwOnCall;
        private McpToolResult? _result;

        public string ServerName { get; }
        public string LastJsonArguments { get; private set; } = "";

        public FakeMcpClient(string serverName, bool throwOnCall = false)
        {
            ServerName = serverName;
            _throwOnCall = throwOnCall;
        }

        public void SetToolResult(string toolName, bool isError, IReadOnlyList<McpContentItem> content)
        {
            _result = new McpToolResult(isError, content);
        }

        public Task<McpServerInfo> InitializeAsync(CancellationToken ct = default)
            => Task.FromResult(new McpServerInfo(ServerName, "1.0"));

        public Task<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<McpToolDescriptor>>(Array.Empty<McpToolDescriptor>());

        public Task<McpToolResult> CallToolAsync(string name, string jsonArguments, CancellationToken ct = default)
        {
            LastJsonArguments = jsonArguments;
            if (_throwOnCall)
                throw new InvalidOperationException("Connection lost");
            return Task.FromResult(_result ?? new McpToolResult(false, Array.Empty<McpContentItem>()));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}