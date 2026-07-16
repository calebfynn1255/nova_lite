using NovaLite.Core.Interfaces;
using NovaLite.Engine.Loaders;

namespace NovaLite.Engine;

/// <summary>
/// Resolves the correct <see cref="IModelLoader"/> for a given file path
/// by checking supported extensions and then verifying magic bytes.
/// </summary>
public static class EngineFactory
{
    /// <summary>
    /// Returns the appropriate loader for <paramref name="filePath"/>,
    /// or throws <see cref="NotSupportedException"/> if no loader matches.
    /// </summary>
    public static IModelLoader GetLoader(string filePath, IServiceProvider services)
    {
        var loaders = new IModelLoader[]
        {
            (IModelLoader)services.GetService(typeof(GGUFLoader))!,
            (IModelLoader)services.GetService(typeof(ONNXLoader))!,
            (IModelLoader)services.GetService(typeof(SafetensorsLoader))!,
            (IModelLoader)services.GetService(typeof(MLXLoader))!,
        };

        // First pass: exact extension match + magic byte check
        foreach (var loader in loaders)
        {
            var ext = Path.GetExtension(filePath);
            if (loader.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)
                && loader.CanLoad(filePath))
                return loader;
        }

        // Second pass: CanLoad only (covers directory-based formats like MLX)
        foreach (var loader in loaders)
        {
            if (loader.CanLoad(filePath)) return loader;
        }

        throw new NotSupportedException(
            $"No loader found for '{Path.GetFileName(filePath)}'. " +
            $"Supported formats: GGUF, ONNX, Safetensors, MLX.");
    }

    /// <summary>Extension → format display name mapping (for UI badges).</summary>
    public static string GetFormatName(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".gguf"        => "GGUF",
            ".onnx"        => "ONNX",
            ".safetensors" => "Safetensors",
            _              => Directory.Exists(filePath) ? "MLX" : "Unknown"
        };
}
