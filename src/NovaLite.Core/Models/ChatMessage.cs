namespace NovaLite.Core.Models;

/// <summary>Role of a chat participant.</summary>
public enum ChatRole { System, User, Assistant }

/// <summary>A single message in a conversation.</summary>
public sealed class ChatMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ChatRole Role { get; init; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Number of tokens in this message (set after tokenisation).</summary>
    public int TokenCount { get; set; }

    public static ChatMessage FromUser(string content) =>
        new() { Role = ChatRole.User, Content = content };

    public static ChatMessage FromAssistant(string content) =>
        new() { Role = ChatRole.Assistant, Content = content };

    public static ChatMessage SystemPrompt(string content) =>
        new() { Role = ChatRole.System, Content = content };
}
