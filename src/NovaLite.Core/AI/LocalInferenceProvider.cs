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
    private const int MaxPromptChars = 64000;
    private const int MaxAttachedFileChars = 16000;
    // DeepSeek-Coder chat templates use these full-width pipes. Keep the delimiter in
    // conversation history, but never expose it in a generated reply (the session
    // treats it as a stop sequence).
    private const string DeepSeekBeginOfSentence = "<\uFF5Cbegin of sentence\uFF5C>";
    private const string DeepSeekEndOfSentence = "<\uFF5Cend of sentence\uFF5C>";

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

    public static string PreparePromptText(string? text, int maxChars = MaxAttachedFileChars)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        if (trimmed.Length <= maxChars)
            return trimmed;

        return trimmed[..maxChars] + Environment.NewLine + Environment.NewLine +
               $"[Content truncated to {maxChars} characters to stay within the model context window.]";
    }

    /// <summary>
    /// Replaces literal ChatML / Llama-3 control-token strings in user-supplied content
    /// so the model cannot mistake them for real conversation boundaries.
    /// </summary>
    private static string SanitizeContent(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        // Replace the pipe characters inside known control tokens so they survive
        // tokenization as plain text rather than being treated as delimiters.
        return text
            .Replace("<|im_start|>",   "<‖im_start‖>")
            .Replace("<|im_end|>",     "<‖im_end‖>")
            .Replace("<|eot_id|>",     "<‖eot_id‖>")
            .Replace("<|end_of_text|>","<‖end_of_text‖>")
            .Replace("<|begin_of_text|>","<‖begin_of_text‖>")
            .Replace("<|start_header_id|>","<‖start_header_id‖>")
            .Replace("<|end_header_id|>",  "<‖end_header_id‖>");
    }

    private static string TrimPromptToBudget(string prompt)
    {
        if (prompt.Length <= MaxPromptChars)
            return prompt;

        var startIndex = prompt.Length - MaxPromptChars;
        return "[Earlier prompt context omitted to stay within the model context window.]" +
               Environment.NewLine +
               prompt[startIndex..];
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

        bool isLlama3 = _model != null && (
            _model.FilePath.Contains("llama_3", StringComparison.OrdinalIgnoreCase) ||
            _model.FilePath.Contains("llama-3", StringComparison.OrdinalIgnoreCase) ||
            _model.FilePath.Contains("llama3", StringComparison.OrdinalIgnoreCase) ||
            _model.DisplayName.Contains("llama 3", StringComparison.OrdinalIgnoreCase) ||
            _model.DisplayName.Contains("llama-3", StringComparison.OrdinalIgnoreCase) ||
            _model.DisplayName.Contains("llama_3", StringComparison.OrdinalIgnoreCase)
        );

        bool isDeepSeek = _model != null && (
            _model.FilePath.Contains("deepseek", StringComparison.OrdinalIgnoreCase) ||
            _model.DisplayName.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
        );

        if (isLlama3)
        {
            sb.Append("<|begin_of_text|>");
        }
        else if (isDeepSeek)
        {
            sb.Append(DeepSeekBeginOfSentence);
        }

        foreach (var msg in messages)
        {
            // Skip the trailing empty assistant placeholder message — we append the manual turn starter below
            if (msg.Role == ChatRole.Assistant && string.IsNullOrWhiteSpace(msg.Content))
                continue;

            string role = msg.Role switch
            {
                ChatRole.System => "system",
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                _ => "user"
            };

            string content = SanitizeContent(msg.Content?.Trim()) ?? "";

            if (isLlama3)
            {
                sb.Append($"<|start_header_id|>{role}<|end_header_id|>\n\n");
                
                if (msg.Role == ChatRole.User && !string.IsNullOrEmpty(msg.AttachedFileName) && !string.IsNullOrEmpty(msg.AttachedFileContent))
                {
                    var attachedContent = PreparePromptText(SanitizeContent(msg.AttachedFileContent), MaxAttachedFileChars);
                    bool isImage = IsImageFile(msg.AttachedFileName);
                    sb.AppendLine(isImage ? $"[Image Analysis: {msg.AttachedFileName}]" : $"[Extracted Text from Attachment: {msg.AttachedFileName}]");
                    sb.AppendLine("```");
                    sb.AppendLine(attachedContent);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }

                sb.AppendLine(content);
                sb.Append("<|eot_id|>");
            }
            else if (isDeepSeek)
            {
                string attachedString = "";
                if (msg.Role == ChatRole.User && !string.IsNullOrEmpty(msg.AttachedFileName) && !string.IsNullOrEmpty(msg.AttachedFileContent))
                {
                    var attachedContent = PreparePromptText(SanitizeContent(msg.AttachedFileContent), MaxAttachedFileChars);
                    bool isImage = IsImageFile(msg.AttachedFileName);
                    string prefix = isImage ? "[Image Analysis:" : "[Extracted Text from Attachment:";
                    attachedString = $"{prefix} {msg.AttachedFileName}]\n```\n{attachedContent}\n```\n\n";
                }

                if (role == "user")
                {
                    sb.Append("User: ");
                    sb.Append(attachedString);
                    sb.Append(content);
                    sb.Append("\n\n");
                }
                else if (role == "assistant")
                {
                    sb.Append("Assistant: ");
                    sb.Append(content);
                    sb.Append(DeepSeekEndOfSentence);
                }
                else // system
                {
                    sb.Append(content);
                    sb.Append("\n\n");
                }
            }
            else
            {
                sb.Append("<|im_start|>");
                sb.AppendLine(role);
                
                if (msg.Role == ChatRole.User && !string.IsNullOrEmpty(msg.AttachedFileName) && !string.IsNullOrEmpty(msg.AttachedFileContent))
                {
                    var attachedContent = PreparePromptText(SanitizeContent(msg.AttachedFileContent), MaxAttachedFileChars);
                    bool isImage = IsImageFile(msg.AttachedFileName);
                    sb.AppendLine(isImage ? $"[Image Analysis: {msg.AttachedFileName}]" : $"[Extracted Text from Attachment: {msg.AttachedFileName}]");
                    sb.AppendLine("```");
                    sb.AppendLine(attachedContent);
                    sb.AppendLine("```");
                    sb.AppendLine();
                }

                sb.AppendLine(content);
                sb.AppendLine("<|im_end|>");
            }
        }

        // Add the assistant turn starter so the model knows to reply
        if (isLlama3)
        {
            sb.Append("<|start_header_id|>assistant<|end_header_id|>\n\n");
        }
        else if (isDeepSeek)
        {
            sb.Append("Assistant:");
        }
        else
        {
            sb.Append("<|im_start|>assistant\n");
        }

        var prompt = TrimPromptToBudget(sb.ToString());

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

    private static bool IsImageFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".webp" || ext == ".tiff";
    }
}
