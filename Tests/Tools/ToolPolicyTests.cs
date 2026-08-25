using ECAssistant.Core.Tools;

namespace ECAssistant.Core.Tests.Tools;

public class ToolPolicyTests
{
    // ── Default permissions ──

    [Fact]
    public void Constructor_SetsDefaultPermissions()
    {
        var policy = new ToolPolicy();

        Assert.True(policy.IsAllowed("EFileResearchTool"));
        Assert.True(policy.IsAllowed("EFileAnalyzer"));
        Assert.True(policy.RequiresApproval("EShellAgent"));
    }

    [Fact]
    public void UnknownTool_DefaultsToAllowed()
    {
        var policy = new ToolPolicy();

        Assert.False(policy.RequiresApproval("UnknownTool"));
        Assert.True(policy.IsAllowed("UnknownTool"));
    }

    // ── SetPermission ──

    [Fact]
    public void SetPermission_ApprovalRequired_Works()
    {
        var policy = new ToolPolicy();

        policy.SetPermission("MyTool", approvalRequired: true);

        Assert.True(policy.RequiresApproval("MyTool"));
        Assert.False(policy.IsAllowed("MyTool"));
    }

    [Fact]
    public void SetPermission_Allowed_Works()
    {
        var policy = new ToolPolicy();
        policy.SetPermission("MyTool", approvalRequired: true);
        policy.SetPermission("MyTool", approvalRequired: false);

        Assert.True(policy.IsAllowed("MyTool"));
        Assert.False(policy.RequiresApproval("MyTool"));
    }

    [Fact]
    public void SetPermission_OverwritesExistingPermission()
    {
        var policy = new ToolPolicy();
        policy.SetPermission("EShellAgent", approvalRequired: false, "test");

        Assert.True(policy.IsAllowed("EShellAgent"));
        Assert.False(policy.RequiresApproval("EShellAgent"));
    }

    // ── IsAllowed / RequiresApproval ──

    [Fact]
    public void RequiresApproval_OnAllowedTool_ReturnsFalse()
    {
        var policy = new ToolPolicy();

        Assert.False(policy.RequiresApproval("EFileReaderTool"));
    }

    [Fact]
    public void IsAllowed_OnApprovalRequiredTool_ReturnsFalse()
    {
        var policy = new ToolPolicy();

        Assert.False(policy.IsAllowed("EShellAgent"));
    }

    // ── GetAllPermissions ──

    [Fact]
    public void GetAllPermissions_ReturnsAllRegisteredTools()
    {
        var policy = new ToolPolicy();

        var all = policy.GetAllPermissions();

        Assert.Contains(all, p => p.ToolName == "EFileResearchTool");
        Assert.Contains(all, p => p.ToolName == "EShellAgent");
    }

    [Fact]
    public void GetAllPermissions_AfterSetPermission_IncludesNewTool()
    {
        var policy = new ToolPolicy();
        policy.SetPermission("NewTool", approvalRequired: false);

        var all = policy.GetAllPermissions();

        Assert.Contains(all, p => p.ToolName == "NewTool");
    }

    // ── Check ──

    [Fact]
    public void Check_AllowedTool_ReturnsCanExecute()
    {
        var policy = new ToolPolicy();
        var args = new Dictionary<string, string?>();

        var decision = policy.Check("EFileReaderTool", args);

        Assert.True(decision.CanExecute);
        Assert.False(decision.NeedsApproval);
        Assert.Equal("Allowed", decision.Message);
    }

    [Fact]
    public void Check_ApprovalRequiredTool_ReturnsNeedsApproval()
    {
        var policy = new ToolPolicy();
        var args = new Dictionary<string, string?>();

        var decision = policy.Check("EShellAgent", args);

        Assert.False(decision.CanExecute);
        Assert.True(decision.NeedsApproval);
        Assert.Contains("requires approval", decision.Message);
    }

    [Fact]
    public void Check_UnknownTool_ReturnsCanExecute()
    {
        var policy = new ToolPolicy();
        var args = new Dictionary<string, string?>();

        var decision = policy.Check("UnknownTool", args);

        Assert.True(decision.CanExecute);
    }

    // ── LoadFromConfig ──

    [Fact]
    public void LoadFromConfig_NullEntries_DoesNothing()
    {
        var policy = new ToolPolicy();

        policy.LoadFromConfig(null);

        Assert.True(policy.RequiresApproval("EShellAgent"));
    }

    [Fact]
    public void LoadFromConfig_EmptyList_DoesNothing()
    {
        var policy = new ToolPolicy();

        policy.LoadFromConfig(new List<ToolPermissionConfigEntry>());

        Assert.True(policy.RequiresApproval("EShellAgent"));
    }

    [Fact]
    public void LoadFromConfig_ValidEntries_SetsPermissions()
    {
        var policy = new ToolPolicy();
        var entries = new List<ToolPermissionConfigEntry>
        {
            new() { ToolName = "ToolA", ApprovalRequired = true, Reason = "needs review" },
            new() { ToolName = "ToolB", ApprovalRequired = false, Reason = "safe" },
        };

        policy.LoadFromConfig(entries);

        Assert.True(policy.RequiresApproval("ToolA"));
        Assert.True(policy.IsAllowed("ToolB"));
    }

    [Fact]
    public void LoadFromConfig_OverridesDefaults()
    {
        var policy = new ToolPolicy();
        var entries = new List<ToolPermissionConfigEntry>
        {
            new() { ToolName = "EShellAgent", ApprovalRequired = false, Reason = "overridden" },
        };

        policy.LoadFromConfig(entries);

        Assert.True(policy.IsAllowed("EShellAgent"));
    }

    // ── LoadSystemTools ──

    [Fact]
    public void LoadSystemTools_NullEntries_DoesNothing()
    {
        var policy = new ToolPolicy();

        policy.LoadSystemTools(null);

        Assert.True(policy.RequiresApproval("EShellAgent"));
    }

    [Fact]
    public void LoadSystemTools_ValidEntry_SetsPermission()
    {
        var policy = new ToolPolicy();
        var entries = new List<SystemToolConfigEntry>
        {
            new() { ToolName = "EShellAgent", ApprovalRequired = false, Reason = "test" },
        };

        policy.LoadSystemTools(entries);

        Assert.True(policy.IsAllowed("EShellAgent"));
    }

    [Fact]
    public void LoadSystemTools_DefaultsToApprovalRequired()
    {
        var policy = new ToolPolicy();
        var entries = new List<SystemToolConfigEntry>
        {
            new() { ToolName = "CustomTool" }, // ApprovalRequired defaults to true
        };

        policy.LoadSystemTools(entries);

        Assert.True(policy.RequiresApproval("CustomTool"));
    }
}