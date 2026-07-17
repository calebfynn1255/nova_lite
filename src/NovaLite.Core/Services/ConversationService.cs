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
            // Fire-and-forget memory extraction — never let it crash the app
            _ = _memoryService.ExtractMemoriesFromChatAsync(userText)
                .ContinueWith(t => _logger.LogWarning("Memory extraction failed: {Err}", t.Exception?.Message),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        var allFacts = await _memoryService.GetAllFactsAsync();

        // Build system prompt. Clearly separate assistant identity (Nova) from user identity.
        // Inject any stored facts about the user so the model can reference them.
        var systemPrompt = new System.Text.StringBuilder();
        systemPrompt.AppendLine("You are Nova, a helpful AI assistant. Your name is Nova — this is YOUR name, not the user's name.");
        systemPrompt.AppendLine("You do NOT know the user's personal details (such as their name, age, or preferences) unless they are listed below.");
        systemPrompt.AppendLine("If the user asks about their own personal details and you have no stored information about it, honestly say you don't know and ask them to tell you.");
        systemPrompt.AppendLine("When the user tells you personal information (like their name), remember and use it.");

        if (allFacts.Count > 0)
        {
            systemPrompt.AppendLine("\nWhat you know about this user:");
            foreach (var fact in allFacts)
                systemPrompt.AppendLine($"- {fact.Fact}");
        }

        var assistantMsg = ChatMessage.FromAssistant(string.Empty);
        Session.AddMessage(assistantMsg);

        // Keep the prompt comfortably below the 2K native context. The remaining
        // space is reserved for formatting and the assistant's response.
        var trimmed = _contextWindow.Trim(Session.Messages, maxTokens: 1100);
        var messages = new List<ChatMessage> { ChatMessage.SystemPrompt(systemPrompt.ToString()) };
        messages.AddRange(trimmed);
        
        var sb = new System.Text.StringBuilder();

        await foreach (var token in _provider.InferStreamAsync(
                           messages, options ?? InferenceOptions.Default, ct))
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
