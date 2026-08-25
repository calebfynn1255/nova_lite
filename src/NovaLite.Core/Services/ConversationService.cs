using NovaLite.Core.Interfaces;
using NovaLite.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    private readonly FileCommandService _fileCommandService;

    public ConversationSession Session { get; private set; } = new();
    public Guid? ActiveSessionId { get; private set; }

    public ConversationService(
        IInferenceProvider provider,
        IChatRepository chatRepo,
        IMemoryService memoryService,
        ILogger<ConversationService> logger,
        FileCommandService fileCommandService,
        int maxContextTokens = 4096)
    {
        _provider = provider;
        _chatRepo = chatRepo;
        _memoryService = memoryService;
        _logger = logger;
        _fileCommandService = fileCommandService;
        _contextWindow = new Memory.ContextWindowManager(maxContextTokens);
    }

    /// <summary>
    /// Enable or disable PC access for file commands handled by the conversation.
    /// </summary>
    public void SetPcAccessEnabled(bool enabled) => _fileCommandService.SetEnabled(enabled);

    public void SetWorkspace(string? path) => _fileCommandService.WorkspaceDirectory = path;

    /// <summary>
    /// Whether PC access is currently enabled.
    /// </summary>
    public bool IsPcAccessEnabled => _fileCommandService.IsEnabled;

    public async Task SetActiveSessionAsync(Guid sessionId)
    {
        ActiveSessionId = sessionId;
        Session = new ConversationSession();
        var historicalMessages = await _chatRepo.GetMessagesForSessionAsync(sessionId);
        foreach (var msg in historicalMessages)
        {
            Session.AddMessage(msg);
        }

        // Lightweight diagnostic log to help trace resume issues
        try
        {
            var count = historicalMessages?.Count ?? 0;
            _logger.LogInformation("SetActiveSessionAsync: loaded session {SessionId} with {Count} messages", sessionId, count);
        }
        catch { }
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
        string? attachedFileName = null,
        string? attachedFileSizeDisplay = null,
        string? attachedFileContent = null,
        InferenceOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct = default)
    {
        if (ActiveSessionId == null)
        {
            await StartNewSessionAsync("New Chat");
        }
        // Avoid duplicate user messages: if the last session message matches this content
        // and is very recent, reuse it instead of adding a new one.
        var userMsg = ChatMessage.FromUser(userText);
        userMsg.AttachedFileName = attachedFileName;
        userMsg.AttachedFileSizeDisplay = attachedFileSizeDisplay;
        userMsg.AttachedFileContent = attachedFileContent;

        var lastMsg = Session.Messages.LastOrDefault();
        if (lastMsg != null && lastMsg.Role == ChatRole.User &&
            string.Equals(lastMsg.Content, userText, StringComparison.Ordinal) &&
            (DateTime.UtcNow - lastMsg.Timestamp).TotalSeconds < 3)
        {
            // Reuse the recent identical message — update attachments if needed
            lastMsg.AttachedFileName = attachedFileName;
            lastMsg.AttachedFileSizeDisplay = attachedFileSizeDisplay;
            lastMsg.AttachedFileContent = attachedFileContent;
            userMsg = lastMsg;
        }
        else
        {
            Session.AddMessage(userMsg);
        }
        
        // Persist user message
        if (ActiveSessionId.HasValue)
        {
            await _chatRepo.AddMessageAsync(ActiveSessionId.Value, userMsg);
            // Fire-and-forget memory extraction — never let it crash the app
            _ = _memoryService.ExtractMemoriesFromChatAsync(userText)
                .ContinueWith(t => _logger.LogWarning("Memory extraction failed: {Err}", t.Exception?.Message),
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }

        var (handled, commandResponse) = await _fileCommandService.TryHandleCommandAsync(userText);
        if (handled)
        {
            var commandResponseMsg = ChatMessage.FromAssistant(commandResponse);
            Session.AddMessage(commandResponseMsg);
            if (ActiveSessionId.HasValue)
            {
                await _chatRepo.AddMessageAsync(ActiveSessionId.Value, commandResponseMsg);
            }

            yield return commandResponse;
            yield break;
        }

        if (ResponseReliabilityGuard.TryCreateResponse(userText, out var reliabilityResponse))
        {
            var reliabilityMessage = ChatMessage.FromAssistant(reliabilityResponse);
            Session.AddMessage(reliabilityMessage);
            if (ActiveSessionId.HasValue)
                await _chatRepo.AddMessageAsync(ActiveSessionId.Value, reliabilityMessage);

            yield return reliabilityResponse;
            yield break;
        }

        var allFacts = await _memoryService.GetAllFactsAsync();

        // Build system prompt. Clearly separate assistant identity (Nova) from user identity.
        // Inject any stored facts about the user so the model can reference them.
        var systemPrompt = new System.Text.StringBuilder();
        
        systemPrompt.AppendLine("You are Nova Enterprise, a secure, 100% offline corporate knowledge-work and productivity AI assistant designed for small and medium enterprises (SMEs), business operators, and professionals.");
        systemPrompt.AppendLine("You specialize in document analysis (PDFs, contracts, invoices, agreements), financial/spreadsheet data analysis (XLSX, CSV), executive business communications, and local task automation.");
        systemPrompt.AppendLine("Accuracy is paramount for business operations: never invent financial figures, legal clauses, product specifications, prices, exchange rates, availability, dates, compatibility, or capabilities. If a fact or number is uncertain, state so plainly rather than guessing.");
        systemPrompt.AppendLine("Use only the recent conversation to resolve abbreviations or context. Reply concisely, professionally, and accurately. Use clean Markdown formatting: **bold** key terms, '- ' for bullet points, and '1. ' for numbered steps. Never print chat-template or control tokens.");

        if (IsPcAccessEnabled)
        {
            systemPrompt.AppendLine("\n[PC CONTROL & TERMINAL ACCESS — CRITICAL RULES]");
            systemPrompt.AppendLine("PC Access is ENABLED on the user's Windows machine.");
            systemPrompt.AppendLine("You can execute Windows commands (sfc /scannow, DISM, ipconfig, chkdsk, ping, systeminfo, mkdir, etc.).");
            systemPrompt.AppendLine("MANDATORY: When the user asks you to perform ANY PC action (create folder, create file, run command, delete, move, etc.), you MUST output the exact command inside a ```cmd code block.");
            systemPrompt.AppendLine("Example: To create a folder named '234' on the desktop, output:\n```cmd\nmkdir \"%USERPROFILE%\\Desktop\\234\"\n```");
            systemPrompt.AppendLine("NEVER say 'I created the folder' or 'Done' without outputting a ```cmd code block. The system executes ONLY what is inside ```cmd blocks.");
            systemPrompt.AppendLine("If you cannot determine what the user wants, ask for clarification. Do NOT guess or hallucinate actions.");
        }

        // A linked workspace is only context when the user explicitly refers to it.
        // Casual chat and standalone questions must not be burdened with project files.
        if (!string.IsNullOrWhiteSpace(_fileCommandService.WorkspaceDirectory) &&
            IsWorkspaceContextRequested(userText))
        {
            systemPrompt.AppendLine($"\n[LINKED WORKSPACE]");
            systemPrompt.AppendLine($"The user has linked the following folder as their active workspace: {_fileCommandService.WorkspaceDirectory}");
            systemPrompt.AppendLine("The contents of their workspace files are provided below. When the user asks about their code, reference these file contents directly. When they ask you to fix something, show the corrected code from the actual files.");

            if (IsPcAccessEnabled)
            {
                systemPrompt.AppendLine("You can also execute terminal commands relative to this folder to read, modify, or manage its contents.");
            }

            // Proactively inject workspace file listing and small file contents
            try
            {
                var wsDir = _fileCommandService.WorkspaceDirectory;
                var codeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".cs", ".js", ".ts", ".tsx", ".jsx", ".py", ".java", ".cpp", ".c", ".h", ".hpp",
                    ".rs", ".go", ".php", ".html", ".css", ".json", ".xml", ".yaml", ".yml",
                    ".md", ".txt", ".toml", ".cfg", ".ini", ".env", ".sh", ".bat", ".ps1",
                    ".rb", ".swift", ".kt", ".scala", ".lua", ".sql", ".r", ".m", ".vue", ".svelte"
                };

                var allFiles = System.IO.Directory.GetFiles(wsDir, "*", System.IO.SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{System.IO.Path.DirectorySeparatorChar}.", StringComparison.Ordinal)) // skip hidden dirs like .git
                    .Where(f => !f.Contains("node_modules", StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Contains("bin" + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .Where(f => !f.Contains("obj" + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // File listing
                systemPrompt.AppendLine("\nWorkspace files:");
                foreach (var file in allFiles.Take(50))
                {
                    var relativePath = System.IO.Path.GetRelativePath(wsDir, file);
                    systemPrompt.AppendLine($"  - {relativePath}");
                }
                if (allFiles.Count > 50)
                    systemPrompt.AppendLine($"  ... and {allFiles.Count - 50} more files");

                // Inject contents of small code files (up to ~40KB total to stay within context budget)
                int totalCharsInjected = 0;
                const int maxTotalChars = 40000;
                const int maxSingleFileChars = 8000;

                var codeFiles = allFiles
                    .Where(f => codeExtensions.Contains(System.IO.Path.GetExtension(f)))
                    .OrderBy(f => new System.IO.FileInfo(f).Length) // smallest first
                    .ToList();

                foreach (var file in codeFiles)
                {
                    if (totalCharsInjected >= maxTotalChars) break;

                    var info = new System.IO.FileInfo(file);
                    if (info.Length > maxSingleFileChars * 2) continue; // skip large files (rough byte estimate)

                    var content = System.IO.File.ReadAllText(file);
                    if (content.Length > maxSingleFileChars)
                        content = content[..maxSingleFileChars] + "\n... (truncated)";

                    var relativePath = System.IO.Path.GetRelativePath(wsDir, file);
                    systemPrompt.AppendLine($"\n--- File: {relativePath} ---");
                    systemPrompt.AppendLine(content);
                    totalCharsInjected += content.Length;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to scan workspace for context injection: {Err}", ex.Message);
            }
        }

        if (allFacts.Count > 0)
        {
            systemPrompt.AppendLine("\nUser facts (use only when directly relevant; never bring them up unprompted):");
            foreach (var fact in allFacts)
                systemPrompt.AppendLine($"- {fact.Fact}");
        }

        // If the last persisted message is an assistant placeholder with empty content,
        // reuse it instead of inserting a new one to avoid duplicates and UI glitches on resume.
        ChatMessage assistantMsg;
        var last = Session.Messages.LastOrDefault();
        if (last != null && last.Role == ChatRole.Assistant && string.IsNullOrEmpty(last.Content))
        {
            assistantMsg = last;
        }
        else
        {
            assistantMsg = ChatMessage.FromAssistant(string.Empty);
            Session.AddMessage(assistantMsg);
        }

        // Keep the prompt comfortably below the model's native context. The remaining
        // space is reserved for the assistant's response.
        var trimmed = _contextWindow.Trim(Session.Messages, maxTokens: 8000);
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

        _fileCommandService.SetLastAssistantResponse(assistantMsg.Content);

        if (_fileCommandService.IsEnabled)
        {
            var autoResult = await _fileCommandService.TryExecuteAssistantCommandAsync(assistantMsg.Content);
            if (!string.IsNullOrWhiteSpace(autoResult))
            {
                var appendText = "\n\n" + autoResult;
                assistantMsg.Content += appendText;
                yield return appendText;
            }
        }

        // Persist assistant message (update if it was an existing placeholder)
        if (ActiveSessionId.HasValue)
        {
            if (Session.Messages.Any(m => m.Id == assistantMsg.Id))
            {
                // If the assistant message already exists in DB (was loaded), update it
                await _chatRepo.UpdateMessageAsync(ActiveSessionId.Value, assistantMsg);
            }
            else
            {
                await _chatRepo.AddMessageAsync(ActiveSessionId.Value, assistantMsg);
            }
        }

        _logger.LogDebug("Turn complete. Response tokens ≈ {Count}", sb.Length / 4);
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    private static bool IsWorkspaceContextRequested(string userText) =>
        !string.IsNullOrWhiteSpace(userText) &&
        Regex.IsMatch(
            userText,
            @"\b(?:workspace|codebase|repository|repo|solution|linked folder|(?:this|my|the)\s+(?:project|files?|file)|in\s+(?:my|the)\s+(?:project|workspace|repo))\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
