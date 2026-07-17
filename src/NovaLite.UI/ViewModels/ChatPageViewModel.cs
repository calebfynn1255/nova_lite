using System.Threading.Tasks;
using System.Threading;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using NovaLite.Core.Models;
using NovaLite.Database.Entities;
using System.Linq;

namespace NovaLite.UI.ViewModels;

public partial class ChatPageViewModel : ObservableObject
{
    private readonly NovaLite.Core.Services.ConversationService _conversationService;
    private readonly NovaLite.Core.Interfaces.IChatRepository _chatRepo;

    [ObservableProperty] private string _activeModelName = "No model loaded";
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private bool _hasMessages;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<ChatSessionEntity> ChatSessions { get; } = [];

    [ObservableProperty] private ChatSessionEntity? _selectedSession;

    public event Action? ChatUpdated;

    private CancellationTokenSource? _cts;

    public ChatPageViewModel(NovaLite.Core.Services.ConversationService conversationService)
    {
        _conversationService = conversationService;
        _chatRepo = App.ChatRepository;
    }
    
    public void SyncSessions(System.Collections.Generic.IEnumerable<ChatSessionEntity> sessions)
    {
        ChatSessions.Clear();
        foreach (var s in sessions)
            ChatSessions.Add(s);
    }

    public async Task LoadSessionAsync(Guid sessionId)
    {
        await _conversationService.SetActiveSessionAsync(sessionId);
        
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Messages.Clear();
            DateTime? lastDate = null;
            foreach (var msg in _conversationService.Session.Messages)
            {
                if (lastDate == null || lastDate.Value.Date != msg.Timestamp.Date)
                {
                    Messages.Add(new ChatMessageViewModel
                    {
                        IsSeparator = true,
                        SeparatorText = msg.Timestamp.ToString("d MMMM yyyy"),
                        Timestamp = msg.Timestamp
                    });
                }
                lastDate = msg.Timestamp;

                Messages.Add(new ChatMessageViewModel
                {
                    Content = msg.Content,
                    IsUser = msg.Role == ChatRole.User,
                    Timestamp = msg.Timestamp // Used msg.Timestamp instead of DateTime.Now
                });
            }
            HasMessages = Messages.Any();
        });
    }

    public async Task StartNewChatAsync()
    {
        await _conversationService.StartNewSessionAsync("New Chat");
        ClearMessages();
    }

    public void ClearMessages()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Messages.Clear();
            HasMessages = false;
        });
    }

    partial void OnInputTextChanged(string value)
    {
        SendCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSend), FlowExceptionsToTaskScheduler = false)]
    private async Task Send()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;

        var userText = InputText.Trim();
        InputText = string.Empty;

        var now = DateTime.Now;
        var lastMsg = Messages.LastOrDefault(m => !m.IsSeparator);
        if (lastMsg == null || lastMsg.Timestamp.Date != now.Date)
        {
            Messages.Add(new ChatMessageViewModel
            {
                IsSeparator = true,
                SeparatorText = now.ToString("d MMMM yyyy"),
                Timestamp = now
            });
        }

        // Add user message immediately so the UI feels responsive
        Messages.Add(new ChatMessageViewModel
        {
            Content = userText,
            IsUser = true,
            Timestamp = now
        });
        HasMessages = true;
        ChatUpdated?.Invoke(); // scroll to bottom

        // IsGenerating shows the TypingIndicator — no blank bubble until first token
        IsGenerating = true;
        _cts = new CancellationTokenSource();

        ChatMessageViewModel? aiMsg = null;

        try
        {
            // Yield once to let Avalonia render the user message + typing indicator
            // before the heavy CPU work (tokenization/decode) begins on the background thread.
            await Task.Yield();

            await foreach (var token in _conversationService.SendAsync(userText, null, _cts.Token))
            {
                if (aiMsg == null)
                {
                    // First token — swap typing indicator for real bubble
                    aiMsg = new ChatMessageViewModel
                    {
                        Content = token,
                        IsUser = false,
                        IsStreaming = true,
                        Timestamp = DateTime.Now
                    };
                    Messages.Add(aiMsg);
                    IsGenerating = false;
                }
                else
                {
                    aiMsg.Content += token;
                }
            }

            if (aiMsg != null)
                aiMsg.IsStreaming = false;
            else
            {
                Messages.Add(new ChatMessageViewModel
                {
                    Content = "*(no response)*",
                    IsUser = false,
                    Timestamp = DateTime.Now
                });
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No model loaded"))
        {
            Messages.Add(new ChatMessageViewModel
            {
                Content = "> ⚠️ No model loaded. Go to **Models** to load a GGUF file.",
                IsUser = false,
                Timestamp = DateTime.Now
            });
        }
        catch (OperationCanceledException)
        {
            if (aiMsg != null)
            {
                aiMsg.Content += " *(cancelled)*";
                aiMsg.IsStreaming = false;
            }
        }
        catch (Exception ex)
        {
            Messages.Add(new ChatMessageViewModel
            {
                Content = $"> ❌ Error: {ex.Message}",
                IsUser = false,
                Timestamp = DateTime.Now
            });
            Serilog.Log.Error(ex, "Chat inference failed");
        }
        finally
        {
            IsGenerating = false;
            if (aiMsg != null) aiMsg.IsStreaming = false;
            _cts?.Dispose();
            _cts = null;
            ChatUpdated?.Invoke();
        }
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(InputText) && !IsGenerating;

    [RelayCommand]
    private void StopGeneration()
    {
        _cts?.Cancel();
    }
}

public partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty] private string _content = string.Empty;
    [ObservableProperty] private bool _isStreaming;
    public bool IsUser { get; init; }
    public DateTime Timestamp { get; init; }
    public string AuthorLabel => IsUser ? "You" : "Nova";

    public bool IsSeparator { get; init; }
    public bool IsUserMessage => IsUser && !IsSeparator;
    public bool IsAiMessage => !IsUser && !IsSeparator;
    public string SeparatorText { get; init; } = string.Empty;

    [RelayCommand]
    private async Task CopyText()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var clipboard = desktop.MainWindow?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(Content);
        }
    }
}
