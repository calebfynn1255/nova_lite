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
    public IReadOnlyList<ChatMessage> Trim(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0) return messages;

        var result = new List<ChatMessage>();
        int budget = _maxTokens;

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
            int cost = Math.Max(msg.TokenCount, 8); // floor estimate
            if (budget - cost < 256) break;         // leave room for response
            budget -= cost;
            result.Insert(0, msg);
        }

        if (systemMsg != null) result.Insert(0, systemMsg);
        return result;
    }
}
