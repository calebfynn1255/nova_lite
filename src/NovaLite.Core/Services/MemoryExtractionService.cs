using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NovaLite.Core.Interfaces;
using NovaLite.Database;
using NovaLite.Database.Entities;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace NovaLite.Core.Services;

public class MemoryExtractionService : IMemoryService
{
    private readonly IDbContextFactory<NovaDbContext> _contextFactory;
    private readonly ILogger<MemoryExtractionService> _logger;

    public MemoryExtractionService(IDbContextFactory<NovaDbContext> contextFactory, ILogger<MemoryExtractionService> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<List<UserFactEntity>> GetAllFactsAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.UserFacts.ToListAsync();
    }

    public async Task AddFactAsync(string fact)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var entity = new UserFactEntity
        {
            Id = Guid.NewGuid(),
            Fact = fact,
            ExtractedAt = DateTime.UtcNow
        };
        context.UserFacts.Add(entity);
        await context.SaveChangesAsync();
        _logger.LogInformation("Stored new user fact: {Fact}", fact);
    }

    public async Task ExtractMemoriesFromChatAsync(string messageContent)
    {
        // For milestone 1, we use basic regex to extract obvious facts without blocking inference.
        // In the future, this could queue a background prompt to a smaller LLM for extraction.
        
        var nameMatch = Regex.Match(messageContent, @"(?i)(my name is|i am) ([\w]+)");
        if (nameMatch.Success)
        {
            await AddFactAsync($"User's name is {nameMatch.Groups[2].Value}");
        }
        
        var likeMatch = Regex.Match(messageContent, @"(?i)(i like|i love) ([\w\s]+)");
        if (likeMatch.Success)
        {
            await AddFactAsync($"User likes {likeMatch.Groups[2].Value.Trim()}");
        }
    }
}
