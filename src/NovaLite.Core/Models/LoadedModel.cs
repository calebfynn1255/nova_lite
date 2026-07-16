namespace NovaLite.Core.Models;

/// <summary>Descriptor for a successfully loaded model, carrying opaque native handles.</summary>
public sealed class LoadedModel : IDisposable
{
    public string FilePath { get; init; } = string.Empty;
    public string Format { get; init; } = string.Empty;   // "GGUF", "ONNX", "MLX", "Safetensors"
    public string DisplayName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public long ContextLength { get; init; }
    public DateTime LoadedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Allows loaders to attach custom state like active sessions.</summary>
    public object? State { get; set; }

    /// <summary>Opaque native handle — type depends on the loader (IntPtr, OrtSession, etc.).</summary>
    public object? NativeHandle { get; set; }

    private bool _disposed;
    private Action? _disposeAction;

    /// <summary>Register a dispose callback called by <see cref="Dispose"/>.</summary>
    public void RegisterDispose(Action action) => _disposeAction = action;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeAction?.Invoke();
    }
}
