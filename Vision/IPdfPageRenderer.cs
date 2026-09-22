using System.Threading;
using System.Threading.Tasks;

namespace ECAssistant.Core.Vision;

/// <summary>
/// Renders one page of a PDF document to PNG bytes so it can be sent through
/// the vision pipeline. Implementations are platform-specific; inject an
/// alternative (e.g. a full rasterizer) without touching callers.
/// </summary>
public interface IPdfPageRenderer
{
    /// <summary>
    /// Render the given 1-based page of the PDF to PNG.
    /// Returns null when rendering fails or the page does not exist.
    /// </summary>
    Task<byte[]?> RenderPageToPngAsync(string pdfPath, int page, CancellationToken ct = default);
}
