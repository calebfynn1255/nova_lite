using NovaLite.Core.Models;

namespace NovaLite.Core.Interfaces;

/// <summary>
/// Contract implemented by each format-specific model loader in NovaLite.Engine.
/// </summary>
public interface IModelLoader
{
    /// <summary>File extensions this loader handles, e.g. [".gguf"].</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Checks magic bytes / file header to confirm this loader can handle the file.</summary>
    bool CanLoad(string filePath);

    /// <summary>Loads the model from disk and returns a descriptor with native handles.</summary>
    Task<LoadedModel> LoadAsync(string filePath, CancellationToken ct = default);

    /// <summary>Releases all native resources associated with the model.</summary>
    void Unload(LoadedModel model);
}
