using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLite.Core.Settings;
using NovaLite.Setup;
using System.Collections.ObjectModel;
using System.IO;

namespace NovaLite.UI.ViewModels;

public partial class SetupWindowViewModel : ObservableObject
{
    private readonly SetupService _setup;
    
    public Action? CloseAction { get; set; }
    public Func<Task<string?>>? PickFolderAction { get; set; }
    
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty] private int _currentStep = 0; // 0=Scan, 1=Catalog, 2=Path, 3=Download
    
    public bool IsStepScan => CurrentStep == 0;
    public bool IsStepRecommend => CurrentStep == 1;
    public bool IsStepPath => CurrentStep == 2;
    public bool IsStepDownload => CurrentStep == 3;

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStepScan));
        OnPropertyChanged(nameof(IsStepRecommend));
        OnPropertyChanged(nameof(IsStepPath));
        OnPropertyChanged(nameof(IsStepDownload));
    }
    
    [ObservableProperty] private HardwareProfile? _hardware;
    
    public ObservableCollection<RecommendedModel> Recommendations { get; } = [];
    
    [ObservableProperty] private ObservableCollection<RecommendedModel> _selectedModels = new();

    [ObservableProperty] private string _installPath = "";
    [ObservableProperty] private string _requiredSpaceStr = "";

    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatus = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isPaused;

    public SetupWindowViewModel()
    {
        _setup = App.SetupManager;
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NovaLiteModels");
        InstallPath = defaultPath;
    }

    [RelayCommand]
    private async Task StartScan()
    {
        // 1. Fetch JSON catalog
        await ModelCatalog.RefreshAsync();

        // 2. Scan hardware
        await _setup.ScanHardwareAsync();
        
        Hardware = _setup.Hardware;
        Recommendations.Clear();
        foreach (var r in _setup.Recommendations) Recommendations.Add(r);
        
        var best = Recommendations.FirstOrDefault();
        SelectedModels.Clear();
        if (best != null) SelectedModels.Add(best);

        CurrentStep = 1; // Go to Recommendations
    }

    [RelayCommand]
    private void GoBack()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    private void GoToPathSelection()
    {
        if (SelectedModels.Count == 0) return;
        
        RequiredSpaceStr = $"Selected {SelectedModels.Count} models.";
        
        CurrentStep = 2; // Go to Path
    }

    [RelayCommand]
    private async Task BrowsePath()
    {
        if (PickFolderAction != null)
        {
            var path = await PickFolderAction();
            if (!string.IsNullOrEmpty(path))
            {
                InstallPath = path;
            }
        }
    }

    [RelayCommand]
    private async Task StartDownload()
    {
        if (SelectedModels.Count == 0) return;
        
        CurrentStep = 3;
        IsDownloading = true;
        IsPaused = false;
        _downloadCts = new CancellationTokenSource();

        // Save path
        var settings = AppSettings.Load();
        settings.ModelDirectory = InstallPath;
        settings.IsFirstRun = false;
        settings.Save();

        Directory.CreateDirectory(InstallPath);

        try
        {
            foreach (var sm in SelectedModels)
            {
                DownloadStatus = $"Downloading {sm.Model.Name}…";
                DownloadProgress = 0;

                await _setup.DownloadAndBenchmarkAsync(sm, p =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (p > 100) DownloadStatus = $"Verifying {sm.Model.Name}...";
                        else DownloadProgress = p;
                    });
                }, _downloadCts.Token);
            }

            DownloadStatus = "Setup complete!";

            await Task.Delay(1000);
            CloseAction?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Handled by Stop/Pause
        }
        catch (Exception ex)
        {
            // Treat unexpected errors (like network drops) as a Pause so the user can resume later
            DownloadStatus = $"Network error (Paused): {ex.Message}";
            IsPaused = true;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void PauseDownload()
    {
        if (!IsDownloading) return;
        
        DownloadStatus = "Paused...";
        IsPaused = true;
        _downloadCts?.Cancel();
    }

    [RelayCommand]
    private void ResumeDownload()
    {
        if (!IsPaused) return;
        
        IsPaused = false;
        // StartDownload will spawn a new CTS and resume via the DownloadManager's Range headers
        _ = StartDownload();
    }

    [RelayCommand]
    private void StopDownload()
    {
        IsPaused = false;
        _downloadCts?.Cancel();
        
        // Delete partial files for selected models
        foreach (var sm in SelectedModels)
        {
            var destPath = Path.Combine(InstallPath, $"{sm.Model.Name.Replace(" ", "_")}.gguf");
            if (File.Exists(destPath))
            {
                try { File.Delete(destPath); } catch { }
            }
        }
        
        DownloadProgress = 0;
        DownloadStatus = "Stopped and cleaned up.";
        
        // Go back to recommendations step
        CurrentStep = 1;
    }


}
