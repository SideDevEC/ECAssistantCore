using System.Reflection;
using System.Runtime.InteropServices;

namespace ECAssistant.Services;

/// <summary>
/// Extracts embedded native runtime files (llama.dll, libllama.dylib, etc.)
/// to a temp directory at startup so Core.dll is fully self-contained.
/// No external NuGet packages or runtime folders needed by consumers.
/// </summary>
public static class NativeRuntimeExtractor
{
    private static bool _extracted = false;
    private static readonly object _lock = new();
    private static string? _nativeDir;

    /// <summary>
    /// Directory where native libraries have been extracted.
    /// Call EnsureExtracted() first to populate.
    /// </summary>
    public static string NativeDir
    {
        get
        {
            EnsureExtracted();
            return _nativeDir!;
        }
    }

    /// <summary>
    /// Extract embedded native runtime files for the current platform.
    /// Idempotent — only extracts once per process.
    /// Also sets the native library resolver so DllImport finds them.
    /// </summary>
    public static void EnsureExtracted()
    {
        if (_extracted) return;
        lock (_lock)
        {
            if (_extracted) return;

            var assembly = typeof(NativeRuntimeExtractor).Assembly;
            var rid = GetRuntimeIdentifier();
            _nativeDir = Path.Combine(Path.GetTempPath(), "ECAssistant.Core.native", rid);

            // Check version hash — if already extracted with same version, skip
            var versionFile = Path.Combine(_nativeDir, ".version");
            var assemblyVersion = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            if (File.Exists(versionFile) && File.ReadAllText(versionFile).Trim() == assemblyVersion)
            {
                _extracted = true;
                SetNativeResolver(_nativeDir);
                return;
            }

            Directory.CreateDirectory(_nativeDir);

            // Extract all embedded native resources for this platform
            var prefix = $"ECAssistant.runtimes.{rid}.native.";
            var resourceNames = assembly.GetManifestResourceNames();

            foreach (var name in resourceNames)
            {
                if (!name.StartsWith(prefix)) continue;

                var fileName = name.Substring(prefix.Length);
                // Handle subfolder (e.g., avx2/ggml-cpu.dll)
                var fullPath = Path.Combine(_nativeDir, fileName.Replace('/', Path.DirectorySeparatorChar));

                // Skip if already exists with same version
                if (File.Exists(fullPath) && File.Exists(versionFile)) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null) continue;
                using var file = File.Create(fullPath);
                stream.CopyTo(file);
            }

            // Write version file
            File.WriteAllText(versionFile, assemblyVersion);

            // Set the native resolver so DllImport finds our extracted files
            SetNativeResolver(_nativeDir);

            _extracted = true;
        }
    }

    /// <summary>
    /// Get the .NET RID for the current platform.
    /// </summary>
    private static string GetRuntimeIdentifier()
    {
        string os = OperatingSystem.IsWindows() ? "win"
                   : OperatingSystem.IsMacOS() ? "osx"
                   : "linux";

        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            _ => "x64"
        };

        return $"{os}-{arch}";
    }

    /// <summary>
    /// Set the custom assembly load resolver so native DllImport calls
    /// find our extracted native libraries.
    /// </summary>
    private static void SetNativeResolver(string dir)
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeRuntimeExtractor).Assembly,
            (libraryName, assembly, searchPath) =>
            {
                // Try direct: llama.dll / libllama.dylib
                var path = Path.Combine(dir, libraryName);
                if (NativeLibrary.TryLoad(path, out var handle)) return handle;

                // Try with platform-specific extension
                if (OperatingSystem.IsWindows())
                    path = Path.Combine(dir, libraryName + ".dll");
                else if (OperatingSystem.IsMacOS())
                    path = Path.Combine(dir, "lib" + libraryName + ".dylib");
                else
                    path = Path.Combine(dir, "lib" + libraryName + ".so");

                if (NativeLibrary.TryLoad(path, out handle)) return handle;

                // Try subfolders (avx, avx2, avx512, noavx, vulkan)
                foreach (var sub in new[] { "avx2", "avx", "avx512", "noavx", "vulkan" })
                {
                    path = Path.Combine(dir, sub, libraryName);
                    if (NativeLibrary.TryLoad(path, out handle)) return handle;

                    if (OperatingSystem.IsWindows())
                        path = Path.Combine(dir, sub, libraryName + ".dll");
                    else if (OperatingSystem.IsMacOS())
                        path = Path.Combine(dir, sub, "lib" + libraryName + ".dylib");
                    else
                        path = Path.Combine(dir, sub, "lib" + libraryName + ".so");

                    if (NativeLibrary.TryLoad(path, out handle)) return handle;
                }

                return IntPtr.Zero;
            });
    }
}