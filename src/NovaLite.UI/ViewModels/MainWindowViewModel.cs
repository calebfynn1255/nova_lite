using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLite.Core.Settings;
using NovaLite.Database.Entities;
using NovaLite.UI.Themes;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NovaLite.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _activeModelLabel = "No model loaded";
    [ObservableProperty] private bool _isChatActive;
    [ObservableProperty] private bool _isModelsActive;
    [ObservableProperty] private bool _isSettingsActive;
    [ObservableProperty] private bool _isAboutActive;

    [ObservableProperty] private ChatSessionEntity? _selectedChatSession;

    public ObservableCollection<ChatSessionEntity> ChatSessions { get; } = [];

    // Shared chat ViewModel (kept alive so conversation persists on nav)
    private readonly ChatPageViewModel _chatVm = new(App.Conversation);
    private readonly ModelManagerViewModel _modelsVm = new();
    private readonly SettingsViewModel _settingsVm;
    private readonly AboutViewModel _aboutVm = new();

    public MainWindowViewModel()
    {
        _settingsVm = new SettingsViewModel(this);
        _modelsVm.OnModelLoaded = name => SetActiveModel(name);
        
        _chatVm.ChatUpdated += () => 
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () => await RefreshChatSessionsAsync());
        };

        NavigateChat();
        
        // Run entirely on background thread — EF Core model compilation on first query
        // is synchronous CPU-bound work that blocks the calling thread. Task.Run ensures
        // it NEVER touches the Avalonia UI event loop.
        Task.Run(InitializeAsync);
        AutoLoadModelBackground();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // This runs entirely on a thread pool thread.
            // EF Core model compilation happens here, off the UI thread.
            var sessions = await App.ChatRepository.GetAllSessionsAsync();

            // Marshal UI updates back to the UI thread
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ChatSessions.Clear();
                foreach (var s in sessions)
                    ChatSessions.Add(s);
                _chatVm.SyncSessions(sessions);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Session load error: {ex}");
        }

        // Avoid eager model loading on startup. The model is loaded only when the user
        // explicitly chooses to use it from the models page or chat flow.
    }

    private void AutoLoadModelBackground()
    {
        Task.Run(async () =>
        {
            var settings = AppSettings.Load();
            var pathToLoad = settings.GetAutoLoadModelPath();

            if (pathToLoad == null) return;

            try
            {
                var loadedModel = await App.GgufLoader.LoadAsync(pathToLoad);
                await App.Provider.LoadAsync(loadedModel);
                
                var modelName = Path.GetFileName(pathToLoad);
                
                // Must post back to UI thread since we're on a background Task.Run
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SetActiveModel(modelName);
                    settings.LastModelPath = pathToLoad;
                    settings.Save();
                });
            }
            catch
            {
                // Best-effort
            }
        });
    }

    private async Task RefreshChatSessionsAsync()
    {
        var currentSelectedId = SelectedChatSession?.Id;
        var sessions = await App.ChatRepository.GetAllSessionsAsync();
        
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            ChatSessions.Clear();
            foreach (var s in sessions)
                ChatSessions.Add(s);
            _chatVm.SyncSessions(sessions);
            
            if (currentSelectedId != null)
            {
                SelectedChatSession = ChatSessions.FirstOrDefault(s => s.Id == currentSelectedId.Value);
            }
        });
    }

    async partial void OnSelectedChatSessionChanged(ChatSessionEntity? value)
    {
        if (value == null) return;
        await _chatVm.LoadSessionAsync(value.Id);
        NavigateChat();
    }

    [RelayCommand]
    private async Task NewChat()
    {
        if (!_chatVm.HasMessages && SelectedChatSession?.Title == "New Chat") return;
        
        await _chatVm.StartNewChatAsync();
        await RefreshChatSessionsAsync();
        SelectedChatSession = ChatSessions.FirstOrDefault();
        NavigateChat();
    }

    [RelayCommand]
    private async Task DeleteChatSession(ChatSessionEntity session)
    {
        if (session == null) return;
        await App.ChatRepository.DeleteSessionAsync(session.Id);
        await RefreshChatSessionsAsync();
        if (SelectedChatSession?.Id == session.Id)
        {
            SelectedChatSession = null;
            _chatVm.ClearMessages();
        }
    }

    [RelayCommand]
    private void BeginRenameChatSession(ChatSessionEntity session)
    {
        if (session == null) return;
        session.IsEditing = true;
    }

    [RelayCommand]
    private async Task EndRenameChatSession(ChatSessionEntity session)
    {
        if (session == null) return;
        session.IsEditing = false;
        await App.ChatRepository.RenameSessionAsync(session.Id, session.Title);
        await RefreshChatSessionsAsync();
    }

    [RelayCommand]
    public void NavigateWelcome()
    {
        CurrentPage = _chatVm;
        SetActive(chat: true);
    }

    [RelayCommand]
    public void NavigateChat()
    {
        CurrentPage = _chatVm;
        SetActive(chat: true);
    }

    [RelayCommand]
    public void NavigateModels()
    {
        CurrentPage = _modelsVm;
        SetActive(models: true);
    }

    [RelayCommand]
    public void NavigateSettings()
    {
        CurrentPage = _settingsVm;
        SetActive(settings: true);
    }

    [RelayCommand]
    public void NavigateAbout()
    {
        CurrentPage = _aboutVm;
        SetActive(about: true);
    }

    public void SetActiveModel(string modelName)
    {
        ActiveModelLabel = modelName;
        _chatVm.ActiveModelName = modelName;
    }

    private void SetActive(bool chat = false, bool models = false,
                           bool settings = false, bool about = false)
    {
        IsChatActive     = chat;
        IsModelsActive   = models;
        IsSettingsActive = settings;
        IsAboutActive    = about;
    }
}
