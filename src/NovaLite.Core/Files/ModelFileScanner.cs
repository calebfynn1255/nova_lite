using NovaLite.Core.Models;

namespace NovaLite.Core.Files;

/// <summary>
/// Scans directories for supported model files and builds <see cref="ModelInfo"/> records.
/// </summary>
public sealed class ModelFileScanner
{
    private static readonly Dictionary<string, string> ExtensionFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".gguf"]        = "GGUF",
        [".onnx"]        = "ONNX",
        [".safetensors"] = "Safetensors",
    };

    /// <summary>
    /// Scans <paramref name="directory"/> (optionally recursive) for model files.
    /// MLX models are folders named *.mlx or containing config.json + model.safetensors.
    /// </summary>
    public IEnumerable<ModelInfo> Scan(string directory, bool recursive = true)
    {
        if (!Directory.Exists(directory)) yield break;

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var file in Directory.EnumerateFiles(directory, "*", searchOption))
        {
            var ext = Path.GetExtension(file);
            if (ExtensionFormats.TryGetValue(ext, out var format))
            {
                yield return BuildInfo(file, format);
            }
        }

        // Detect MLX model directories
        foreach (var dir in Directory.EnumerateDirectories(directory, "*", searchOption))
        {
            if (File.Exists(Path.Combine(dir, "config.json")) &&
                Directory.GetFiles(dir, "*.safetensors").Length > 0)
            {
                var size = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                    .Sum(f => new FileInfo(f).Length);
                yield return new ModelInfo
                {
                    FilePath = dir,
                    Format = "MLX",
                    FileSizeBytes = size
                };
            }
        }
    }

    private static ModelInfo BuildInfo(string path, string format)
    {
        var fi = new FileInfo(path);
        return new ModelInfo
        {
            FilePath = path,
            Format = format,
            FileSizeBytes = fi.Length
        };
    }
}
