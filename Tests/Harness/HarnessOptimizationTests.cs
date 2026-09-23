using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Engine;
using ECAssistant.Core.Interfaces;
using ECAssistant.TestSupport;

namespace ECAssistant.Core.Tests.Harness;

/// <summary>
/// Tests for the 2026-09-21 harness optimizations (P1-P6): tool-result truncation
/// limits from config, preplanning toggle, verifier contract, typed tool schemas,
/// staged compaction, and rules-file injection.
/// </summary>
public sealed class HarnessOptimizationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("eca-harness").FullName;

    // ── P1: config-driven tool-output limits ──

    [Fact]
    public void ToolOutputLimits_ParseFromConfig()
    {
        var json = """
        {
          "tool_output_limits": {
            "max_result_chars": 2500,
            "max_result_chars_per_tool": { "ECodeEditor": 9000 },
            "max_stored_outputs": 5
          }
        }
        """;
        var config = JsonSerializer.Deserialize<EAgentConfig>(json);
        Assert.NotNull(config!.ToolOutputLimits);
        Assert.Equal(2500, config.ToolOutputLimits.MaxResultChars);
        Assert.Equal(9000, config.ToolOutputLimits.MaxResultCharsPerTool!["ECodeEditor"]);
        Assert.Equal(5, config.ToolOutputLimits.MaxStoredOutputs);
    }

    [Fact]
    public void ToolOutputLimits_Defaults_MatchPreviousHardcodedValues()
    {
        var limits = new ToolOutputLimitsConfig();
        Assert.Equal(4000, limits.MaxResultChars);
        Assert.Equal(20, limits.MaxStoredOutputs);
        Assert.Null(limits.MaxResultCharsPerTool);
    }

    // ── P2/P3: preplanning toggle + verifier contract (config surface) ──
    // 2026-09-22: preplanning default REVERTED to true (in-loop planning regressed
    // live — model wandered without explicit step lists). See InterfaceConfig.

    // v14.12: Preplanning is now bool? — null = auto (resolved from model tier in
    // Orchestrator.UsePreplanning: large models skip, small models keep preplanning).
    [Fact]
    public void Preplanning_DefaultsNull_AutoResolution()
    {
        var config = JsonSerializer.Deserialize<EAgentConfig>("{}");
        Assert.Null(config!.Interface.Preplanning);
    }

    [Fact]
    public void VerifyCommand_ParsesFromConfig()
    {
        var config = JsonSerializer.Deserialize<EAgentConfig>(
            """{"interface": {"verify_command": "dotnet test --filter Fast"}}""");
        Assert.Equal("dotnet test --filter Fast", config!.Interface.VerifyCommand);
    }

    // ── P4: typed tool schemas ──

    [Fact]
    public void ToolsWithSchemas_ProvideValidJsonSchema()
    {
        foreach (var (typeName, instance) in SchemaProviders())
        {
            var schema = instance.GetParameterSchema();
            if (schema == "") continue; // tools without a schema fall back permissive

            using var doc = JsonDocument.Parse(schema); // throws on malformed
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("type", out var t) && t.GetString() == "object",
                $"{typeName} schema must be an object schema");
            Assert.True(root.TryGetProperty("properties", out _), $"{typeName} schema must declare properties");
        }
    }

    [Fact]
    public void ShellSchema_RequiresCommand()
    {
        var shell = SchemaProviders().First(kv => kv.Item1 == "EShellAgent").Item2;
        using var doc = JsonDocument.Parse(shell.GetParameterSchema());
        var required = doc.RootElement.GetProperty("required");
        Assert.Contains("command", required.EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void ToolSpec_CarriesSchema_FromEngine()
    {
        // BuildToolSpecs must copy GetParameterSchema() into the ToolSpec so the
        // native function-calling path can send real schemas.
        var engine = new MockEngine(workingDir: _dir);
        engine.RegisterTool(new ProbeTestTool());
        var method = typeof(EAgentEngine).GetMethod("BuildToolSpecs",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var specs = (List<ToolSpec>)method.Invoke(engine, null)!;
        var spec = specs.Single(s => s.Name == "ProbeTool");
        Assert.Contains("\"action\"", spec.ParameterSchema);
    }

    // ── P5: staged compaction ──

    [Fact]
    public void TrimStaleToolOutputs_KeepsLastToolOutput_Only()
    {
        var cw = new ContextWindow(64_000);
        cw.AddUserMessage("fix it");
        cw.AddToolOutput(new string('a', 500), "T1");
        cw.AddToolOutput(new string('b', 500), "T2");
        cw.AddToolOutput(new string('c', 500), "T3");

        cw.TrimStaleToolOutputs(keepRecent: 1);

        var msgs = cw.GetWindowMessages();
        var toolMsgs = msgs.Where(m => m.Role == "tool_output").ToList();
        Assert.Equal(3, toolMsgs.Count); // stubs keep positions
        Assert.Contains("trimmed by compaction", toolMsgs[0].Content);
        Assert.Contains("trimmed by compaction", toolMsgs[1].Content);
        Assert.Equal(new string('c', 500), toolMsgs[2].Content); // most recent verbatim
        Assert.Contains(msgs, m => m.Role == "user"); // user turns untouched
    }

    [Fact]
    public void TrimStaleToolOutputs_KeepRecent_StubOlder()
    {
        var cw = new ContextWindow(64_000);
        cw.AddUserMessage("task");
        for (int i = 0; i < 5; i++)
            cw.AddToolOutput(new string((char)('a' + i), 300), $"T{i}");

        cw.TrimStaleToolOutputs(keepRecent: 3);

        var toolMsgs = cw.GetWindowMessages().Where(m => m.Role == "tool_output").ToList();
        Assert.Equal(5, toolMsgs.Count); // stubs, not removals — trace preserved
        Assert.Contains("trimmed by compaction", toolMsgs[0].Content);
        Assert.Contains("trimmed by compaction", toolMsgs[1].Content);
        Assert.Equal(new string('c', 300), toolMsgs[2].Content); // last 3 verbatim
        Assert.Equal(new string('d', 300), toolMsgs[3].Content);
        Assert.Equal(new string('e', 300), toolMsgs[4].Content);
    }

    [Fact]
    public void TrimStaleToolOutputs_ReclaimsBudget()
    {
        var cw = new ContextWindow(2_000);
        cw.AddUserMessage("task");
        for (int i = 0; i < 10; i++)
            cw.AddToolOutput(new string('x', 2000), $"T{i}");

        Assert.False(cw.IsWithinBudget());
        var before = cw.GetTotalTokens();

        cw.TrimStaleToolOutputs();

        Assert.True(cw.GetTotalTokens() < before);
        Assert.True(cw.IsWithinBudget());
    }

    // ── P6: rules file ──

    [Fact]
    public void AgentsMd_InWorkingDir_IsInjectedIntoSystemPrompt()
    {
        // The engine injects AGENTS.md content from the working directory into the
        // system prompt (BuildSystemToolsPrompt → "## PROJECT RULES (AGENTS.md)").
        // Pin the actual injection via a real engine, not just file readability.
        var rulesPath = Path.Combine(_dir, "AGENTS.md");
        File.WriteAllText(rulesPath, "Never touch config files.");

        var engine = new MockEngine(workingDir: _dir);
        var method = typeof(EAgentEngine).GetMethod("BuildSystemToolsPrompt",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(method);
        var prompt = (string)method.Invoke(engine, null)!;

        Assert.Contains("## PROJECT RULES (AGENTS.md)", prompt);
        Assert.Contains("Never touch config files.", prompt);
    }

    private static List<(string, ECAssistant.Core.Tools.EToolBase)> SchemaProviders()
    {
        var found = new List<(string, ECAssistant.Core.Tools.EToolBase)>();
        foreach (var t in typeof(EAgentEngine).Assembly.GetTypes()
                     .Where(t => t.IsSubclassOf(typeof(ECAssistant.Core.Tools.EToolBase)) && !t.IsAbstract))
        {
            try
            {
                object? instance;
                if (t.Name == "EShellAgent")
                {
                    // EShellAgent ctor needs (IProcessRunner, EAgentConfig, string).
                    instance = Activator.CreateInstance(t,
                        new StubProcessRunner(), new EAgentConfig(), Directory.GetCurrentDirectory());
                }
                else
                {
                    instance = Activator.CreateInstance(t);
                }
                if (instance is ECAssistant.Core.Tools.EToolBase tool) found.Add((t.Name, tool));
            }
            catch { /* constructors needing other deps are skipped */ }
        }
        return found;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}

/// <summary>No-op process runner for tool schema tests.</summary>
file sealed class StubProcessRunner : ECAssistant.Core.Interfaces.IProcessRunner
{
    public Task<ProcessResult> ExecuteAsync(string command, string? workDir = null, CancellationToken ct = default)
        => Task.FromResult(new ProcessResult(0, "", "", true));
}
