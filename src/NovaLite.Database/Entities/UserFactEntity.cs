using System;

namespace NovaLite.Database.Entities;

public class UserFactEntity
{
    public Guid Id { get; set; }
    
    /// <summary>
    /// The extracted fact (e.g. "User is a programmer", "User's name is John")
    /// </summary>
    public string Fact { get; set; } = string.Empty;
    
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
}
