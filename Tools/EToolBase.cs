using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Session;

namespace ECAssistant.Core.Tools;

/// <summary>
/// Base class for all Tools. 
/// Each tool must extend this and implement ExecuteAsync().
/// New tools are added by creating a subclass — no changes to Harness required.
/// </summary>
public abstract class EToolBase
{
             /// <summary>Unique name of this tool (e.g., "EShellAgent")</summary>
    public abstract string Name { get; }

          /// <summary>Human-readable description for system prompt injection</summary>
    public abstract string Description { get; }

          /// <summary>Whether this tool is enabled. Read from config.Tools[Name].enabled.</summary>
    public virtual bool IsEnabled { get; protected set; } = true;

               /// <summary>Example usage text shown to the LLM in the system prompt</summary>
    public abstract string UsageExample { get; }

    /// <summary>
    /// Session context — set right before registration via session.RegisterTool().
    /// Provides access to session info, memory, and the secondary LLM.
    /// Null until the tool is registered.
    /// </summary>
    public ISessionContext? Session { get; internal set; }

                /// <summary>
                /// Execute the tool with given arguments.
                 /// </summary>
             /// <param name="arguments">Dictionary of argument key/value pairs from the LLM</param>
    public abstract Task<EToolResult> ExecuteAsync(Dictionary<string, string?> arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// v10.24: Returns the default config section for this tool.
    /// Called when the tool is registered and its config section is not yet in appsettings.json.
    /// Every tool returns at minimum { enabled = true }.
    /// Override to add tool-specific parameters.
    /// </summary>
    public virtual object GetConfigSection() => new { enabled = true };

                /// <summary>
                /// Override to provide additional tool-specific system prompt text.
                /// Returned text is appended to the main system prompt.
                 /// </summary>
          public virtual string GetExtendedSystemPrompt() 
                  => string.Empty;

                   /// <summary>
                    /// Return a one-line XML example showing how to call this tool.
                    /// The LLM sees this next to each tool name in the system prompt.
                    /// Override for useful examples. Default returns nothing.
                     /// </summary>
          public virtual string GetToolExample() 
                   => string.Empty;

                   /// <summary>
                    /// Override to provide strict policy rules for this tool.
                    /// Returned text is injected into the system prompt alongside examples.
                    /// Use for non-negotiable constraints, format requirements, or behavioral rules.
                     /// </summary>
          public virtual string GetToolRules() 
                   => string.Empty;

                   /// <summary>
                    /// One unified block for the LLM: Name then Description then Rules then Examples.
                    /// Override GetToolRules() to add policy constraints on top of existing examples.
                    /// The engine calls this once per tool during registration prompt injection.
                     /// </summary>
          public string ToSystemPromptBlock()
               {
                var sb = new System.Text.StringBuilder();
              sb.AppendLine($"## {Name}");
              sb.AppendLine($"{Description}");

            // Rules come before examples (enforce first, illustrate second)
            var rules = GetToolRules();
            if (!string.IsNullOrEmpty(rules))
               {
                sb.AppendLine();
                sb.Append(rules);
               }

            var example = GetToolExample();
            if (!string.IsNullOrEmpty(example))
               {
                sb.AppendLine();
                sb.Append("Examples:");
                sb.AppendLine();
                sb.Append(example);
               }

            return sb.ToString();
           }

    // ── v10.24: Config helpers ──

    /// <summary>
    /// Read a value from the tool's config section in EAgentConfig.Tools.
    /// Returns defaultValue if the key is not found or the section doesn't exist.
    /// </summary>
    protected static T ReadConfig<T>(Dictionary<string, JsonElement> tools, string toolName, string key, T defaultValue)
    {
        if (tools.TryGetValue(toolName, out var section) && section.ValueKind == JsonValueKind.Object)
        {
            if (section.TryGetProperty(key, out var prop))
            {
                try { return prop.Deserialize<T>() ?? defaultValue; }
                catch { return defaultValue; }
            }
        }
        return defaultValue;
    }

    /// <summary>
    /// Check if a tool is enabled in the config.
    /// </summary>
    protected static bool IsToolEnabled(Dictionary<string, JsonElement> tools, string toolName)
        => ReadConfig(tools, toolName, "enabled", true);

    /// <summary>
    /// Read a config value from a JsonElement section. Shared by all tools.
    /// Replaces the duplicated ReadCfg&lt;T&gt; across tool files.
    /// </summary>
    protected static T ReadCfg<T>(JsonElement? section, string key, T defaultValue)
    {
        if (section.HasValue && section.Value.ValueKind == JsonValueKind.Object)
        {
            if (section.Value.TryGetProperty(key, out var prop))
            {
                try { return prop.Deserialize<T>() ?? defaultValue; } catch { return defaultValue; }
            }
        }
        return defaultValue;
    }
}


