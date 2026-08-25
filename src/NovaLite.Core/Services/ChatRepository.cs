using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NovaLite.Core.Interfaces;
using NovaLite.Core.Models;
using NovaLite.Database;
using NovaLite.Database.Entities;

namespace NovaLite.Core.Services;

public class ChatRepository : IChatRepository
{
    private readonly IDbContextFactory<NovaDbContext> _contextFactory;

    public ChatRepository(IDbContextFactory<NovaDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<List<ChatSessionEntity>> GetAllSessionsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Sessions
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<ChatSessionEntity> CreateSessionAsync(string title)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var session = new ChatSessionEntity
        {
            Id = Guid.NewGuid(),
            Title = title,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        context.Sessions.Add(session);
        await context.SaveChangesAsync();
        return session;
    }

    public async Task DeleteSessionAsync(Guid sessionId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var session = await context.Sessions.FindAsync(sessionId);
        if (session != null)
        {
            context.Sessions.Remove(session);
            await context.SaveChangesAsync();
        }
    }

    public async Task RenameSessionAsync(Guid sessionId, string newTitle)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var session = await context.Sessions.FindAsync(sessionId);
        if (session != null)
        {
            session.Title = newTitle;
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<ChatMessage>> GetMessagesForSessionAsync(Guid sessionId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var entities = await context.Messages
            .Where(m => m.SessionId == sessionId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
            
        return entities.Select(e => new ChatMessage
        {
            Id = e.Id,
            Role = Enum.Parse<ChatRole>(e.Role),
            Content = e.Content,
            Timestamp = e.Timestamp,
            AttachedFileName = e.AttachedFileName,
            AttachedFileSizeDisplay = e.AttachedFileSizeDisplay,
            AttachedFileContent = e.AttachedFileContent
        }).ToList();
    }

    public async Task AddMessageAsync(Guid sessionId, ChatMessage message)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        // Ensure session exists to avoid foreign key failures if session was not persisted
        var session = await context.Sessions.FindAsync(sessionId);
        if (session == null)
        {
            session = new ChatSessionEntity
            {
                Id = sessionId,
                Title = "New Chat",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Sessions.Add(session);
            // Note: do not SaveChanges here; the message will be saved in the same SaveChanges call below.
        }

        session.UpdatedAt = DateTime.UtcNow;
        if (session.Title == "New Chat" && message.Role == ChatRole.User && !string.IsNullOrWhiteSpace(message.Content))
        {
            session.Title = message.Content.Length > 40 ? message.Content[..40] + "…" : message.Content;
        }

        var entity = new ChatMessageEntity
        {
            Id = message.Id != Guid.Empty ? message.Id : Guid.NewGuid(),
            SessionId = sessionId,
            Role = message.Role.ToString(),
            Content = message.Content,
            Timestamp = DateTime.UtcNow,
            AttachedFileName = message.AttachedFileName,
            AttachedFileSizeDisplay = message.AttachedFileSizeDisplay,
            AttachedFileContent = message.AttachedFileContent
        };
        
        context.Messages.Add(entity);
        await context.SaveChangesAsync();
    }

    public async Task UpdateMessageAsync(Guid sessionId, ChatMessage message)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var entity = await context.Messages.FindAsync(message.Id);
        if (entity == null)
        {
            // If the entity can't be found, fallback to adding as new
            await AddMessageAsync(sessionId, message);
            return;
        }

        entity.Content = message.Content;
        entity.AttachedFileName = message.AttachedFileName;
        entity.AttachedFileSizeDisplay = message.AttachedFileSizeDisplay;
        entity.AttachedFileContent = message.AttachedFileContent;
        entity.Timestamp = DateTime.UtcNow;

        await context.SaveChangesAsync();
    }
}
