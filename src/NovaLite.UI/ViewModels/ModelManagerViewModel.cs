using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using NovaLite.Core.Files;
using NovaLite.Core.Models;
using NovaLite.Core.Settings;
using NovaLite.Setup;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NovaLite.UI.ViewModels;

public partial class ModelManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _scanDirectory = string.Empty;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] 
    [NotifyCanExecuteChangedFor(nameof(LoadSelectedCommand))]
    private bool _isLoading;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isRecommendedDownloadRunning;
    [ObservableProperty] private bool _isRecommendedDownloadPaused;
    [ObservableProperty] private double _recommendedDownloadProgress;
    [ObservableProperty] private string _recommendedDownloadStatus = string.Empty;
    [ObservableProperty] private RecommendedModel? _activeRecommendedModel;
    [ObservableProperty] 
    [NotifyCanExecuteChangedFor(nameof(LoadSelectedCommand))]
    [NotifyPropertyChangedFor(nameof(LoadButtonText))]
    private ModelInfoViewModel? _selectedModel;

    public ObservableCollection<ModelInfoViewModel> Models { get; } = [];
    public ObservableCollection<RecommendedModelViewModel> RecommendedDownloads { get; } = [];

    /// <summary>Called after a model is successfully loaded. UI can hook this to update the header label.</summary>
    public Action<string>? OnModelLoaded { get; set; }

    private readonly ModelFileScanner _scanner = new();

    private CancellationTokenSource? _downloadCts;

    public ModelManagerViewModel()
    {
        ScanDirectory = AppSettings.Load().ModelDirectory;
        if (!string.IsNullOrEmpty(ScanDirectory))
            _ = ScanAsync();
        else
            _ = RefreshRecommendedDownloadsAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                if (App.SetupManager.Recommendations.Count == 0)
                    await App.SetupManager.ScanHardwareAsync();

                await RefreshRecommendedDownloadsAsync();
            }
            catch
            {
                // Best-effort; the view still loads.
            }
        });

        var pending = AppSettings.Load();
        if (!pending.IsDownloadComplete && !string.IsNullOrEmpty(pending.PendingDownloadModelName))
        {
            RecommendedDownloadStatus = $"Resume download for {pending.PendingDownloadModelName}?";
            RecommendedDownloadProgress = pending.PendingDownloadProgress;
            IsRecommendedDownloadPaused = true;
        }
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

            var pending = AppSettings.Load();
            var pendingFilePath = !pending.IsDownloadComplete && !string.IsNullOrWhiteSpace(pending.PendingDownloadFilePath)
                ? Path.GetFullPath(pending.PendingDownloadFilePath)
                : null;

            foreach (var m in found)
            {
                if (!string.IsNullOrWhiteSpace(pendingFilePath) &&
                    string.Equals(Path.GetFullPath(m.FilePath), pendingFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var vm = new ModelInfoViewModel(m);
                if (App.Provider.IsReady && !string.IsNullOrEmpty(lastLoaded) &&
                    string.Equals(m.FilePath, lastLoaded, StringComparison.OrdinalIgnoreCase))
                {
                    vm.IsLoaded = true;
                }
                Models.Add(vm);
            }

            if (App.SetupManager.Recommendations.Count == 0)
                await App.SetupManager.ScanHardwareAsync();

            await RefreshRecommendedDownloadsAsync();

            StatusMessage = $"{found.Count} model{(found.Count == 1 ? "" : "s")} found";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Scan error: {ex.Message}";
        }
        finally { IsScanning = false; }
    }

    private async Task RefreshRecommendedDownloadsAsync()
    {
        try
        {
            if (App.SetupManager.Recommendations.Count == 0)
                await App.SetupManager.ScanHardwareAsync();

            var installDir = string.IsNullOrWhiteSpace(ScanDirectory) ? AppSettings.Load().ModelDirectory : ScanDirectory;
            var downloaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(installDir) && Directory.Exists(installDir))
            {
                foreach (var file in Directory.GetFiles(installDir, "*.gguf", SearchOption.TopDirectoryOnly))
                    downloaded.Add(Path.GetFullPath(file));
            }

            var pending = AppSettings.Load();
            var pendingModelName = pending.IsDownloadComplete ? string.Empty : pending.PendingDownloadModelName;
            var pendingFilePath = !pending.IsDownloadComplete && !string.IsNullOrWhiteSpace(pending.PendingDownloadFilePath)
                ? Path.GetFullPath(pending.PendingDownloadFilePath)
                : null;

            var recommendations = App.SetupManager.Recommendations
                .Where(r =>
                {
                    var expectedPath = Path.GetFullPath(Path.Combine(installDir ?? string.Empty, $"{r.Model.Name.Replace(" ", "_")}.gguf"));
                    if (!string.IsNullOrWhiteSpace(pendingModelName) && r.Model.Name.Equals(pendingModelName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    return !downloaded.Contains(expectedPath);
                })
                .Select(r =>
                {
                    var item = new RecommendedModelViewModel(r,
                        !string.IsNullOrWhiteSpace(pendingModelName) && r.Model.Name.Equals(pendingModelName, StringComparison.OrdinalIgnoreCase),
                        pending.PendingDownloadProgress);
                    item.IsPaused = !string.IsNullOrWhiteSpace(pendingModelName) && r.Model.Name.Equals(pendingModelName, StringComparison.OrdinalIgnoreCase);
                    return item;
                })
                .ToList();

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                RecommendedDownloads.Clear();
                foreach (var item in recommendations)
                    RecommendedDownloads.Add(item);

                RefreshRecommendedDownloadStates();
            });
        }
        catch
        {
            // Best-effort; the page still works with local models.
        }
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
            s.ModelDirectory = ScanDirectory;
            s.IsFirstRun = false;
            s.IsDownloadComplete = true;
            s.Save();
            
            OnModelLoaded?.Invoke(SelectedModel.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load error: {ex.Message}";
        }
        finally { IsLoading = false; OnPropertyChanged(nameof(LoadButtonText)); }
    }

    [RelayCommand]
    private async Task DownloadRecommended(RecommendedModelViewModel model)
    {
        if (model is null) return;

        if (IsRecommendedDownloadRunning && ActiveRecommendedModel?.Model.Name == model.Source.Model.Name)
            return;

        var settings = AppSettings.Load();
        var installDir = string.IsNullOrWhiteSpace(ScanDirectory) ? settings.ModelDirectory : ScanDirectory;
        if (string.IsNullOrWhiteSpace(installDir))
        {
            installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NovaLiteModels");
        }

        Directory.CreateDirectory(installDir);
        ScanDirectory = installDir;
        settings.ModelDirectory = installDir;
        settings.IsFirstRun = false;
        settings.IsDownloadComplete = false;
        settings.PendingDownloadModelName = model.Source.Model.Name;
        settings.PendingDownloadFilePath = Path.Combine(installDir, $"{model.Source.Model.Name.Replace(" ", "_")}.gguf") + ".partial";
        settings.PendingDownloadProgress = 0;
        settings.Save();

        ActiveRecommendedModel = model.Source;
        IsRecommendedDownloadRunning = true;
        IsRecommendedDownloadPaused = false;
        RecommendedDownloadProgress = 0;
        RefreshRecommendedDownloadStates();
        var destPath = settings.PendingDownloadFilePath;
        RecommendedDownloadStatus = $"Downloading {model.Source.Model.Name}…";
        _downloadCts = new CancellationTokenSource();

        try
        {
            await App.SetupManager.DownloadAndBenchmarkAsync(
                model.Source,
                progress => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    RecommendedDownloadProgress = progress;
                    var s = AppSettings.Load();
                    s.PendingDownloadProgress = progress;
                    s.Save();
                }),
                status => Avalonia.Threading.Dispatcher.UIThread.Post(() => RecommendedDownloadStatus = status),
                _downloadCts.Token);

            var loadedModel = await App.GgufLoader.LoadAsync(destPath);
            await App.Provider.LoadAsync(loadedModel);

            var store = AppSettings.Load();
            store.LastModelPath = destPath;
            store.ModelDirectory = installDir;
            store.IsFirstRun = false;
            store.IsDownloadComplete = true;
            store.PendingDownloadModelName = string.Empty;
            store.PendingDownloadFilePath = string.Empty;
            store.PendingDownloadProgress = 0;
            store.Save();

            await ScanAsync();
            RecommendedDownloadStatus = $"Loaded {Path.GetFileName(destPath)}";
            OnModelLoaded?.Invoke(Path.GetFileName(destPath));
        }
            catch (OperationCanceledException)
        {
            RecommendedDownloadStatus = "Download paused.";
            var pauseSettings = AppSettings.Load();
            pauseSettings.PendingDownloadProgress = RecommendedDownloadProgress;
            pauseSettings.Save();
        }
        catch (Exception ex)
        {
            RecommendedDownloadStatus = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsRecommendedDownloadRunning = false;
            IsRecommendedDownloadPaused = _downloadCts?.IsCancellationRequested == true;
            _downloadCts?.Dispose();
            _downloadCts = null;
            RecommendedDownloadProgress = Math.Max(RecommendedDownloadProgress, 100);

            if (!IsRecommendedDownloadPaused)
            {
                ActiveRecommendedModel = null;
            }

            RefreshRecommendedDownloadStates();
        }
    }

    [RelayCommand]
    private void PauseRecommendedDownload()
    {
        if (!IsRecommendedDownloadRunning || _downloadCts is null) return;
        IsRecommendedDownloadPaused = true;
        RecommendedDownloadStatus = "Pausing download…";
        RefreshRecommendedDownloadStates();
        _downloadCts.Cancel();
    }

    [RelayCommand]
    private async Task ResumeRecommendedDownload()
    {
        var settings = AppSettings.Load();
        if (string.IsNullOrWhiteSpace(settings.PendingDownloadModelName)) return;

        var matchingModel = App.SetupManager.Recommendations.FirstOrDefault(r => r.Model.Name.Equals(settings.PendingDownloadModelName, StringComparison.OrdinalIgnoreCase));
        if (matchingModel is null) return;

        IsRecommendedDownloadPaused = false;
        RefreshRecommendedDownloadStates();

        var vm = new RecommendedModelViewModel(matchingModel, true, settings.PendingDownloadProgress);
        await DownloadRecommended(vm);
    }

    [RelayCommand]
    private void CancelRecommendedDownload()
    {
        if (!IsRecommendedDownloadRunning && _downloadCts is null)
        {
            var pending = AppSettings.Load();
            pending.IsDownloadComplete = false;
            pending.PendingDownloadModelName = string.Empty;
            pending.PendingDownloadFilePath = string.Empty;
            pending.PendingDownloadProgress = 0;
            pending.Save();
            RecommendedDownloadStatus = "Download cancelled.";
            RecommendedDownloadProgress = 0;
            ActiveRecommendedModel = null;
            IsRecommendedDownloadPaused = false;
            IsRecommendedDownloadRunning = false;
            RefreshRecommendedDownloadStates();
            return;
        }

        if (_downloadCts is not null)
            _downloadCts.Cancel();

        var settings = AppSettings.Load();
        settings.IsDownloadComplete = false;
        settings.PendingDownloadModelName = string.Empty;
        settings.PendingDownloadFilePath = string.Empty;
        settings.PendingDownloadProgress = 0;
        settings.Save();

        RecommendedDownloadStatus = "Download cancelled.";
        RecommendedDownloadProgress = 0;
        IsRecommendedDownloadRunning = false;
        IsRecommendedDownloadPaused = false;
        ActiveRecommendedModel = null;
        RefreshRecommendedDownloadStates();
    }

    private void RefreshRecommendedDownloadStates()
    {
        var pending = AppSettings.Load();
        var pendingModelName = pending.IsDownloadComplete ? string.Empty : pending.PendingDownloadModelName;

        foreach (var item in RecommendedDownloads)
        {
            var isPendingResume = !string.IsNullOrWhiteSpace(pendingModelName) &&
                item.Model.Name.Equals(pendingModelName, StringComparison.OrdinalIgnoreCase);
            var isActive = ActiveRecommendedModel is not null &&
                item.Model.Name.Equals(ActiveRecommendedModel.Model.Name, StringComparison.OrdinalIgnoreCase);

            item.IsActiveDownload = isActive || isPendingResume;
            item.IsPaused = isActive ? IsRecommendedDownloadPaused : isPendingResume;
        }
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
