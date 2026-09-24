using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Services.Http;
using ECAssistant.Core.Transport;

namespace ECAssistant.Core.Tests.Services.Http;

/// <summary>
/// v15: SessionId=null on InferenceRequestParams must be a REAL stateless signal —
/// the engine must not fall back to its default session id (the old `?? _defaultSessionId`
/// behavior leaked decompose/planner/stateless-summary prompts into the main session's
/// KV cache and drove shift-incapable models into the 16k context wall, J2 failure).
/// Routing is private, so tests observe the OUTBOUND request body via a fake endpoint.
/// </summary>
public sealed class StatelessSessionRoutingTests
{
    private sealed class RecordingServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cts = new();
        public readonly List<string> Bodies = new();
        public string Url { get; }

        public RecordingServer()
        {
            var port = GetFreePort();
            Url = $"http://localhost:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(() => Loop(_cts.Token));
        }

        private static int GetFreePort()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private async Task Loop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch (Exception) { return; } // disposed
                try
                {
                    using var reader = new StreamReader(ctx.Request.InputStream);
                    var body = await reader.ReadToEndAsync(ct);
                    lock (Bodies) Bodies.Add(body);
                    ctx.Response.ContentType = "application/json";
                    // Structured path parses "decision"; plain path parses choices[0].message.content.
                    var payload = """{"decision":{"thinking":"","answer":"ok","toolcalls":[]},"choices":[{"message":{"role":"assistant","content":"ok"}}]}""";
                    var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
                    await ctx.Response.OutputStream.WriteAsync(bytes, ct);
                    ctx.Response.Close();
                }
                catch { /* client may disconnect */ }
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); _listener.Stop(); } catch { }
            _cts.Dispose();
        }
    }

    private static InferenceRequestParams Params(string? sessionId) => new()
    {
        ModelId = "test-model",
        MaxTokens = 16,
        SessionId = sessionId,
    };

    private static List<string> SessionIdsFrom(RecordingServer server)
    {
        List<string> found = new();
        foreach (var body in server.Bodies)
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("session_id", out var sid))
                found.Add(sid.GetString() ?? "");
            else
                found.Add("(omitted)");
        }
        return found;
    }

    [Fact]
    public async Task NullSessionId_OmitsSessionId_InRequestBody()
    {
        using var server = new RecordingServer();
        var client = new OpenAIClient(server.Url);
        var engine = new HttpStreamingEngine(client, "test-model", "main-session"); // engine HAS a default session
        await engine.GenerateAsync("hello", Params(sessionId: null)); // explicit stateless

        var ids = SessionIdsFrom(server);
        Assert.Single(ids);
        Assert.Equal("(omitted)", ids[0]); // server → stateless executor path
    }

    [Fact]
    public async Task ExplicitSessionId_IsSent_InRequestBody()
    {
        using var server = new RecordingServer();
        var client = new OpenAIClient(server.Url);
        var engine = new HttpStreamingEngine(client, "test-model", "main-session");
        await engine.GenerateAsync("hello", Params(sessionId: "warm-session"));

        var ids = SessionIdsFrom(server);
        Assert.Single(ids);
        Assert.Equal("warm-session", ids[0]);
    }

    [Fact]
    public async Task Structured_NullSessionId_OmitsSessionId()
    {
        using var server = new RecordingServer();
        var client = new OpenAIClient(server.Url);
        var engine = new HttpStreamingEngine(client, "test-model", "main-session");
        await engine.GenerateStructuredAsync("decide", Params(sessionId: null), CancellationToken.None);

        var ids = SessionIdsFrom(server);
        Assert.Single(ids);
        Assert.Equal("(omitted)", ids[0]);
    }

    [Fact]
    public async Task Structured_ExplicitSessionId_IsSent()
    {
        using var server = new RecordingServer();
        var client = new OpenAIClient(server.Url);
        var engine = new HttpStreamingEngine(client, "test-model", "main-session");
        await engine.GenerateStructuredAsync("decide", Params(sessionId: "warm-session"), CancellationToken.None);

        var ids = SessionIdsFrom(server);
        Assert.Single(ids);
        Assert.Equal("warm-session", ids[0]);
    }
}