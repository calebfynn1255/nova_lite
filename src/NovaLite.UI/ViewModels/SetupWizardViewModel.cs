using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLite.Core.Settings;
using NovaLite.Setup;
using System.Collections.ObjectModel;
using System.IO;

namespace NovaLite.UI.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly MainWindowViewModel _mainVm;
    private readonly SetupService _setup;

    [ObservableProperty] private int _currentStep = 0; // 0=Scan, 1=Catalog, 2=Download, 3=Benchmark
    
    public bool IsStepScan => CurrentStep == 0;
    public bool IsStepRecommend => CurrentStep == 1;
    public bool IsStepDownload => CurrentStep == 2;
    public bool IsStepFinish => CurrentStep == 3;

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStepScan));
        OnPropertyChanged(nameof(IsStepRecommend));
        OnPropertyChanged(nameof(IsStepDownload));
        OnPropertyChanged(nameof(IsStepFinish));
    }
    
    [ObservableProperty] private HardwareProfile? _hardware;
    
    public ObservableCollection<RecommendedModel> Recommendations { get; } = [];
    [ObservableProperty] private RecommendedModel? _selectedModel;
    
    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatus = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isDownloadReady;
    [ObservableProperty] private string? _downloadedModelPath;
    [ObservableProperty] private string? _downloadedModelName;
    
    [ObservableProperty] private double _benchmarkTokensPerSec;

    public SetupWizardViewModel(MainWindowViewModel mainVm)
    {
        _mainVm = mainVm;
        _setup = App.SetupManager;
        _setup.StepChanged += OnSetupStepChanged;
    }

    private void OnSetupStepChanged(int step)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => CurrentStep = step);
    }

    [RelayCommand]
    private async Task StartScan()
    {
        // Ensure catalog is loaded before scanning so recommendations are available
        await ModelCatalog.RefreshAsync();
        await _setup.ScanHardwareAsync();
        
        Hardware = _setup.Hardware;
        Recommendations.Clear();
        foreach (var r in _setup.Recommendations) Recommendations.Add(r);
        
        SelectedModel = Recommendations.FirstOrDefault();
    }

    [RelayCommand]
    private async Task StartDownload()
    {
        if (SelectedModel is null) return;
        
        IsDownloadReady = false;
        DownloadedModelPath = null;
        DownloadedModelName = null;
        IsDownloading = true;
        DownloadStatus = $"Downloading {SelectedModel.Model.Name}…";

        try
        {
            var destPath = await _setup.DownloadAndBenchmarkAsync(SelectedModel, p =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (p > 100) DownloadStatus = "Verifying SHA-256 Hash...";
                    else DownloadProgress = p;
                });
            });

            // Do not auto-benchmark or auto-load. Mark ready and show Proceed button.
            DownloadStatus = "Download complete. Model ready.";
            IsDownloadReady = true;
            DownloadedModelPath = destPath;
            DownloadedModelName = SelectedModel.Model.Name;
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Error: {ex.Message}";
            IsDownloadReady = false;
            DownloadedModelPath = null;
            DownloadedModelName = null;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private async Task ProceedToChat()
    {
        if (string.IsNullOrEmpty(DownloadedModelPath)) return;

        try
        {
            var loadedModel = await App.GgufLoader.LoadAsync(DownloadedModelPath);
            await App.Provider.LoadAsync(loadedModel);

            var s = AppSettings.Load();
            s.LastModelPath = DownloadedModelPath;
            s.ModelDirectory = Path.GetDirectoryName(DownloadedModelPath) ?? s.ModelDirectory;
            s.IsFirstRun = false;
            s.IsDownloadComplete = true;
            s.Save();

            _mainVm.NavigateChat();
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Load failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Finish()
    {
        // Complete wizard, show welcome page
        _mainVm.NavigateWelcome();
    }
}
