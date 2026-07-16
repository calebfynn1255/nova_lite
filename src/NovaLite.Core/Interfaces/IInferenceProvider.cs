namespace NovaLite.Core.Interfaces;

/// <summary>
/// Abstracts an inference backend that can stream token responses
/// given a conversation history and inference parameters.
/// </summary>
public interface IInferenceProvider : IAsyncDisposable
{
    /// <summary>Friendly display name of this provider.</summary>
    string Name { get; }

    /// <summary>Whether the provider has a model loaded and is ready to infer.</summary>
    bool IsReady { get; }

    /// <summary>Loads a model from a <see cref="LoadedModel"/> descriptor.</summary>
    Task LoadAsync(Models.LoadedModel model, CancellationToken ct = default);

    /// <summary>Unloads the current model and frees native resources.</summary>
    Task UnloadAsync();

    /// <summary>
    /// Streams generated tokens for the given conversation.
    /// Each yielded string is a raw token fragment (not accumulated).
    /// </summary>
    IAsyncEnumerable<string> InferStreamAsync(
        IReadOnlyList<Models.ChatMessage> messages,
        Models.InferenceOptions options,
        CancellationToken ct = default);
}
