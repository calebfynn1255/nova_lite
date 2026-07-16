using NovaLite.Core.Interfaces;
using NovaLite.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NovaLite.Core.Services;

/// <summary>
/// Orchestrates conversation flow: maintains the active session,
/// delegates inference to the <see cref="IInferenceProvider"/>, and
/// handles context window trimming via <see cref="Memory.ContextWindowManager"/>.
/// </summary>
public sealed class ConversationService : IAsyncDisposable
{
    private readonly IInferenceProvider _provider;
    private readonly IChatRepository _chatRepo;
    private readonly IMemoryService _memoryService;
    private readonly Memory.ContextWindowManager _contextWindow;
    private readonly ILogger<ConversationService> _logger;

    public ConversationSession Session { get; private set; } = new();
    public Guid? ActiveSessionId { get; private set; }

    public ConversationService(
        IInferenceProvider provider,
        IChatRepository chatRepo,
        IMemoryService memoryService,
        ILogger<ConversationService> logger,
        int maxContextTokens = 4096)
    {
        _provider = provider;
        _chatRepo = chatRepo;
        _memoryService = memoryService;
        _logger = logger;
        _contextWindow = new Memory.ContextWindowManager(maxContextTokens);
    }

    public async Task SetActiveSessionAsync(Guid sessionId)
    {
        ActiveSessionId = sessionId;
        Session = new ConversationSession();
        var historicalMessages = await _chatRepo.GetMessagesForSessionAsync(sessionId);
        foreach (var msg in historicalMessages)
        {
            Session.AddMessage(msg);
        }
    }

    public async Task<Guid> StartNewSessionAsync(string title)
    {
        var session = await _chatRepo.CreateSessionAsync(title);
        ActiveSessionId = session.Id;
        Session = new ConversationSession();
        return session.Id;
    }

    public async IAsyncEnumerable<string> SendAsync(
        string userText,
        InferenceOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
    {
        if (ActiveSessionId == null)
        {
            await StartNewSessionAsync("New Chat");
        }

        var userMsg = ChatMessage.FromUser(userText);
        Session.AddMessage(userMsg);
        
        // Persist user message
        if (ActiveSessionId.HasValue)
        {
            await _chatRepo.AddMessageAsync(ActiveSessionId.Value, userMsg);
            // Fire and forget memory extraction
            _ = _memoryService.ExtractMemoriesFromChatAsync(userText);
        }

        var assistantMsg = ChatMessage.FromAssistant(string.Empty);
        Session.AddMessage(assistantMsg);

        // Feed memory context to prompt if necessary
        // For milestone 1, we just prepend facts to the system prompt if we were using a system role.
        var allFacts = await _memoryService.GetAllFactsAsync();
        string systemContext = string.Join("\n", allFacts.Select(f => f.Fact));
        
        // In LocalInferenceProvider, we could inject this system context, but for now we'll stick to the basic prompt loop.

        var trimmed = _contextWindow.Trim(Session.Messages);
        var sb = new System.Text.StringBuilder();

        await foreach (var token in _provider.InferStreamAsync(
                           trimmed, options ?? InferenceOptions.Default, ct))
        {
            sb.Append(token);
            assistantMsg.Content = sb.ToString();
            yield return token;
        }

        // Persist assistant message
        if (ActiveSessionId.HasValue)
        {
            await _chatRepo.AddMessageAsync(ActiveSessionId.Value, assistantMsg);
        }

        _logger.LogDebug("Turn complete. Response tokens ≈ {Count}", sb.Length / 4);
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();
}
