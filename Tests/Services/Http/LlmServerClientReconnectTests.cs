using System.Net;
using System.Net.Sockets;
using ECAssistant.Core.Services.Http;

namespace ECAssistant.Core.Tests.Services.Http;

/// <summary>
/// Minimal fake of the ECAssistantLLM endpoints used by LlmServerClient:
/// POST /eca/clients → registration, DELETE /eca/clients/{id} → disconnect,
/// POST /eca/clients/{id}/heartbeat → heartbeat.
/// Records calls so tests can assert interaction order and counts.
/// </summary>
public sealed class FakeLlmServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    public int RegisterCalls;
    public int DeleteCalls;
    public bool FailRegistration;
    public string? LastDeletedClientId;

    public string Url { get; }

    public FakeLlmServer()
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
            catch { break; }

            try
            {
                var path = ctx.Request.Url?.AbsolutePath ?? "/";
                string body = "{}";

                if (ctx.Request.HttpMethod == "POST" && path == "/eca/clients")
                {
                    Interlocked.Increment(ref RegisterCalls);
                    if (FailRegistration)
                    {
                        await Write(ctx, 500, "{\"error\":\"nope\"}");
                        continue;
                    }
                    body = "{\"client_id\":\"test-client-1\",\"server_version\":\"1.0\"}";
                }
                else if (ctx.Request.HttpMethod == "POST" && path.EndsWith("/heartbeat"))
                {
                    body = "{\"ok\":true,\"sessions_alive\":0}";
                }
                else if (ctx.Request.HttpMethod == "DELETE" && path.StartsWith("/eca/clients/"))
                {
                    Interlocked.Increment(ref DeleteCalls);
                    LastDeletedClientId = path["/eca/clients/".Length..];
                    body = "{\"ok\":true}";
                }

                await Write(ctx, 200, body);
            }
            catch { /* client vanished */ }
        }
    }

    private static async Task Write(HttpListenerContext ctx, int status, string json)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _cts.Dispose();
    }
}

public class LlmServerClientReconnectTests
{
    [Fact]
    public async Task TryReconnect_BackToBack_SecondAttempt_IsCooldownBlocked()
    {
        using var fake = new FakeLlmServer { FailRegistration = true };
        var client = new LlmServerClient(fake.Url);

        // First attempt runs; second within the cooldown window must be a no-op.
        var first = await client.TryReconnectAsync();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var second = await client.TryReconnectAsync();
        sw.Stop();

        Assert.False(first);
        Assert.False(second);
        Assert.True(sw.ElapsedMilliseconds < 1000, "second reconnect should be blocked by cooldown, not executed");
        Assert.Equal(1, fake.RegisterCalls); // only one actual network attempt
    }

    [Fact]
    public async Task Disconnect_SendsDelete_WithClientId_AndClearsState()
    {
        using var fake = new FakeLlmServer();
        var client = new LlmServerClient(fake.Url);

        var ok = await client.ConnectAsync("integration-test", "1.0");
        Assert.True(ok);
        Assert.Equal("test-client-1", client.ClientId);

        var disconnected = await client.DisconnectAsync();
        Assert.True(disconnected);
        Assert.Equal("", client.ClientId);
        Assert.Equal("test-client-1", fake.LastDeletedClientId);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task DisposeAsync_SendsDisconnect_BeforeDisposing()
    {
        using var fake = new FakeLlmServer();
        var client = new LlmServerClient(fake.Url);

        await client.ConnectAsync("dispose-test", "1.0");
        Assert.True(client.IsConnected);

        await client.DisposeAsync();

        // Regression: DisposeAsync previously set _disposed BEFORE DisconnectAsync,
        // so IsConnected was false and the DELETE never reached the server.
        Assert.Equal(1, fake.DeleteCalls);
        Assert.Equal("test-client-1", fake.LastDeletedClientId);
    }

    [Fact]
    public async Task Heartbeat_Success_ResetsFailureCounter()
    {
        using var fake = new FakeLlmServer();
        var client = new LlmServerClient(fake.Url);
        await client.ConnectAsync("hb-test", "1.0");

        var ok = await client.HeartbeatAsync(activeSessions: 2);
        Assert.True(ok);
        Assert.Equal(0, client.ConsecutiveFailures);
    }
}
