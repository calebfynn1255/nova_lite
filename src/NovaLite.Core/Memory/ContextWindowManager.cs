using NovaLite.Core.Models;

namespace NovaLite.Core.Memory;

/// <summary>
/// Trims the conversation history to stay within the model's context window.
/// Uses a simple token-budget strategy: keep system prompt + most recent N messages.
/// </summary>
public sealed class ContextWindowManager
{
    private readonly int _maxTokens;

    public ContextWindowManager(int maxTokens = 4096)
    {
        _maxTokens = maxTokens;
    }

    /// <summary>
    /// Returns a trimmed message list that fits within <see cref="_maxTokens"/>.
    /// Always preserves the system prompt (index 0 if role = System).
    /// </summary>
    public IReadOnlyList<ChatMessage> Trim(IReadOnlyList<ChatMessage> messages, int? maxTokens = null)
    {
        if (messages.Count == 0) return messages;

        int budget = maxTokens ?? _maxTokens;
        var result = new List<ChatMessage>();

        // Always include system prompt
        ChatMessage? systemMsg = null;
        if (messages[0].Role == ChatRole.System)
        {
            systemMsg = messages[0];
            budget -= systemMsg.TokenCount;
        }

        // Walk from the end, adding messages until budget is exhausted
        for (int i = messages.Count - 1; i >= (systemMsg != null ? 1 : 0); i--)
        {
            var msg = messages[i];
            // Persisted messages don't have tokenizer counts. Estimate from UTF-8 text
            // and leave room for ChatML role markers so long follow-up chats don't
            // overflow the native context window.
            int estimatedTokens = Math.Max(8, (System.Text.Encoding.UTF8.GetByteCount(msg.Content) + 3) / 4);
            int cost = Math.Max(msg.TokenCount, estimatedTokens) + 12;
            if (budget - cost < 256) break;         // leave room for response
            budget -= cost;
            result.Insert(0, msg);
        }

        if (systemMsg != null) result.Insert(0, systemMsg);

        // Safety net: always ensure the most recent user message is present.
        // If the budget math dropped it (e.g. a very long code paste), the model
        // would only see the system prompt and respond with a generic greeting.
        var lastUserMsg = messages
            .LastOrDefault(m => m.Role == ChatRole.User || m.Role == ChatRole.Assistant);
        if (lastUserMsg != null && !result.Contains(lastUserMsg))
        {
            // Insert just before any trailing empty assistant placeholder
            int insertAt = result.Count;
            if (insertAt > 0 && result[insertAt - 1].Role == ChatRole.Assistant 
                             && string.IsNullOrEmpty(result[insertAt - 1].Content))
                insertAt--;
            result.Insert(insertAt, lastUserMsg);
        }

        return result;
    }
}
