using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Setup;
using ECAssistant.Core.Session;

namespace ECAssistant.Core.Tests.Setup;

/// <summary>
/// "Every parameter from the config" (Emre, 2026-09-21): new config keys must
/// parse, and the previously-hardcoded values must flow from appsettings.json —
/// engine context size (llm.context_size) and orchestrator turn tuning
/// (interface.max_turns / turns_per_subtask / subtask_turn_buffer /
/// context_management.compact_threshold_percent).
/// </summary>
public sealed class ConfigDrivenParamsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-cfgparams").FullName;

    [Fact]
    public void NewConfigKeys_HaveSaneDefaults()
    {
        var iface = new InterfaceConfig();
        Assert.Equal(2, iface.TurnsPerSubtask);
        Assert.Equal(2, iface.SubtaskTurnBuffer);

        var ctx = new ContextManagementConfig();
        Assert.Equal(80, ctx.CompactThresholdPercent);
    }

    [Fact]
    public void ConfigKeys_ParseFromJson()
    {
        var json = """
        {
          "interface": { "max_turns": 25, "turns_per_subtask": 4, "subtask_turn_buffer": 5 },
          "context_management": { "compact_threshold_percent": 60 },
          "llm": { "context_size": 32768 }
        }
        """;
        var config = JsonSerializer.Deserialize<AppConfig>(json);

        Assert.NotNull(config);
        Assert.Equal(25, config!.Interface.MaxTurns);
        Assert.Equal(4, config.Interface.TurnsPerSubtask);
        Assert.Equal(5, config.Interface.SubtaskTurnBuffer);
        Assert.Equal(60, config.ContextManagement.CompactThresholdPercent);
        Assert.Equal(32768u, config.Llm.ContextSize);
    }

    [Fact]
    public void FirstRunOrchestrator_IsLocalEmbeddingsRequested_Modes()
    {
        // Local mode (explicit) → server binary required.
        WriteAppsettings("local", true, null);
        Assert.True(FirstRunOrchestrator.IsLocalEmbeddingsRequested(Path.Combine(_dir, "appsettings.json")));

        // Remote embeddings with endpoint → no local server needed.
        WriteAppsettings("remote", true, "https://openrouter.ai/api/v1");
        Assert.False(FirstRunOrchestrator.IsLocalEmbeddingsRequested(Path.Combine(_dir, "appsettings.json")));

        // Disabled → not requested regardless of mode.
        WriteAppsettings("local", false, null);
        Assert.False(FirstRunOrchestrator.IsLocalEmbeddingsRequested(Path.Combine(_dir, "appsettings.json")));
    }

    [Fact]
    public void IsLocalEmbeddingsRequested_MissingFile_ReturnsFalse()
    {
        Assert.False(FirstRunOrchestrator.IsLocalEmbeddingsRequested(Path.Combine(_dir, "nope.json")));
    }

    [Fact]
    public async Task AgentSession_EngineContextWindow_ComesFromConfig()
    {
        // Previously: engine defaulted to 8192 and llm.context_size was silently
        // ignored. The context window must now follow the config.
        var json = """{"llm": {"context_size": 16384}}""";
        var config = JsonSerializer.Deserialize<AppConfig>(json);
        Assert.NotNull(config);

        var session = new AgentSession(
            key: "t", sessionId: "t",
            endpoint: "http://127.0.0.1:1",
            clientId: null,
            inferenceParams: new InferenceRequestParams { MaxTokens = 100 },
            workingDir: _dir,
            inferenceLock: new SemaphoreSlim(1, 1),
            config: config,
            isLocalMode: false);

        Assert.Equal(16384u, session.MaxTokens);
        await session.DisposeAsync();
    }

    private void WriteAppsettings(string mode, bool enabled, string? endpoint)
    {
        var enabledStr = enabled ? "true" : "false";
        var embeddingJson = endpoint == null
            ? "{\"embedding\": {\"enabled\": " + enabledStr + ", \"mode\": \"" + mode + "\"}}"
            : "{\"embedding\": {\"enabled\": " + enabledStr + ", \"mode\": \"" + mode + "\", \"endpoint\": \"" + endpoint + "\"}}";
        File.WriteAllText(Path.Combine(_dir, "appsettings.json"), embeddingJson);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}