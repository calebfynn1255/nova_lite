using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using NovaLite.Core.Files;
using NovaLite.Core.Models;
using NovaLite.Core.Settings;
using NovaLite.Setup;
using System.IO;
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
    [ObservableProperty] private bool _isDownloadReady;
    [ObservableProperty] private string? _downloadedModelPath;
    [ObservableProperty] private string? _downloadedModelName;
    [ObservableProperty] 
    [NotifyCanExecuteChangedFor(nameof(LoadSelectedCommand))]
    [NotifyPropertyChangedFor(nameof(LoadButtonText))]
    private ModelInfoViewModel? _selectedModel;

    public ObservableCollection<ModelInfoViewModel> Models { get; } = [];
    public ObservableCollection<ModelInfoViewModel> AdvancedModels { get; } = [];
    public ObservableCollection<ModelInfoViewModel> GeneralModels { get; } = [];
    public ObservableCollection<RecommendedModelViewModel> RecommendedDownloads { get; } = [];

    /// <summary>Called after a model is successfully loaded. UI can hook this to update the header label.</summary>
    public Action<string>? OnModelLoaded { get; set; }
    public Action? OnProceedToChatRequested { get; set; }

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

    [RelayCommand]
    private async Task ScanWholePc() => await ScanPathsAsync(
        DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => d.RootDirectory.FullName),
        "Scanning entire PC…");

    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(ScanDirectory)) return;
        await ScanPathsAsync(new[] { ScanDirectory }, "Scanning…");
    }

    private async Task ScanPathsAsync(IEnumerable<string> paths, string statusMessage)
    {
        var rootPaths = paths?.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p)).ToArray() ?? Array.Empty<string>();
        if (rootPaths.Length == 0)
        {
            StatusMessage = "No valid directory selected.";
            return;
        }

        IsScanning = true;
        StatusMessage = statusMessage;
        Models.Clear();
        AdvancedModels.Clear();
        GeneralModels.Clear();

        try
        {
            var found = await Task.Run(() =>
            {
                var results = new List<ModelInfo>();
                foreach (var path in rootPaths)
                {
                    try
                    {
                        results.AddRange(_scanner.Scan(path));
                    }
                    catch
                    {
                        // Some drive roots or folders may be inaccessible; skip them.
                    }
                }
                return results;
            });

            var lastLoaded = AppSettings.Load().LastModelPath;

            var pending = AppSettings.Load();
            var pendingFilePath = !pending.IsDownloadComplete && !string.IsNullOrWhiteSpace(pending.PendingDownloadFilePath)
                ? Path.GetFullPath(pending.PendingDownloadFilePath)
                : null;

            var distinctFound = found
                .GroupBy(m => Path.GetFullPath(m.FilePath), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            var advancedKeywords = new[] { "coder", "code", "qwen", "deepseek", "codellama", "starcoder", "phi" };
            var lastLoadedPath = !string.IsNullOrEmpty(lastLoaded) ? Path.GetFullPath(lastLoaded) : null;

            foreach (var m in distinctFound)
            {
                if (!string.IsNullOrWhiteSpace(pendingFilePath) &&
                    string.Equals(Path.GetFullPath(m.FilePath), pendingFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var vm = new ModelInfoViewModel(m);
                if (lastLoadedPath != null &&
                    string.Equals(Path.GetFullPath(m.FilePath), lastLoadedPath, StringComparison.OrdinalIgnoreCase))
                {
                    vm.IsLoaded = true;
                }
                
                Models.Add(vm);
                
                var name = m.FileName.ToLowerInvariant();
                if (advancedKeywords.Any(k => name.Contains(k)))
                    AdvancedModels.Add(vm);
                else
                    GeneralModels.Add(vm);
            }

            if (App.SetupManager.Recommendations.Count == 0)
                await App.SetupManager.ScanHardwareAsync();

            await RefreshRecommendedDownloadsAsync();

            StatusMessage = $"{Models.Count} model{(Models.Count == 1 ? "" : "s")} found";
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
    private async Task LoadSelected(object? parameter)
    {
        var target = parameter as ModelInfoViewModel ?? SelectedModel;
        if (target is null) return;
        SelectedModel = target;
        IsLoading = true;
        StatusMessage = $"Loading {target.FileName}…";
        try
        {
            // Unload existing
            await App.Provider.UnloadAsync();

            // Load via loader (hardcoding GGUF loader for Milestone 1)
            var loadedModel = await App.GgufLoader.LoadAsync(target.FilePath);
            
            // Set as active model in provider
            await App.Provider.LoadAsync(loadedModel);
            
            foreach (var m in Models) m.IsLoaded = false;
            target.IsLoaded = true;
            OnPropertyChanged(nameof(LoadButtonText));
            LoadSelectedCommand.NotifyCanExecuteChanged();
            
            StatusMessage = $"Loaded: {target.FileName}";
            
            // Persist last model path for auto-load on next startup
            var s = AppSettings.Load();
            s.LastModelPath = target.FilePath;
            s.ModelDirectory = ScanDirectory;
            s.IsFirstRun = false;
            s.IsDownloadComplete = true;
            s.Save();
            
            OnModelLoaded?.Invoke(target.FileName);
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
        var pendingFilePath = settings.PendingDownloadFilePath;
        RecommendedDownloadStatus = $"Downloading {model.Source.Model.Name}…";
        _downloadCts = new CancellationTokenSource();

        try
        {
            var destPath = await App.SetupManager.DownloadAndBenchmarkAsync(
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

            // Do not auto-load the model. Leave it ready and show a Proceed button.
            DownloadedModelPath = destPath;
            DownloadedModelName = model.Source.Model.Name;
            IsDownloadReady = true;

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
            RecommendedDownloadStatus = $"Downloaded {Path.GetFileName(destPath)} (ready)";
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

            // If download finished and is ready, expose Proceed action in UI
            if (IsDownloadReady && !IsRecommendedDownloadPaused)
            {
                // UI may show a button bound to ProceedToChatCommand
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
    private async Task ProceedToChat()
    {
        if (string.IsNullOrEmpty(DownloadedModelPath)) return;

        try
        {
            var loadedModel = await App.GgufLoader.LoadAsync(DownloadedModelPath);
            await App.Provider.LoadAsync(loadedModel);

            var store = AppSettings.Load();
            store.LastModelPath = DownloadedModelPath;
            store.ModelDirectory = ScanDirectory;
            store.IsFirstRun = false;
            store.IsDownloadComplete = true;
            store.Save();

            OnModelLoaded?.Invoke(Path.GetFileName(DownloadedModelPath));

            // Let MainWindow navigate to chat if it wants
            OnProceedToChatRequested?.Invoke();
        }
        catch (Exception ex)
        {
            RecommendedDownloadStatus = $"Load failed: {ex.Message}";
        }
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

    // CanLoad no longer gates on list selection — each card passes its item via CommandParameter
    private bool CanLoad() => !IsLoading;

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
