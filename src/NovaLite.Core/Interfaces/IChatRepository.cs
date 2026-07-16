using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NovaLite.Database.Entities;
using NovaLite.Core.Models;

namespace NovaLite.Core.Interfaces;

public interface IChatRepository
{
    Task<List<ChatSessionEntity>> GetAllSessionsAsync();
    Task<ChatSessionEntity> CreateSessionAsync(string title);
    Task DeleteSessionAsync(Guid sessionId);
    Task RenameSessionAsync(Guid sessionId, string newTitle);
    
    Task<List<ChatMessage>> GetMessagesForSessionAsync(Guid sessionId);
    Task AddMessageAsync(Guid sessionId, ChatMessage message);
}
