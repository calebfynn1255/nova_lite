namespace NovaLite.Core.Models;

/// <summary>Inference hyper-parameters forwarded to the engine.</summary>
public sealed class InferenceOptions
{
    public static readonly InferenceOptions Default = new();

    public float Temperature { get; set; } = 0.7f;
    public float TopP { get; set; } = 0.9f;
    public float RepetitionPenalty { get; set; } = 1.1f;
    public int MaxTokens { get; set; } = 2048;
    public int Seed { get; set; } = -1; // -1 = random
    public IReadOnlyList<string> StopSequences { get; set; } = [];

    // Hardware/Performance Config
    public int Threads { get; set; } = 4;
    public int BatchSize { get; set; } = 512;
    public int GpuLayers { get; set; } = 0;
}
