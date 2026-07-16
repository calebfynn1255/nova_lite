using System;

namespace NovaLite.Database.Entities;

public class ChatMessageEntity
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public ChatSessionEntity Session { get; set; } = null!;
    
    // Core Role Enum will be converted to string or int in EF
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
