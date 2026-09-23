using ECAssistant.Core.Config;

namespace ECAssistant.Core.Tests.Config;

public class McpConfigTests
{
    // ── McpConfig ──

    [Fact]
    public void Constructor_Default_HasEmptyServers()
    {
        var config = new McpConfig();

        Assert.NotNull(config.Servers);
        Assert.Empty(config.Servers);
    }

    [Fact]
    public void Servers_CanAddEntries()
    {
        var config = new McpConfig();
        config.Servers["test"] = new McpServerConfig { Command = "npx", Args = new() { "-y", "server" } };

        Assert.Single(config.Servers);
        Assert.Equal("npx", config.Servers["test"].Command);
    }

    // ── McpServerConfig — stdio ──

    [Fact]
    public void StdioConfig_IsStdio_True_WhenCommandSet()
    {
        var config = new McpServerConfig { Command = "npx" };

        Assert.True(config.IsStdio);
        Assert.False(config.IsHttp);
    }

    [Fact]
    public void StdioConfig_IsHttp_False_WhenCommandSet()
    {
        var config = new McpServerConfig { Command = "python3" };

        Assert.False(config.IsHttp);
    }

    [Fact]
    public void StdioConfig_DefaultArgs_EmptyList()
    {
        var config = new McpServerConfig { Command = "npx" };

        Assert.Empty(config.Args);
    }

    [Fact]
    public void StdioConfig_DefaultEnv_EmptyDict()
    {
        var config = new McpServerConfig { Command = "npx" };

        Assert.Empty(config.Env);
    }

    // ── McpServerConfig — HTTP ──

    [Fact]
    public void HttpConfig_IsHttp_True_WhenUrlSet()
    {
        var config = new McpServerConfig { Url = "https://mcp.example.com/sse" };

        Assert.True(config.IsHttp);
        Assert.False(config.IsStdio);
    }

    [Fact]
    public void HttpConfig_IsStdio_False_WhenUrlSet()
    {
        var config = new McpServerConfig { Url = "https://mcp.example.com/sse" };

        Assert.False(config.IsStdio);
    }

    [Fact]
    public void HttpConfig_DefaultHeaders_EmptyDict()
    {
        var config = new McpServerConfig { Url = "https://example.com" };

        Assert.Empty(config.Headers);
    }

    // ── McpServerConfig — neither ──

    [Fact]
    public void EmptyConfig_IsStdio_False_And_IsHttp_False()
    {
        var config = new McpServerConfig();

        Assert.False(config.IsStdio);
        Assert.False(config.IsHttp);
    }

    // ── Tool filtering ──

    [Fact]
    public void Tools_Default_EmptyList()
    {
        var config = new McpServerConfig { Command = "npx" };

        Assert.Empty(config.Tools);
    }

    [Fact]
    public void Exclude_Default_False()
    {
        var config = new McpServerConfig { Command = "npx" };

        Assert.False(config.Exclude);
    }

    [Fact]
    public void Exclude_True_IndicatesBlacklist()
    {
        var config = new McpServerConfig
        {
            Command = "npx",
            Tools = new() { "dangerous_tool" },
            Exclude = true
        };

        Assert.True(config.Exclude);
        Assert.Single(config.Tools);
    }

    // ── ApprovalRequired ──

    [Fact]
    public void ApprovalRequired_Default_Null()
    {
        var config = new McpServerConfig { Command = "npx" };

        Assert.Null(config.ApprovalRequired);
    }

    [Fact]
    public void ApprovalRequired_True_WhenSet()
    {
        var config = new McpServerConfig
        {
            Command = "npx",
            ApprovalRequired = true
        };

        Assert.True(config.ApprovalRequired);
    }

    [Fact]
    public void ApprovalRequired_False_WhenSet()
    {
        var config = new McpServerConfig
        {
            Command = "npx",
            ApprovalRequired = false
        };

        Assert.False(config.ApprovalRequired);
    }

    // ── TimeoutSeconds ──

    [Fact]
    public void TimeoutSeconds_Default_30()
    {
        var config = new McpServerConfig { Command = "npx" };

        Assert.Equal(30, config.TimeoutSeconds);
    }

    [Fact]
    public void TimeoutSeconds_CanOverride()
    {
        var config = new McpServerConfig
        {
            Command = "npx",
            TimeoutSeconds = 60
        };

        Assert.Equal(60, config.TimeoutSeconds);
    }

    // ── AppConfig integration ──

    [Fact]
    public void AppConfig_Mcp_DefaultNull()
    {
        var config = new AppConfig();

        Assert.Null(config.Mcp);
    }

    [Fact]
    public void AppConfig_Mcp_CanBeSet()
    {
        var config = new AppConfig
        {
            Mcp = new McpConfig
            {
                Servers = new()
                {
                    ["filesystem"] = new McpServerConfig
                    {
                        Command = "npx",
                        Args = new() { "-y", "@modelcontextprotocol/server-filesystem" }
                    }
                }
            }
        };

        Assert.NotNull(config.Mcp);
        Assert.Single(config.Mcp.Servers);
        Assert.Equal("npx", config.Mcp.Servers["filesystem"].Command);
    }
}