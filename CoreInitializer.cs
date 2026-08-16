using System.Runtime.CompilerServices;
using ECAssistant.Services;

namespace ECAssistant;

/// <summary>
/// Runs automatically when the Core assembly is loaded.
/// Extracts embedded native runtime files so LLamaSharp can find them.
/// </summary>
internal static class CoreModuleInitializer
{
    [ModuleInitializer]
    public static void Initialize()
    {
        NativeRuntimeExtractor.EnsureExtracted();
    }
}