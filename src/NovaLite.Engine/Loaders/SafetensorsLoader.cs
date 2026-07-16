using Microsoft.Extensions.Logging;
using NovaLite.Core.Interfaces;
using NovaLite.Core.Models;
using System.Text.Json;

namespace NovaLite.Engine.Loaders;

/// <summary>
/// Loads Safetensors files — reads header metadata and optionally routes
/// to a compatible backend (ONNX Runtime, llama.cpp) based on architecture.
/// Full inference from raw Safetensors tensors is a future milestone.
/// </summary>
public sealed class SafetensorsLoader : IModelLoader
{
    private readonly ILogger<SafetensorsLoader> _logger;

    public IReadOnlyList<string> SupportedExtensions { get; } = [".safetensors"];

    public SafetensorsLoader(ILogger<SafetensorsLoader> logger)
    {
        _logger = logger;
    }

    public bool CanLoad(string filePath) =>
        File.Exists(filePath) &&
        Path.GetExtension(filePath).Equals(".safetensors", StringComparison.OrdinalIgnoreCase);

    public async Task<LoadedModel> LoadAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogInformation("Reading Safetensors metadata: {Path}", filePath);

        await using var fs = File.OpenRead(filePath);

        // Safetensors format: first 8 bytes = header length (LE uint64)
        var lenBuf = new byte[8];
        await fs.ReadExactlyAsync(lenBuf, ct);
        var headerLen = BitConverter.ToUInt64(lenBuf, 0);

        if (headerLen > 100_000_000) // 100 MB sanity check
            throw new InvalidDataException("Safetensors header is implausibly large.");

        var headerBuf = new byte[headerLen];
        await fs.ReadExactlyAsync(headerBuf, ct);
        var headerJson = System.Text.Encoding.UTF8.GetString(headerBuf);

        using var doc = JsonDocument.Parse(headerJson);
        var metadata = doc.RootElement.TryGetProperty("__metadata__", out var meta)
            ? meta.ToString()
            : "(no metadata)";

        _logger.LogDebug("Safetensors header metadata: {Meta}", metadata);

        var fi = new FileInfo(filePath);
        var model = new LoadedModel
        {
            FilePath = filePath,
            Format = "Safetensors",
            DisplayName = Path.GetFileNameWithoutExtension(filePath),
            FileSizeBytes = fi.Length,
            NativeHandle = headerJson   // store parsed header as handle for now
        };

        return model;
    }

    public void Unload(LoadedModel model) => model.Dispose();
}
