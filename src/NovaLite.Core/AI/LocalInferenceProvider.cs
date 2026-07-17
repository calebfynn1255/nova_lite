using Microsoft.Extensions.Logging;
using NovaLite.Core.Interfaces;
using NovaLite.Core.Models;
using System.Runtime.CompilerServices;

namespace NovaLite.Core.AI;

/// <summary>
/// Sits between the UI/ConversationService and the engine's IModelLoader.
/// Owns context-windowing, message formatting, and streaming orchestration.
/// </summary>
public sealed class LocalInferenceProvider : IInferenceProvider
{
    private readonly IModelLoader _loader;
    private readonly ILogger<LocalInferenceProvider> _logger;
    private LoadedModel? _model;

    public string Name => "LocalInferenceProvider";
    public bool IsReady => _model is not null;

    public LocalInferenceProvider(IModelLoader loader, ILogger<LocalInferenceProvider> logger)
    {
        _loader = loader;
        _logger = logger;
    }

    public async Task LoadAsync(LoadedModel model, CancellationToken ct = default)
    {
        _logger.LogInformation("Loading model: {Path}", model.FilePath);
        await Task.CompletedTask; // model is already loaded by EngineFactory
        _model = model;
    }

    public async Task UnloadAsync()
    {
        if (_model is null) return;
        _logger.LogInformation("Unloading model: {Name}", _model.DisplayName);
        _loader.Unload(_model);
        _model.Dispose();
        _model = null;
        await Task.CompletedTask;
    }

    public async IAsyncEnumerable<string> InferStreamAsync(
        IReadOnlyList<ChatMessage> messages,
        InferenceOptions options,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (_model is null)
            throw new InvalidOperationException("No model loaded. Call LoadAsync first.");

        _logger.LogDebug("Starting inference. Messages: {Count}", messages.Count);

        // Build a proper ChatML multi-turn prompt from the full conversation history.
        // This gives the model memory of previous exchanges.
        var sb = new System.Text.StringBuilder();

        foreach (var msg in messages)
        {
            string role = msg.Role switch
            {
                ChatRole.System => "system",
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                _ => "user"
            };

            sb.Append("<|im_start|>");
            sb.AppendLine(role);
            sb.AppendLine(msg.Content);
            sb.AppendLine("<|im_end|>");
        }

        // Add the assistant turn starter so the model knows to reply
        sb.Append("<|im_start|>assistant\n");

        var prompt = sb.ToString();

        if (_model.State is NovaLite.Core.Interfaces.IInferenceSession session)
        {
            await foreach (var token in session.InferAsync(prompt, options, ct))
            {
                yield return token;
            }
        }
        else
        {
            yield return "[Engine not wired correctly]";
        }
    }

    public async ValueTask DisposeAsync()
    {
        await UnloadAsync();
    }
}
