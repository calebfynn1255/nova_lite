namespace NovaLite.Core.Models;

/// <summary>An active or persisted conversation between the user and the AI.</summary>
public sealed class ConversationSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; set; } = "New Chat";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? ModelPath { get; set; }

    public List<ChatMessage> Messages { get; init; } = [];

    /// <summary>Adds a message and updates the session timestamp.</summary>
    public void AddMessage(ChatMessage message)
    {
        Messages.Add(message);
        UpdatedAt = DateTime.UtcNow;

        // Auto-derive title from first user message
        if (Title == "New Chat" && message.Role == ChatRole.User)
            Title = message.Content.Length > 40
                ? message.Content[..40] + "…"
                : message.Content;
    }
}
