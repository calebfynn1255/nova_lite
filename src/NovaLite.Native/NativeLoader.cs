using System.Runtime.InteropServices;

namespace NovaLite.Native;

/// <summary>
/// Resolves the path to the platform-specific llama native library
/// and ensures it is loaded before any P/Invoke calls are made.
/// </summary>
public static class NativeLoader
{
    private static bool _loaded;
    private static readonly object _lock = new();

    /// <summary>
    /// Ensures the llama native library is loaded from the <c>runtimes/</c>
    /// subdirectory appropriate for the current OS and architecture.
    /// Call this once before any <see cref="LlamaCppBindings"/> call.
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            var libPath = ResolveLibraryPath();
            NativeLibrary.Load(libPath);
            _loaded = true;
        }
    }

    private static string ResolveLibraryPath()
    {
        var rid = GetRuntimeIdentifier();
        var libName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "llama.dll"
                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)     ? "libllama.dylib"
                    : "libllama.so";

        // Look next to the executing assembly first (deployment layout)
        var assemblyDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(assemblyDir, "runtimes", rid, "native", libName);
        if (File.Exists(candidate)) return candidate;

        // Fallback: same directory as the assembly
        candidate = Path.Combine(assemblyDir, libName);
        if (File.Exists(candidate)) return candidate;

        throw new DllNotFoundException(
            $"Cannot find '{libName}' for runtime '{rid}'. " +
            $"Place it in: runtimes/{rid}/native/{libName}");
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.OSArchitecture == Architecture.X64 ? "win-x64" : "win-arm64";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }
}
