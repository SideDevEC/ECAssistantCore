using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ECAssistant.Core.Interfaces;

namespace ECAssistant.Core.Vision;

/// <summary>
/// macOS implementation of IPdfPageRenderer using the built-in `sips`
/// utility. Renders the FIRST page only — sips cannot select other pages.
/// Requests for page &gt; 1 return null (caller reports an error); swap in a
/// full rasterizer implementation later without changing consumers.
/// </summary>
public sealed class SipsPdfPageRenderer : IPdfPageRenderer
{
    private readonly IProcessRunner _processRunner;

    public SipsPdfPageRenderer(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<byte[]?> RenderPageToPngAsync(string pdfPath, int page, CancellationToken ct = default)
    {
        if (page != 1) return null;

        var tempPng = Path.Combine(Path.GetTempPath(), $"eca-vision-{Guid.NewGuid():N}.png");
        try
        {
            var result = await _processRunner.ExecuteAsync(
                $"sips -s format png \"{pdfPath}\" --out \"{tempPng}\"", ct: ct);
            if (result.ExitCode != 0 || !File.Exists(tempPng)) return null;
            return await File.ReadAllBytesAsync(tempPng, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[SipsPdfPageRenderer] Non-critical error ignored: {ex.Message}");
            return null;
        }
        finally
        {
            try { if (File.Exists(tempPng)) File.Delete(tempPng); }
            catch { /* temp cleanup best-effort */ }
        }
    }
}
