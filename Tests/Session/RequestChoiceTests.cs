using ECAssistant.Core.Session;
using Xunit;

namespace ECAssistant.Core.Tests.Session;

/// <summary>
/// v14.9 interactive checkpoint (RequestChoice): listener gets the prompt + options,
/// returns a 1-based index; null = no listener / cancelled / invalid — callers proceed
/// autonomously. Default interface method keeps existing listeners compiling.
/// </summary>
public class RequestChoiceTests : IDisposable
{
    private readonly string _dir;

    public RequestChoiceTests()
    {
        _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ECAssistantTests_RequestChoice_" + System.Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_dir, true); } catch { }
    }

    private AgentSession CreateSession()
        => new AgentSession(
            key: "t", sessionId: "t",
            endpoint: "http://127.0.0.1:1",
            clientId: null,
            inferenceParams: new ECAssistant.Core.Interfaces.InferenceRequestParams { MaxTokens = 100 },
            workingDir: _dir,
            inferenceLock: new System.Threading.SemaphoreSlim(1, 1),
            isLocalMode: false);

    private sealed class ChoiceListener : IOutputListener
    {
        public int? Answer { get; set; }
        public string? LastPrompt { get; private set; }
        public System.Collections.Generic.IReadOnlyList<string>? LastOptions { get; private set; }

        public void OnOutput(string text, OutputState state) { }
        public void OnStreamStart() { }
        public void OnStreamStop() { }
        public bool OnRequestApproval(string message) => false;
        public int? OnRequestChoice(string prompt, System.Collections.Generic.IReadOnlyList<string> options)
        {
            LastPrompt = prompt;
            LastOptions = options;
            return Answer;
        }
    }

    [Fact]
    public async Task RequestChoice_ListenerAttached_ReturnsChoiceAndPrompt()
    {
        var session = CreateSession();
        var listener = new ChoiceListener { Answer = 2 };
        session.AddListener(listener);

        var result = await session.RequestChoiceAsync("Pick one", new[] { "A", "B", "C" });

        Assert.Equal(2, result);
        Assert.Equal("Pick one", listener.LastPrompt);
        Assert.Equal(3, listener.LastOptions!.Count);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task RequestChoice_ListenerReturnsNull_ProceedsAutonomously()
    {
        var session = CreateSession();
        session.AddListener(new ChoiceListener { Answer = null });

        var result = await session.RequestChoiceAsync("Pick one", new[] { "A", "B" });

        Assert.Null(result);
        await session.DisposeAsync();
    }
}
