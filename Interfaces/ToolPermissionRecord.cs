namespace ECAssistant.Core.Interfaces;

/// <summary>
/// Tool permission policy record (immutable snapshot of a tool's permission level).
/// Renamed from <c>ToolPolicy</c> to avoid collision with the mutable
/// <see cref="ECAssistant.Core.Tools.ToolPolicy"/> manager class.
/// </summary>
public record ToolPermissionRecord(
    string ToolName,
    string Level,
    string? Reason = null
)
{
   // Stateless factory — immutable record
    public static ToolPermissionRecord Allowed(string name) => new(name, "Allowed");
   // Stateless factory — immutable record
    public static ToolPermissionRecord Approved(string name) => new(name, "Approved");
}
