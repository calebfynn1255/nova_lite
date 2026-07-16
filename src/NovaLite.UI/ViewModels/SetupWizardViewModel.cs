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
        
        IsDownloading = true;
        DownloadStatus = $"Downloading {SelectedModel.Model.Name}…";

        try
        {
            await _setup.DownloadAndBenchmarkAsync(SelectedModel, p =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (p > 100) DownloadStatus = "Verifying SHA-256 Hash...";
                    else DownloadProgress = p;
                });
            });

            DownloadStatus = "Setup complete!";
            
            var result = await _setup.GetLatestBenchmarkAsync();
            if (result != null)
            {
                BenchmarkTokensPerSec = result.AverageTokensPerSecond;
            }
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void Finish()
    {
        // Complete wizard, show welcome page
        _mainVm.NavigateWelcome();
    }
}
