using Microsoft.Extensions.Logging;
using NovaLite.Core.Interfaces;
using NovaLite.Core.Models;

namespace NovaLite.Engine.Loaders;

/// <summary>
/// Apple MLX loader — only available on macOS arm64 (Apple Silicon).
/// On other platforms, <see cref="CanLoad"/> always returns false and
/// <see cref="LoadAsync"/> throws <see cref="PlatformNotSupportedException"/>.
/// </summary>
public sealed class MLXLoader : IModelLoader
{
    private readonly ILogger<MLXLoader> _logger;

    public IReadOnlyList<string> SupportedExtensions { get; } = [".mlxmodel", ""];   // MLX = directory

    public MLXLoader(ILogger<MLXLoader> logger)
    {
        _logger = logger;
    }

    public bool CanLoad(string path)
    {
#if PLATFORM_MLX
        // An MLX model is a directory containing config.json + *.safetensors
        return Directory.Exists(path) &&
               File.Exists(Path.Combine(path, "config.json"));
#else
        return false;
#endif
    }

    public Task<LoadedModel> LoadAsync(string filePath, CancellationToken ct = default)
    {
#if PLATFORM_MLX
        _logger.LogInformation("Loading MLX model: {Path}", filePath);
        // MLX interop is handled through Swift/Python bridge — stub for now
        throw new NotImplementedException("MLX inference bridge not yet implemented.");
#else
        throw new PlatformNotSupportedException(
            "MLX models are only supported on macOS with Apple Silicon.");
#endif
    }

    public void Unload(LoadedModel model) => model.Dispose();
}
