using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using NovaLite.Core.Files;
using NovaLite.Core.Models;
using NovaLite.Core.Settings;

namespace NovaLite.UI.ViewModels;

public partial class ModelManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _scanDirectory = string.Empty;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] 
    [NotifyCanExecuteChangedFor(nameof(LoadSelectedCommand))]
    private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] 
    [NotifyCanExecuteChangedFor(nameof(LoadSelectedCommand))]
    [NotifyPropertyChangedFor(nameof(LoadButtonText))]
    private ModelInfoViewModel? _selectedModel;

    public ObservableCollection<ModelInfoViewModel> Models { get; } = [];

    /// <summary>Called after a model is successfully loaded. UI can hook this to update the header label.</summary>
    public Action<string>? OnModelLoaded { get; set; }

    private readonly ModelFileScanner _scanner = new();

    public ModelManagerViewModel()
    {
        ScanDirectory = AppSettings.Load().ModelDirectory;
        if (!string.IsNullOrEmpty(ScanDirectory))
            _ = ScanAsync();
    }

    [RelayCommand]
    private async Task BrowseDirectory()
    {
        // Directory picking is done via Avalonia's OpenFolderDialog in the code-behind
        // The View calls this and passes the selected path
        await Task.CompletedTask;
    }

    public async Task SetDirectoryAndScan(string path)
    {
        ScanDirectory = path;
        var s = AppSettings.Load();
        s.ModelDirectory = path;
        s.Save();
        await ScanAsync();
    }

    [RelayCommand]
    private async Task Scan() => await ScanAsync();

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(ScanDirectory)) return;
        IsScanning = true;
        StatusMessage = "Scanning…";
        Models.Clear();
        try
        {
            var found = await Task.Run(() =>
                _scanner.Scan(ScanDirectory).ToList());

            var lastLoaded = AppSettings.Load().LastModelPath;

            foreach (var m in found)
            {
                var vm = new ModelInfoViewModel(m);
                if (!string.IsNullOrEmpty(lastLoaded) &&
                    string.Equals(m.FilePath, lastLoaded, StringComparison.OrdinalIgnoreCase))
                {
                    vm.IsLoaded = true;
                }
                Models.Add(vm);
            }

            StatusMessage = $"{found.Count} model{(found.Count == 1 ? "" : "s")} found";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
        }
        finally { IsScanning = false; }
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task LoadSelected()
    {
        if (SelectedModel is null) return;
        IsLoading = true;
        StatusMessage = $"Loading {SelectedModel.FileName}…";
        try
        {
            // Unload existing
            await App.Provider.UnloadAsync();

            // Load via loader (hardcoding GGUF loader for Milestone 1)
            var loadedModel = await App.GgufLoader.LoadAsync(SelectedModel.FilePath);
            
            // Set as active model in provider
            await App.Provider.LoadAsync(loadedModel);
            
            foreach (var m in Models) m.IsLoaded = false;
            SelectedModel.IsLoaded = true;
            OnPropertyChanged(nameof(LoadButtonText));
            LoadSelectedCommand.NotifyCanExecuteChanged();
            
            StatusMessage = $"Loaded: {SelectedModel.FileName}";
            
            // Persist last model path for auto-load on next startup
            var s = AppSettings.Load();
            s.LastModelPath = SelectedModel.FilePath;
            s.Save();
            
            OnModelLoaded?.Invoke(SelectedModel.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load error: {ex.Message}";
        }
        finally { IsLoading = false; OnPropertyChanged(nameof(LoadButtonText)); }
    }

    private bool CanLoad() => SelectedModel is not null && !IsLoading && !SelectedModel.IsLoaded;

    public string LoadButtonText
    {
        get
        {
            if (IsLoading) return "Loading…";
            if (SelectedModel?.IsLoaded == true) return "Loaded";
            return "Load Model";
        }
    }
}

public partial class ModelInfoViewModel : ObservableObject
{
    private readonly ModelInfo _info;
    [ObservableProperty] private bool _isLoaded;

    public string FilePath    => _info.FilePath;
    public string FileName    => _info.FileName;
    public string Format      => _info.Format;
    public string SizeDisplay => _info.FileSizeDisplay;

    public ModelInfoViewModel(ModelInfo info) => _info = info;
}
