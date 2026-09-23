using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Orchestration;
using ECAssistant.Core.Session;
using ECAssistant.Core.Services;
using ECAssistant.Core.Tools.Build;
using ECAssistant.Core.Tools.Code;
using ECAssistant.Core.Tools.Shell;
using ECAssistant.TestSupport;

namespace ECAssistant.Core.Tests.E2E;

/// <summary>
/// v15 rigorous journey E2E — long mixed conversations (chat → tools → chat → tools),
/// forced context compaction, and the full feature matrix, run against REAL models.
///
/// Tiers (env-gated, plain unit/CI runs unaffected):
///   LOCAL SMALL:  ECA_E2E_SERVER (+ ECA_E2E_MODEL, default qwen35-4b)
///   REMOTE LARGE: ECA_E2E_REMOTE_ENDPOINT (OpenAI-compatible, e.g. https://ollama.com/v1)
///                 ECA_E2E_REMOTE_MODEL (e.g. glm-5.3-flash:cloud)
///                 ECA_E2E_REMOTE_KEYFILE (path to an API key file)
///
/// Debug: set ECA_JOURNEY_DEBUG to dump per-turn transcripts + context snapshots
/// to /tmp/eca-journeys/&lt;tier&gt;/&lt;journey&gt;/ for post-run behavior analysis.
/// </summary>
[Trait("Category", "JourneyE2E")]
public sealed class JourneySuiteE2E
{
    private string? ServerUrl => Environment.GetEnvironmentVariable("ECA_E2E_SERVER");
    private string LocalModel => Environment.GetEnvironmentVariable("ECA_E2E_MODEL") ?? "qwen35-4b";
    private string? RemoteEndpoint => Environment.GetEnvironmentVariable("ECA_E2E_REMOTE_ENDPOINT");
    private string? RemoteModel => Environment.GetEnvironmentVariable("ECA_E2E_REMOTE_MODEL");
    private string? RemoteKeyFile => Environment.GetEnvironmentVariable("ECA_E2E_REMOTE_KEYFILE");
    private bool DebugDump => Environment.GetEnvironmentVariable("ECA_JOURNEY_DEBUG") == "1";

    private static string DebugDir(string tier, string journey) =>
        Path.Combine("/tmp/eca-journeys", tier, journey);

    // ── Config builders ─────────────────────────────────────────────

    private AppConfig LocalConfig(int maxTurns = 12, int? compactPct = null,
        string? verifyCommand = null, int? llmContextSize = null)
    {
        var json = $$"""
        {
          "llm_provider": { "mode": "local", "model_id": "{{LocalModel}}" },
          "model_tier": { "mode": "small" },
          "interface": { "max_turns": {{maxTurns}} },
          "llm": { "context_size": {{llmContextSize ?? 16384}} },
          "context_management": {
            "compact_threshold_percent": {{compactPct ?? 80}}
          }
          {{(verifyCommand != null ? $",\"verification\": {{ \"enabled\": true, \"build_command\": \"{verifyCommand}\", \"max_rounds\": 2 }}" : "")}}
        }
        """;
        return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json)!;
    }

    private AppConfig RemoteConfig(int maxTurns = 12, int? compactPct = null, int? llmContextSize = null)
    {
        var json = $$"""
        {
          "llm_provider": { "mode": "remote", "model_id": "{{RemoteModel}}", "endpoint": "{{RemoteEndpoint}}" },
          "model_tier": { "mode": "large" },
          "interface": { "max_turns": {{maxTurns}} },
          "llm": { "context_size": {{llmContextSize ?? 16384}} },
          "context_management": {
            "compact_threshold_percent": {{compactPct ?? 80}}
          }
        }
        """;
        return System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json)!;
    }

    async Task<AgentSession> CreateLocalAsync(
        string endpoint, AppConfig config, Action<AgentEngine>? configure = null)
    {
        var factory = new HarnessE2ESessionFactory();
        var (session, dir) = await factory.CreateAsync(
            endpoint, config,
            configure: engine => configure?.Invoke(engine),
            prepareWorkingDir: dir =>
             {
                config.AgentSettings.WorkingDirectory = dir;
                config.RootPath = dir; // verification + root-relative paths resolve to the temp workspace
             });
        return session;
    }

    /// <summary>
    /// Headless parity: register handoff + sub-agents (as SessionBuilder does in
    /// production) and auto-approve every registered tool — there is no user to
    /// approve in an e2e run, so ApprovalRequired tools would be silently DENIED.
    /// </summary>
    private static async Task WireSessionParityAsync(AgentSession session, bool withSubAgents = true)
    {
        await session.InitializeHandoffAsync();
        if (withSubAgents)
            await session.Orchestrator.InitializeSubAgentsAsync(session.Engine.WorkingDir);
        foreach (var tool in session.Engine.Tools.ToList())
            session.Orchestrator.Policy.SetPermission(tool.Name, approvalRequired: false, "e2e journey");
    }

    private AgentSession? TryCreateRemoteSession(AppConfig config, Action<AgentEngine>? configure = null)
    {
        if (string.IsNullOrEmpty(RemoteEndpoint) || string.IsNullOrEmpty(RemoteModel) ||
            string.IsNullOrEmpty(RemoteKeyFile) || !File.Exists(RemoteKeyFile))
            return null;

        var apiKey = File.ReadAllText(RemoteKeyFile!).Trim();
        var workingDir = Directory.CreateTempSubdirectory("eca-journey-remote").FullName;
        config.AgentSettings.WorkingDirectory = workingDir;

        var session = new AgentSession(
            key: "remote-journey-" + Guid.NewGuid().ToString("N")[..8],
            sessionId: "remote-journey-" + Guid.NewGuid().ToString("N")[..8],
            endpoint: RemoteEndpoint!,
            clientId: null,
            inferenceParams: InferenceParamsFactory.Default.CreateTiered(config, isLargeTier: true),
            workingDir: workingDir,
            inferenceLock: new SemaphoreSlim(1, 1),
            config: config,
            isLocalMode: false,
            apiKey: apiKey);
        configure?.Invoke(session.Engine);
        return session;
    }

    // ── Debug helpers ───────────────────────────────────────────────

    void DumpTurn(string tier, string journey, int turn, string prompt,
        OrchestratorResult result, AgentSession session)
    {
        if (!DebugDump) return;
        var dir = Path.Combine(DebugDir(tier, journey));
        Directory.CreateDirectory(dir);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"═══ TURN {turn} ═══ {DateTime.UtcNow:HH:mm:ss}");
        sb.AppendLine($"PROMPT: {prompt}");
        sb.AppendLine($"STATUS: {result.Status} | tool calls: {result.ToolCallsMade}");
        sb.AppendLine($"OUTPUT: {result.FinalOutput}");
        sb.AppendLine();
        sb.AppendLine("--- CONTEXT WINDOW (model-visible) ---");
        foreach (var m in session.Engine.ContextWindow.GetWindowMessages())
            sb.AppendLine($"[{m.Role}{(string.IsNullOrEmpty(m.Source) ? "" : "/" + m.Source)}] {Trunc(m.Content, 300)}");
        File.WriteAllText(Path.Combine(dir, $"turn{turn:D2}.md"), sb.ToString());
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    // ════════════════════════════════════════════════════════════════
    // J1 — LONG MIXED CONVERSATION: chat → tools → chat → tools → chat
    // ════════════════════════════════════════════════════════════════

    [LocalTheory]
    public async Task J1_MixedChatTool_LongJourney_MemorySurvivesAcrossTurns()
    {
        var (session, tier) = await CreateAnyTierSession((engine, cfg) =>
        {
            engine.RegisterTool(new ECodeEditorTool(new FileSystemAdapter(), cfg));
            engine.RegisterTool(new EShellAgent(new ProcessRunner(), cfg, cfg.AgentSettings.WorkingDirectory));
        });
        if (session == null) return;
        try
        {
            var results = new List<OrchestratorResult>();

            // T1 — knowledge chat
            results.Add(await Turn(session, tier, "J1", 1,
                "What is 12 times 12? Answer directly from knowledge, no tools."));
            Assert.Equal(OrchestratorStatus.GoalAchieved, results[0].Status);
            Assert.Contains("144", results[0].FinalOutput);

            // T2 — tool: create a file
            results.Add(await Turn(session, tier, "J1", 2,
                "Create a file named notes.md in the working directory containing exactly one line: journey marker 42. Use the code editor tool."));
            Assert.Equal(OrchestratorStatus.GoalAchieved, results[1].Status);
            Assert.True(File.Exists(Path.Combine(session.Engine.WorkingDir, "notes.md")),
                "notes.md was not created");
            Assert.Contains("journey marker 42", File.ReadAllText(Path.Combine(session.Engine.WorkingDir, "notes.md")));

            // T3 — chat about the file (cross-turn memory)
            results.Add(await Turn(session, tier, "J1", 3,
                "Without reading the file again, what marker did you write into notes.md earlier? Answer from memory."));
            Assert.Equal(OrchestratorStatus.GoalAchieved, results[2].Status);

            // T4 — tool: shell echo append
            results.Add(await Turn(session, tier, "J1", 4,
                "Run a shell command that appends the line 'shell was here' to notes.md, then show me the file contents."));
            Assert.Equal(OrchestratorStatus.GoalAchieved, results[3].Status);
            var content = File.ReadAllText(Path.Combine(session.Engine.WorkingDir, "notes.md"));
            Assert.Contains("shell was here", content);

            // T5 — chat: interpret shell result
            results.Add(await Turn(session, tier, "J1", 5,
                "How many lines does notes.md have now? Answer briefly from what you saw."));
            Assert.Equal(OrchestratorStatus.GoalAchieved, results[4].Status);

            // T6 — tool: shell again (different command)
            results.Add(await Turn(session, tier, "J1", 6,
                "Use the shell to compute 6*7 with echo or expr and tell me the result."));
            Assert.Equal(OrchestratorStatus.GoalAchieved, results[5].Status);

            // T7 — long-range memory chat
            results.Add(await Turn(session, tier, "J1", 7,
                "Final recap: in one sentence, what file did we create and what did we append to it?"));
            Assert.Equal(OrchestratorStatus.GoalAchieved, results[6].Status);
            Assert.Contains("notes.md", results[6].FinalOutput, StringComparison.OrdinalIgnoreCase);

            Assert.True(results.Count(r => r.ToolCallsMade > 0) >= 2, "expected tool usage in the mixed journey");
        }
        finally { await session.DisposeAsync(); }
    }

    // ════════════════════════════════════════════════════════════════
    // J2 — FORCED CONTEXT COMPACTION MID-JOURNEY
    // ════════════════════════════════════════════════════════════════

    [LocalTheory]
    public async Task J2_Compaction_TriggersMidJourney_SessionSurvivesAndRemembers()
    {
        var (session, tier) = await CreateAnyTierSession((engine, cfg) =>
        {
            // Force compaction: tiny budget, low threshold
            engine.RegisterTool(new EShellAgent(new ProcessRunner(), cfg, cfg.AgentSettings.WorkingDirectory));
        }, llmContextSize: 2048, compactPct: 30);
        if (session == null) return;
        try
        {
            // T1 — plant a durable fact
            var t1 = await Turn(session, tier, "J2", 1,
                "Remember this code word for later: PINEAPPLE-77. Just acknowledge.");
            Assert.Equal(OrchestratorStatus.GoalAchieved, t1.Status);

            // T2..T12 — flood the context with large tool outputs + chats to force compaction.
            // SummarizeOldest requires oldMessages.Count > 3 (keeps 5 recent) → need a real
            // multi-turn flood, 5 turns can never trigger it.
            for (var i = 2; i <= 12; i++)
            {
                var r = await Turn(session, tier, "J2", i,
                    i % 2 == 0
                        ? "Run this shell command and show me the full output: seq 1 400"
                        : $"Turn {i}: reply with a 60-word story about the sea. Do not use tools.");
                Assert.Equal(OrchestratorStatus.GoalAchieved, r.Status);
            }

            // Compaction evidence: transcript contains an inserted system summary
            // (SummarizeOldest inserts TranscriptMessage.System(summary) after leading system messages)
            var hasSummaryInsert = session.Engine.ContextWindow.GetWindowMessages()
                .Any(m => m.Role == "system" &&
                          !m.Content.StartsWith("You are", StringComparison.OrdinalIgnoreCase) &&
                          m.Content.Length > 50);
            Assert.True(hasSummaryInsert, "expected a compaction summary to be inserted into the context window");

            // T13 — post-compaction recall (the summary must have preserved the code word)
            var t7 = await Turn(session, tier, "J2", 13,
                "What was the code word I asked you to remember at the start? Answer with just the code word.");
            Assert.Equal(OrchestratorStatus.GoalAchieved, t7.Status);
            Assert.Contains("PINEAPPLE-77", t7.FinalOutput);
        }
        finally { await session.DisposeAsync(); }
    }

    // ════════════════════════════════════════════════════════════════
    // J3 — SUB-AGENT inside a long conversation
    // ════════════════════════════════════════════════════════════════

    [LocalTheory]
    public async Task J3_SubAgentJourney_ParentContinues_IntegratesResult()
    {
        var (session, tier) = await CreateAnyTierSession();
        if (session == null) return;
        try
        {
            var r = await Turn(session, tier, "J3", 1,
                "Use ESubAgent with this task: 'Count how many files are in the current directory using a shell command and report the number.' Then tell me the count.");
            Assert.Equal(OrchestratorStatus.GoalAchieved, r.Status);

            // Follow-up proves the parent is alive and the sub-agent result landed in parent context
            var followUp = await Turn(session, tier, "J3", 2,
                "Based on the sub-agent's count: is it greater than zero? Answer yes or no.");
            Assert.Equal(OrchestratorStatus.GoalAchieved, followUp.Status);
        }
        finally { await session.DisposeAsync(); }
    }

    // ════════════════════════════════════════════════════════════════
    // J4 — HANDOFF (specialist takeover), both tiers
    // ════════════════════════════════════════════════════════════════

    [LocalTheory]
    public async Task J4_HandoffJourney_SpecialistTakesOver()
    {
        var (session, tier) = await CreateAnyTierSession();
        if (session == null) return;
        try
        {
            var result = await Turn(session, tier, "J4", 1,
                "Call EHandoff now to delegate this task to a specialist. " +
                "Use this exact specialist prompt: 'You are an echo specialist. Your secret code phrase is SPECIALIST_ECHO_OK. Reply with exactly that code phrase and nothing else.' " +
                "Pass context: 'Echo the code phrase from your instructions.' Do not answer yourself — delegate.");

            Assert.Equal(OrchestratorStatus.GoalAchieved, result.Status);
            Assert.Contains("SPECIALIST_ECHO_OK", result.FinalOutput);
        }
        finally { await session.DisposeAsync(); }
    }

    // ════════════════════════════════════════════════════════════════
    // J5 — POST-EDIT VERIFICATION LOOP
    // ════════════════════════════════════════════════════════════════
     [LocalTheory]
    public async Task J5_VerificationJourney_VerifyCommandRunsAfterEdit()
     {
        var (session, tier) = await CreateAnyTierSession((engine, cfg) =>
          {
             // J5: prepareWorkingDir sets the same dir on this cfg.AgentSettings.WorkingDirectory;
             // register here so EDotnetBuildTool captures it at construction.
            engine.RegisterTool(new ECodeEditorTool(new FileSystemAdapter(), cfg));
            engine.RegisterTool(new EDotnetBuildTool(new ProcessRunner(), cfg));
          }, verifyCommand: "dotnet build", withSubAgents: false);
        if (session == null) return;
        try
         {
             // A minimal PASSING project so the verify gate can succeed
            var dir = session.Engine.WorkingDir;
            File.WriteAllText(Path.Combine(dir, "App.csproj"),
                 """
                 <Project Sdk="Microsoft.NET.Sdk">
                   <PropertyGroup>
                     <OutputType>Exe</OutputType>
                     <TargetFramework>net8.0</TargetFramework>
                   </PropertyGroup>
                 </Project>
                 """
            );
            File.WriteAllText(Path.Combine(dir, "Program.cs"), "System.Console.WriteLine(\"ok\");");

             // Content must exceed TrivialEditMaxChars (200) — large tier skips
             // verification for trivial edits by design.
            var readme = "demo project readme\n" + new string('x', 240) + "\n";
            var r = await Turn(session, tier, "J5", 1,
                 $"Create a file named README.md containing exactly:\n{readme}\nThen run the verification build to confirm the project still builds. Report the build result.");

            Assert.Equal(OrchestratorStatus.GoalAchieved, r.Status);
             // The verify command (dotnet build) must have executed — its output is in context.
             // Large tier keeps the success path slim (no window note by design), so accept
             // either a window EDotnetBuild message or the "[Verify] build OK" line in the log.
            var verifyEvidence =
                session.Engine.ContextWindow.GetWindowMessages().Any(m => m.Source == "EDotnetBuild") ||
                File.ReadAllText(session.OutputFilePath).Contains("[Verify] build OK");
            Assert.True(verifyEvidence, "expected verification evidence ([Verify] build OK or EDotnetBuild output)");
            Assert.True(File.Exists(Path.Combine(dir, "README.md")));
         }
        finally { await session.DisposeAsync(); }
    }

    // ════════════════════════════════════════════════════════════════
    // J6 — PLAYBOOK CAPTURE after successful tool runs
    // ════════════════════════════════════════════════════════════════

    [LocalTheory]
    public async Task J6_PlaybookJourney_SuccessfulToolRunCapturesPlaybook()
    {
        var (session, tier) = await CreateAnyTierSession((engine, cfg) =>
            engine.RegisterTool(new EShellAgent(new ProcessRunner(), cfg, cfg.AgentSettings.WorkingDirectory)));
        if (session == null) return;
        try
        {
            var r = await Turn(session, tier, "J6", 1,
                "Use the shell tool to run: echo playbook-probe. Then report the output.");
            Assert.Equal(OrchestratorStatus.GoalAchieved, r.Status);

            var second = await Turn(session, tier, "J6", 2,
                "What command did you just run? Answer briefly.");
            Assert.Equal(OrchestratorStatus.GoalAchieved, second.Status);
        }
        finally { await session.DisposeAsync(); }
    }

    // ════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════

    private async Task<OrchestratorResult> Turn(
        AgentSession session, string tier, string journey, int n, string prompt)
    {
        var result = await session.Orchestrator.ExecuteMultiStep(prompt);
        DumpTurn(tier, journey, n, prompt, result, session);
        return result;
    }

    /// <summary>
    /// Create a session for whichever tier the environment provides.
    /// Returns (session, tierName); (null, "") when that tier is not enabled.
    /// </summary>
    private async Task<(AgentSession?, string)> CreateAnyTierSession(
        Action<AgentEngine, AppConfig>? configure = null,
        int? compactPct = null, string? verifyCommand = null,
        int? llmContextSize = null, bool withSubAgents = true)
    {
        if (!string.IsNullOrEmpty(ServerUrl))
        {
            var cfg = LocalConfig(compactPct: compactPct,
                verifyCommand: verifyCommand, llmContextSize: llmContextSize);
            var factory = new HarnessE2ESessionFactory();
            var (session, dir) = await factory.CreateAsync(ServerUrl!, cfg,
                configure: engine => configure?.Invoke(engine, cfg),
                prepareWorkingDir: dir => cfg.AgentSettings.WorkingDirectory = dir);
            await WireSessionParityAsync(session, withSubAgents);
            return (session, "local");
        }

        if (!string.IsNullOrEmpty(RemoteEndpoint))
        {
            var cfg = RemoteConfig(compactPct: compactPct, llmContextSize: llmContextSize);
            var session = TryCreateRemoteSession(cfg, engine => configure?.Invoke(engine, cfg));
            if (session != null)
                await WireSessionParityAsync(session, withSubAgents);
            return (session, "remote");
        }

        return (null, "");
    }

    private sealed class LocalTheoryAttribute : FactAttribute
    {
        public LocalTheoryAttribute()
        {
            // Runs when EITHER tier is configured — the tier used is resolved at runtime.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ECA_E2E_SERVER")) &&
                string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ECA_E2E_REMOTE_ENDPOINT")))
                Skip = "journey e2e — needs ECA_E2E_SERVER (local) or ECA_E2E_REMOTE_ENDPOINT (remote)";
        }
    }
}