namespace ECAssistant.Core;

/// <summary>
/// Builds a system prompt for ECAssistant.Core that includes the required
/// &lt;lm&gt; tag format rules, OS awareness, and custom domain context.
///
/// The OS is auto-detected if WithPlatform() is not called.
/// The &lt;lm&gt; tag rules are always included — the engine parser depends on them.
///
/// Usage:
///   var prompt = SystemPromptBuilder.Create()
///       .WithAgentName("ECSQL Assistant")
///       .WithDescription("You help users manage and query SQL databases.")
///       .Build();
///   // OS is auto-detected: "macOS", "Windows", or "Linux"
///   session.Engine.SystemPromptText = prompt;
///
/// Override OS if needed:
///   .WithPlatform("Linux")
///
/// Add domain rules:
///   .WithCustomRules("Always explain SQL before executing. Use SqlQuery for SELECT statements.")
/// </summary>
public class SystemPromptBuilder
{
    private string _agentName = "ECAssistant";
    private string _description = "a local AI agent with multiple tools and persistent memory";
    private string? _platform = null;  // null = auto-detect
    private string _customRules = "";

    private SystemPromptBuilder() { }

    /// <summary>Start building a system prompt.</summary>
    public static SystemPromptBuilder Create() => new();

    /// <summary>Name the agent appears as in the prompt. Default: "ECAssistant".</summary>
    public SystemPromptBuilder WithAgentName(string name) { _agentName = name; return this; }

    /// <summary>Describe what the agent does. Appended after the name.</summary>
    public SystemPromptBuilder WithDescription(string desc) { _description = desc; return this; }

    /// <summary>
    /// Specify the platform explicitly. If not called, auto-detected:
    /// macOS → "macOS", Windows → "Windows", otherwise → "Linux".
    /// </summary>
    public SystemPromptBuilder WithPlatform(string platform) { _platform = platform; return this; }

    /// <summary>
    /// Add custom rules/instructions. Appended after the tag format rules.
    /// Use this for domain-specific guidance (e.g., "Always explain SQL before executing").
    /// </summary>
    public SystemPromptBuilder WithCustomRules(string rules) { _customRules = rules; return this; }

    /// <summary>
    /// Auto-detect the OS. Returns "macOS", "Windows", or "Linux".
    /// </summary>
    private string DetectPlatform()
    {
        if (OperatingSystem.IsMacOS()) return "macOS";
        if (OperatingSystem.IsWindows()) return "Windows";
        return "Linux";
    }

    /// <summary>
    /// Get OS-specific shell guidance for the platform.
    /// </summary>
    private string GetShellGuidance(string platform)
    {
        return platform switch
        {
            "macOS" => "Use zsh commands. Shell is case-sensitive.",
            "Windows" => "Use PowerShell commands. Shell is case-insensitive.",
            "Linux" => "Use bash commands. Shell is case-sensitive.",
            _ => "Use shell commands appropriate for your platform."
        };
    }

    /// <summary>Build the complete system prompt string.</summary>
    public string Build()
    {
        var platform = _platform ?? DetectPlatform();
        var shellGuidance = GetShellGuidance(platform);

        var sb = new System.Text.StringBuilder();

        // ── Agent identity + OS ──
        sb.AppendLine($"# {_agentName} — System Prompt");
        sb.AppendLine();
        sb.AppendLine($"You are **{_agentName}** — {_description}");
        sb.AppendLine();
        sb.AppendLine($"You work on {platform}. {shellGuidance}");
        sb.AppendLine();

        // ── Decision semantics (v13: output FORMAT is enforced by the server's
        // decision grammar for local models — the model never needs tag instructions.
        // Remote/legacy fallback re-injects format rules via format-retry on demand.) ──
        sb.AppendLine("## HOW YOU WORK");
        sb.AppendLine();
        sb.AppendLine("Each turn you make ONE decision: either finish and give the user your answer, or invoke a tool to gather data. Brief reasoning first, then the decision. Never repeat a tool call that already succeeded with the same arguments.");
        sb.AppendLine();

        // ── Custom domain rules ──
        if (!string.IsNullOrEmpty(_customRules))
        {
            sb.AppendLine("## DOMAIN RULES");
            sb.AppendLine();
            sb.AppendLine(_customRules);
            sb.AppendLine();
        }

        // ── Error handling (generic, always included) ──
        sb.AppendLine("## ERROR HANDLING");
        sb.AppendLine();
        sb.AppendLine("1. Read the error, identify root cause, fix with a new tool call — don't retry the same command.");
        sb.AppendLine("2. After 3 failed attempts, ask the user for help.");
        sb.AppendLine();

        // ── Conversation history format ──
        sb.AppendLine("## CONVERSATION HISTORY");
        sb.AppendLine();
        sb.AppendLine("When you see history from previous turns:");
        sb.AppendLine("- `<user>...text...</user>` = what the user asked");
        sb.AppendLine("- `<tooloutput>ToolName<result>text</result></tooloutput>` = tool result from a previous turn");
        sb.AppendLine("- Your past `<thinking>`/`<toolcall>`/`<output>` blocks are visible in history.");
        sb.AppendLine();

        // ── Operating rules ──
        sb.AppendLine("## OPERATING RULES");
        sb.AppendLine();
        sb.AppendLine("1. Keep responses concise — don't over-explain");
        sb.AppendLine("2. Use the right tool for the job");
        sb.AppendLine();

        // ── Memory ──
        sb.AppendLine("## MEMORY");
        sb.AppendLine();
        sb.AppendLine("Save important patterns for future sessions:");
        sb.AppendLine("- Bugs and their fixes");
        sb.AppendLine("- Solutions that worked");
        sb.AppendLine("- User preferences");
        sb.AppendLine();

        // ── Tools section (engine appends tool definitions at runtime) ──
        sb.AppendLine("## AVAILABLE TOOLS");
        sb.AppendLine();
        sb.AppendLine("Tools are registered at runtime below. Use the tool name exactly as shown.");

        return sb.ToString();
    }
}