using Xunit;
using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Tests.Tools;

// v14.10.2: session-scoped remember-decision approvals.
public class ToolPolicySessionApprovalTests
{
    private static Dictionary<string, string?> Args(params (string k, string v)[] kv)
        => kv.ToDictionary(x => x.k, x => (string?)x.v);

    [Fact]
    public void Check_ECodeEditor_BeforeApproval_RequiresApproval()
    {
        var policy = new ToolPolicy();
        var d = policy.Check("ECodeEditor", new Dictionary<string, string?> { ["action"] = "create" });
        Assert.True(d.NeedsApproval);
    }

    [Fact]
    public void Check_AfterSessionApproval_Allowed()
    {
        var policy = new ToolPolicy();
        policy.ApproveSessionPattern("ECodeEditor", "create");
        var d = policy.Check("ECodeEditor", new Dictionary<string, string?> { ["action"] = "create", ["file"] = "x.txt" });
        Assert.False(d.NeedsApproval);
        Assert.True(d.CanExecute);
    }

    [Fact]
    public void Check_SessionApproval_IsPatternScoped()
    {
        var policy = new ToolPolicy();
        policy.ApproveSessionPattern("EShellAgent", "git");
        // same pattern allowed
        Assert.True(policy.Check("EShellAgent", new Dictionary<string, string?> { ["command"] = "git status" }).CanExecute);
        // different pattern still gated
        Assert.True(policy.Check("EShellAgent", new Dictionary<string, string?> { ["command"] = "rm -rf /tmp/x" }).NeedsApproval);
    }

    [Fact]
    public void BuildSessionPattern_Shell_TakesFirstToken()
    {
        Assert.Equal("git", ToolPolicy.BuildSessionPattern("EShellAgent", new Dictionary<string, string?> { ["command"] = "git status --short" }));
    }

    [Fact]
    public void BuildSessionPattern_ECodeEditor_TakesAction()
    {
        Assert.Equal("create", ToolPolicy.BuildSessionPattern("ECodeEditor", new Dictionary<string, string?> { ["action"] = "create", ["file"] = "f" }));
    }
}
