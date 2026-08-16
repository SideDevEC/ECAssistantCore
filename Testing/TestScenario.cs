using ECAssistant.Core.Orchestration;
namespace ECAssistant.Core.Testing;

public class TestScenario
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Prompt { get; set; } = "";

    /// <summary>Scripted inputs for approval prompts (e.g., "y" to approve).</summary>
    public string?[] ScriptedInputs { get; set; } = Array.Empty<string?>();

    /// <summary>Assertions to evaluate after the test runs.</summary>
    public List<Func<TestResult, TestContext, bool>> Assertions { get; set; } = new();

    /// <summary>Timeout in seconds (default 120 = 2 min).</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Files that should exist after the test (created by the agent).</summary>
    public List<string> ExpectedFiles { get; set; } = new();

    /// <summary>Files that should NOT exist after the test.</summary>
    public List<string> UnexpectedFiles { get; set; } = new();

    /// <summary>Strings that should appear in the final output.</summary>
    public List<string> ExpectedOutputContains { get; set; } = new();

    /// <summary>Strings that should NOT appear in the final output.</summary>
    public List<string> UnexpectedOutputContains { get; set; } = new();

    /// <summary>Expected orchestrator status.</summary>
    public OrchestratorStatus? ExpectedStatus { get; set; }

    /// <summary>Setup action to run before the test (e.g., create existing files).</summary>
    public Action<string>? Setup { get; set; }

    /// <summary>Minimum number of tool calls expected.</summary>
    public int MinToolCalls { get; set; } = 0;

    /// <summary>Maximum number of tool calls expected (0 = no limit).</summary>
    public int MaxToolCalls { get; set; } = 0;
}
