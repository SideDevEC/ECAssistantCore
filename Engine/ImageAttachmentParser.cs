using System.Text;
using System.Text.RegularExpressions;

namespace ECAssistant.Core.Engine;

/// <summary>
/// Parses <c>[image:&lt;path&gt;]</c> attachment tokens out of user input.
/// Valid image extensions: .png .jpg .jpeg .webp .gif .bmp
/// </summary>
// Stateless utility — no mutable state, no external dependencies.
public static class ImageAttachmentParser
{
    public const string TokenPattern = @"\[image:(?<path>[^\[\]]+)\]";

    private static readonly string[] SupportedExtensions =
        { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    /// <summary>One parsed attachment reference.</summary>
    public sealed record ImageRef(string OriginalPath, string FullPath, string DataUri);

    // Stateless factory on immutable record
    public static (string CleanPrompt, IReadOnlyList<ImageRef> Images) Extract(
        string input, string workingDir)
    {
        var refs = new List<ImageRef>();
        var missing = new List<string>();

        var cleaned = Regex.Replace(input ?? "", TokenPattern, match =>
        {
            var rawPath = match.Groups["path"].Value.Trim();
            if (rawPath.Length == 0) return "";

            var fullPath = Path.IsPathRooted(rawPath)
                ? rawPath
                : Path.Combine(workingDir, rawPath);

            if (!File.Exists(fullPath))
            {
                missing.Add(rawPath);
                return "";
            }

            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            if (!SupportedExtensions.Contains(ext))
            {
                missing.Add($"{rawPath} (unsupported type '{ext}')");
                return "";
            }

            try
            {
                var mime = ext switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".webp" => "image/webp",
                    ".gif" => "image/gif",
                    _ => "image/bmp"
                };
                var b64 = Convert.ToBase64String(File.ReadAllBytes(fullPath));
                refs.Add(new ImageRef(rawPath, fullPath, $"data:{mime};base64,{b64}"));
                return "";
            }
            catch (IOException)
            {
                missing.Add(rawPath);
                return "";
            }
        });

        var note = "";
        if (missing.Count > 0)
            note = $"\n(image(s) omitted: {string.Join(", ", missing)})";

        var cleanPrompt = NormalizeWhitespace(cleaned) + note;
        return (cleanPrompt.Trim(), refs.AsReadOnly());
    }

    private static string NormalizeWhitespace(string text)
    {
        var sb = new StringBuilder(text);
        sb.Replace("  ", " ");
        return sb.ToString().Trim();
    }
}
