namespace NovaLite.Core.Models;

/// <summary>Metadata about a model file discovered on disk.</summary>
public sealed class ModelInfo
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName => Path.GetFileName(FilePath);
    public string Format { get; init; } = string.Empty;   // "GGUF", "ONNX", "MLX", "Safetensors"
    public long FileSizeBytes { get; init; }
    public string FileSizeDisplay => FileSizeBytes switch
    {
        >= 1_073_741_824 => $"{FileSizeBytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576     => $"{FileSizeBytes / 1_048_576.0:F0} MB",
        _                => $"{FileSizeBytes / 1024.0:F0} KB"
    };

    // Populated from metadata headers when available
    public string? Architecture { get; set; }
    public string? QuantType { get; set; }
    public int? ParameterCount { get; set; }
    public int? ContextLength { get; set; }
    public bool IsLoaded { get; set; }
}
