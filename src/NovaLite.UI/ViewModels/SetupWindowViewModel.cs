using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaLite.Core.Settings;
using NovaLite.Setup;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NovaLite.UI.ViewModels;

public partial class SetupWindowViewModel : ObservableObject
{
    private readonly SetupService _setup;
    
    public Action? CloseAction { get; set; }
    public Func<Task<string?>>? PickFolderAction { get; set; }
    
    private CancellationTokenSource? _downloadCts;

    // Steps: 0=Scan, 1=Catalog, 2=Path, 3=Download
    [ObservableProperty] private int _currentStep = 0; 
    
    public bool IsStepScan     => CurrentStep == 0;
    public bool IsStepRecommend=> CurrentStep == 1;
    public bool IsStepPath     => CurrentStep == 2;
    public bool IsStepDownload => CurrentStep == 3;

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStepScan));
        OnPropertyChanged(nameof(IsStepRecommend));
        OnPropertyChanged(nameof(IsStepPath));
        OnPropertyChanged(nameof(IsStepDownload));
    }
    
    [ObservableProperty] private HardwareProfile? _hardware;
    
    public ObservableCollection<RecommendedModelViewModel> Recommendations { get; } = [];
    
    [ObservableProperty] private ObservableCollection<RecommendedModel> _selectedModels = new();

    public bool HasSelection => SelectedModels.Count > 0;

    [ObservableProperty] private string _installPath = "";
    [ObservableProperty] private string _requiredSpaceStr = "";

    [ObservableProperty] private double _downloadProgress;
    [ObservableProperty] private string _downloadStatus = string.Empty;
    [ObservableProperty] private bool _isDownloading;
    [ObservableProperty] private bool _isPaused;

    // Resume-step display info
    [ObservableProperty] private string _resumeModelName = string.Empty;
    [ObservableProperty] private double _resumeProgress;

    public SetupWindowViewModel()
    {
        _setup = App.SetupManager;
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NovaLiteModels");
        InstallPath = defaultPath;

        var settings = AppSettings.Load();
        _pendingModelName = settings.PendingDownloadModelName;
        _pendingModelProgress = settings.PendingDownloadProgress;

        SelectedModels.CollectionChanged += (s, e) => OnPropertyChanged(nameof(HasSelection));

        // If there's an incomplete download, auto-scan and land on recommendations
        if (!settings.IsFirstRun && !settings.IsDownloadComplete && !string.IsNullOrEmpty(settings.PendingDownloadModelName))
        {
            InstallPath = Path.GetDirectoryName(settings.PendingDownloadFilePath) ?? defaultPath;
            // Auto-scan in background so the user lands on step 1 with the model highlighted
            _ = AutoScanForResumeAsync();
        }
    }

    private async Task AutoScanForResumeAsync()
    {
        try
        {
            await ModelCatalog.RefreshAsync();
            await _setup.ScanHardwareAsync();
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Hardware = _setup.Hardware;
                PopulateRecommendations();
                CurrentStep = 1;
            });
        }
        catch { /* best-effort — user can hit Scan manually */ }
    }

    // Stored for marking the resumable model in recommendations
    private string _pendingModelName = string.Empty;
    private double _pendingModelProgress;

    [RelayCommand]
    private async Task StartScan()
    {
        await ModelCatalog.RefreshAsync();
        await _setup.ScanHardwareAsync();
        Hardware = _setup.Hardware;
        PopulateRecommendations();
        var best = Recommendations.FirstOrDefault(r => r.Source.IsRecommended) ?? Recommendations.FirstOrDefault();
        SelectedModels.Clear();
        if (best != null)
        {
            SelectedModels.Add(best.Source);
            SelectModelInUiAction?.Invoke(best);
        }
        CurrentStep = 1;
    }

    public Action<RecommendedModelViewModel>? SelectModelInUiAction { get; set; }

    private void PopulateRecommendations()
    {
        Recommendations.Clear();
        var pending = AppSettings.Load();
        foreach (var r in _setup.Recommendations)
        {
            bool isResumable = !string.IsNullOrEmpty(pending.PendingDownloadModelName) &&
                               !pending.IsDownloadComplete &&
                               r.Model.Name.Equals(pending.PendingDownloadModelName, StringComparison.OrdinalIgnoreCase);
            Recommendations.Add(new RecommendedModelViewModel(r, isResumable, isResumable ? pending.PendingDownloadProgress : 0));
        }
    }

    /// <summary>Called when the user clicks Resume directly on the model card.</summary>
    [RelayCommand]
    private async Task ResumeFromCard(RecommendedModelViewModel vm)
    {
        SelectedModels.Clear();
        SelectedModels.Add(vm.Source);
        await StartDownload();
    }

    [RelayCommand]
    private void GoBack()
    {
        if (CurrentStep > 0)
            CurrentStep--;
    }

    [RelayCommand]
    private void GoToPathSelection()
    {
        if (SelectedModels.Count == 0) return;
        RequiredSpaceStr = $"Selected {SelectedModels.Count} models.";
        CurrentStep = 2;
    }

    [RelayCommand]
    private async Task BrowsePath()
    {
        if (PickFolderAction != null)
        {
            var path = await PickFolderAction();
            if (!string.IsNullOrEmpty(path))
                InstallPath = path;
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

        var settings = AppSettings.Load();
        settings.ModelDirectory = InstallPath;
        settings.IsFirstRun = false;
        settings.IsDownloadComplete = false;
        settings.Save();

        Directory.CreateDirectory(InstallPath);

        try
        {
            foreach (var sm in SelectedModels)
            {
                DownloadStatus = $"Downloading {sm.Model.Name}…";
                DownloadProgress = 0;

                // Track pending state so resume is possible after restart
                var destPath = Path.Combine(InstallPath, $"{sm.Model.Name.Replace(" ", "_")}.gguf");
                var s2 = AppSettings.Load();
                s2.PendingDownloadModelName = sm.Model.Name;
                s2.PendingDownloadFilePath = destPath;
                s2.PendingDownloadProgress = 0;
                s2.Save();

                await _setup.DownloadAndBenchmarkAsync(sm, p =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (p > 100)
                        {
                            DownloadStatus = $"Verifying {sm.Model.Name}...";
                        }
                        else
                        {
                            DownloadProgress = p;
                            // Persist progress so resume screen is accurate
                            var s3 = AppSettings.Load();
                            s3.PendingDownloadProgress = p;
                            s3.Save();
                        }
                    });
                }, _downloadCts.Token);
            }

            // All done — mark as complete
            var sf = AppSettings.Load();
            sf.IsDownloadComplete = true;
            sf.PendingDownloadModelName = string.Empty;
            sf.PendingDownloadFilePath = string.Empty;
            sf.PendingDownloadProgress = 0;
            
            // Automatically enable GPU offloading if they have a dedicated GPU
            if (Hardware?.TotalVRamMB > 512)
            {
                sf.GpuLayers = 99; // 99 layers means offload everything to GPU in llama.cpp
            }

            sf.Save();

            DownloadStatus = "Setup complete!";
            await Task.Delay(1000);
            CloseAction?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Save progress for resume
            var sc = AppSettings.Load();
            sc.PendingDownloadProgress = DownloadProgress;
            sc.Save();
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Network error (Paused): {ex.Message}";
            IsPaused = true;
            var se = AppSettings.Load();
            se.PendingDownloadProgress = DownloadProgress;
            se.Save();
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
        DownloadStatus = "Paused. Press Resume to continue.";
        IsPaused = true;
        _downloadCts?.Cancel();
    }

    [RelayCommand]
    private void ResumeDownload()
    {
        if (!IsPaused) return;
        IsPaused = false;
        _ = StartDownload();
    }

    [RelayCommand]
    private void StopDownload()
    {
        IsPaused = false;
        _downloadCts?.Cancel();

        // Delete partial files
        foreach (var sm in SelectedModels)
        {
            var destPath = Path.Combine(InstallPath, $"{sm.Model.Name.Replace(" ", "_")}.gguf");
            if (File.Exists(destPath))
                try { File.Delete(destPath); } catch { }
        }

        // Reset pending state and go back to first run / scan
        var s = AppSettings.Load();
        s.IsFirstRun = true;
        s.IsDownloadComplete = false;
        s.PendingDownloadModelName = string.Empty;
        s.PendingDownloadFilePath = string.Empty;
        s.PendingDownloadProgress = 0;
        s.Save();

        DownloadProgress = 0;
        DownloadStatus = "Stopped and cleaned up.";
        CurrentStep = 0;
    }

    /// <summary>Called from the Resume screen when user taps Continue.</summary>
    [RelayCommand]
    private async Task ContinueIncompleteDownload()
    {
        var settings = AppSettings.Load();
        // Re-hydrate enough to call StartDownload
        if (SelectedModels.Count == 0)
        {
            // We don't have a real RecommendedModel object — but we can reload catalog and find it
            await ModelCatalog.RefreshAsync();
            await _setup.ScanHardwareAsync();
            Hardware = _setup.Hardware;
            var pending2 = AppSettings.Load();
            foreach (var r in _setup.Recommendations)
            {
                bool isResumable = r.Model.Name.Equals(pending2.PendingDownloadModelName, StringComparison.OrdinalIgnoreCase);
                Recommendations.Add(new RecommendedModelViewModel(r, isResumable, isResumable ? pending2.PendingDownloadProgress : 0));
            }

            var match = Recommendations.FirstOrDefault(r =>
                r.Source.Model.Name.Equals(settings.PendingDownloadModelName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
                SelectedModels.Add(match.Source);
            else
            {
                // Fallback: go back to recommendations
                DownloadStatus = "Model not found in catalog. Please re-select.";
                CurrentStep = 1;
                return;
            }
        }

        await StartDownload();
    }

    /// <summary>Cancel and restart from scratch.</summary>
    [RelayCommand]
    private void CancelIncompleteDownload()
    {
        // Delete partial file
        var settings = AppSettings.Load();
        if (!string.IsNullOrEmpty(settings.PendingDownloadFilePath) && File.Exists(settings.PendingDownloadFilePath))
            try { File.Delete(settings.PendingDownloadFilePath); } catch { }

        settings.IsFirstRun = true;
        settings.IsDownloadComplete = false;
        settings.PendingDownloadModelName = string.Empty;
        settings.PendingDownloadFilePath = string.Empty;
        settings.PendingDownloadProgress = 0;
        settings.Save();

        CurrentStep = 0;
    }
}

/// <summary>
/// Wraps a <see cref="RecommendedModel"/> to add UI-only state like whether
/// a partial download exists for this model.
/// </summary>
public sealed class RecommendedModelViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public RecommendedModel Source { get; }

    // Convenience pass-throughs so XAML can still bind to the same names
    public string DisplayName   => Source.DisplayName;
    public int    StarRating    => Source.StarRating;
    public string Reason        => Source.Reason;
    public ModelCatalogEntry Model => Source.Model;

    public bool   IsPendingResume  { get; }
    public double ResumeProgress   { get; }

    public RecommendedModelViewModel(RecommendedModel source, bool isPendingResume, double resumeProgress)
    {
        Source          = source;
        IsPendingResume = isPendingResume;
        ResumeProgress  = resumeProgress;
    }
}
