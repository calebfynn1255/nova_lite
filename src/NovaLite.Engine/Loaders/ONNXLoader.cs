using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using NovaLite.Core.Interfaces;
using NovaLite.Core.Models;

namespace NovaLite.Engine.Loaders;

/// <summary>
/// Loads ONNX models via Microsoft.ML.OnnxRuntime.
/// Automatically detects CUDA if available; falls back to CPU.
/// </summary>
public sealed class ONNXLoader : IModelLoader
{
    private readonly ILogger<ONNXLoader> _logger;

    public IReadOnlyList<string> SupportedExtensions { get; } = [".onnx"];

    public ONNXLoader(ILogger<ONNXLoader> logger)
    {
        _logger = logger;
    }

    public bool CanLoad(string filePath) =>
        File.Exists(filePath) &&
        Path.GetExtension(filePath).Equals(".onnx", StringComparison.OrdinalIgnoreCase);

    public async Task<LoadedModel> LoadAsync(string filePath, CancellationToken ct = default)
    {
        _logger.LogInformation("Loading ONNX model: {Path}", filePath);

        var options = new SessionOptions();
        options.EnableMemoryPattern = true;
        options.ExecutionMode = ExecutionMode.ORT_PARALLEL;

        // Try to enable CUDA; silently fall back to CPU
        try
        {
            options.AppendExecutionProvider_CUDA(0);
            _logger.LogDebug("ONNX: CUDA execution provider enabled");
        }
        catch
        {
            _logger.LogDebug("ONNX: CUDA unavailable, using CPU");
        }

        var session = new InferenceSession(filePath, options);
        var fi = new FileInfo(filePath);

        var model = new LoadedModel
        {
            FilePath = filePath,
            Format = "ONNX",
            DisplayName = Path.GetFileNameWithoutExtension(filePath),
            FileSizeBytes = fi.Length,
            NativeHandle = session
        };
        model.RegisterDispose(() =>
        {
            session.Dispose();
            options.Dispose();
        });

        _logger.LogInformation("ONNX model loaded: {Name}", model.DisplayName);
        await Task.CompletedTask;
        return model;
    }

    public void Unload(LoadedModel model) => model.Dispose();
}
