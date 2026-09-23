using System.Text;
using System.Text.Json;
using ECAssistant.Core.Config;
using ECAssistant.Core.Interfaces;
using ECAssistant.Core.Vision;

namespace ECAssistant.Core.Tools.EVision;

/// <summary>
/// EVisionStructure — analyze an image file or PDF page and return a fixed,
/// versioned JSON structure of detected UI elements (headers, labels,
/// buttons, inputs...), their approximate positions, label↔control
/// associations, and semantic groups. Output shape is VisionStructureResult
/// (schemaVersion "1.0"); parsing always yields a fully-populated result.
/// Requires a vision-capable model (SupportsVision).
/// </summary>
public class EVisionStructureTool : EToolBase
{
    private const string ToolName = "EVisionStructure";

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp" };

    private readonly IInferenceEngine _inferenceEngine;
    private readonly IPdfPageRenderer _pdfRenderer;
    private readonly int _maxTokens;
    private readonly float _temperature;

    public override string Name => ToolName;

    public override string Description =>
        "Analyze a local image file (png/jpg/webp/gif/bmp) or PDF page and extract its UI structure " +
        "as a fixed JSON schema: elements (headers, labels, buttons, inputs, checkboxes, tables...) " +
        "with approximate pixel bounding boxes, label-to-control associations, and semantic groups " +
        "(form/section/toolbar/list). Use for screenshots and scanned documents that need to be read " +
        "programmatically. Requires a vision-capable model. Use ONLY when the user asks to analyze " +
        "a local image or PDF file. Do NOT use for questions that involve no local file.";

    public override string GetParameterSchema() =>
        """
        {
          "type": "object", "required": ["path"],
          "properties": {
            "path": { "type": "string", "description": "Path to the image or PDF file" },
            "page": { "type": "integer", "description": "PDF page number (1-based). Screenshots ignore this." }
          }
        }
        """;

    public override string UsageExample =>
        "EVisionStructure(path:screenshots/login.png)\n" +
        "EVisionStructure(path:docs/scan.pdf, page:1)";

    public override string GetToolRules() =>
        "Bounding boxes are approximate regions, not pixel-accurate. The JSON always follows the " +
        "fixed VisionStructureResult schema (schemaVersion 1.0) — present it as-is or query it; " +
        "never invent fields. For multi-page PDFs call the tool once per page.";

    public override bool IsEnabled { get; protected set; } = true;

    public EVisionStructureTool(
        IInferenceEngine inferenceEngine,
        IPdfPageRenderer pdfRenderer,
        AppConfig config)
    {
        _inferenceEngine = inferenceEngine ?? throw new ArgumentNullException(nameof(inferenceEngine));
        _pdfRenderer = pdfRenderer ?? throw new ArgumentNullException(nameof(pdfRenderer));

        var section = config?.Tools.TryGetValue(ToolName, out var el) == true ? el : (JsonElement?)null;
        _maxTokens = ReadCfg(section, "maxTokens", 2048);
        _temperature = ReadCfg(section, "temperature", 0f);
        IsEnabled = ReadCfg(section, "enabled", true);
    }

    public override object GetConfigSection() => new
    {
        enabled = true,
        maxTokens = 2048,
        temperature = 0f
    };

    public override async Task<EToolResult> ExecuteAsync(
        Dictionary<string, string?> arguments, CancellationToken cancellationToken = default)
    {
        var path = arguments.TryGetValue("path", out var p) ? p?.Trim() : null;
        if (string.IsNullOrEmpty(path))
            return EToolResult.Failure(ToolName, "Missing required argument: path");

        if (!File.Exists(path))
            return EToolResult.Failure(ToolName, $"File not found: {path}");

        var ext = Path.GetExtension(path);
        byte[] imageBytes;
        string sourceKind;
        int page = 1;

        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            page = ParsePage(arguments) ?? 1;
            if (page < 1)
                return EToolResult.Failure(ToolName, "page must be >= 1");

            imageBytes = await _pdfRenderer.RenderPageToPngAsync(path, page, cancellationToken);
            if (imageBytes is null)
                return EToolResult.Failure(ToolName,
                    $"Could not render page {page} of '{path}'. Only page 1 is supported by the current renderer.");
            sourceKind = "pdf-page";
        }
        else if (SupportedImageExtensions.Contains(ext))
        {
            imageBytes = await File.ReadAllBytesAsync(path, cancellationToken);
            sourceKind = "screenshot";
        }
        else
        {
            return EToolResult.Failure(ToolName,
                $"Unsupported file type '{ext}'. Supported: .png .jpg .jpeg .webp .gif .bmp .pdf");
        }

        var dataUri = $"data:image/png;base64,{Convert.ToBase64String(imageBytes)}";
        var prompt = VisionStructurePromptBuilder.Build(sourceKind, page);

        string raw;
        try
        {
            raw = await _inferenceEngine.GenerateAsync(prompt, new InferenceRequestParams
            {
                MaxTokens = _maxTokens,
                Temperature = _temperature,
                Stream = false,
                ImageDataUris = { dataUri },
                Grammar = VisionStructureGrammar.Gbnf
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return EToolResult.Failure(ToolName, $"Vision inference failed: {ex.Message}");
        }

        if (!VisionStructureJsonParser.TryParse(raw, out var result, out var errors) || result is null)
        {
            var detail = string.Join("; ", errors.Take(5));
            return EToolResult.Failure(ToolName,
                $"Vision output did not produce a valid structure. Details: {detail}");
        }

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        return EToolResult.Success(
            ToolName,
            json,
            new Dictionary<string, string>
            {
                ["elements"] = result.Elements.Count.ToString(),
                ["groups"] = result.Groups.Count.ToString(),
                ["schemaVersion"] = result.SchemaVersion,
                ["source"] = sourceKind
            });
    }

    private static int? ParsePage(Dictionary<string, string?> arguments)
    {
        if (!arguments.TryGetValue("page", out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        return int.TryParse(raw, out var page) ? page : null;
    }
        /// <summary>v15: small tier gets output discipline; large tier gets analysis guidance.</summary>
        public override string GetToolRulesForTier(bool isLargeTier)
        {
            if (isLargeTier)
            {
                return "Rules:\n" +
                       "- Report structure decisions with rationale (names, layers, boundaries) — not just a file list.\n";
            }
            return "Rules:\n" +
                   "- Report ONLY the structure that was asked for (classes, files, or folders) — no extra suggestions.\n" +
                   "- Copy names EXACTLY as they appear in the output.\n";
        }

}
