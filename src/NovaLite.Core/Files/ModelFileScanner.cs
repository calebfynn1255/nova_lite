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

        foreach (var file in SafeEnumerateFiles(directory, "*", recursive))
        {
            var ext = Path.GetExtension(file);
            if (ExtensionFormats.TryGetValue(ext, out var format))
            {
                yield return BuildInfo(file, format);
            }
        }

        // Detect MLX model directories
        foreach (var dir in SafeEnumerateDirectories(directory, "*", recursive))
        {
            if (File.Exists(Path.Combine(dir, "config.json")) &&
                SafeEnumerateFiles(dir, "*.safetensors", false).Any())
            {
                var size = SafeEnumerateFiles(dir, "*", true)
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

    private static IEnumerable<string> SafeEnumerateFiles(string root, string searchPattern, bool recursive)
    {
        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            var current = directories.Pop();
            string[] files = Array.Empty<string>();
            try
            {
                files = Directory.GetFiles(current, searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            if (!recursive) continue;

            string[] subdirs = Array.Empty<string>();
            try
            {
                subdirs = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var dir in subdirs)
            {
                directories.Push(dir);
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root, string searchPattern, bool recursive)
    {
        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            var current = directories.Pop();
            string[] subdirs = Array.Empty<string>();
            try
            {
                subdirs = Directory.GetDirectories(current, searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var dir in subdirs)
            {
                yield return dir;
                if (recursive)
                {
                    directories.Push(dir);
                }
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
