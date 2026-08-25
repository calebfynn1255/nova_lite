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
    public Action<string, string>? ShowNotificationAction { get; set; }
    
    private CancellationTokenSource? _downloadCts;

    // Steps: 0=Scan, 1=Recommend, 2=Path, 3=Download
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
    [ObservableProperty] private bool _isBenchmarking;
    [ObservableProperty] private bool _isDownloadReady;
    [ObservableProperty] private string? _downloadedModelPath;
    [ObservableProperty] private string? _downloadedModelName;

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
                CurrentStep = 0;
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

        var filtered = _setup.Recommendations;

        // Sort by whether it fits the PC (IsRecommended)
        var ordered = filtered.OrderByDescending(r => r.IsRecommended).Take(5).ToList();

        foreach (var r in ordered)
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
                var plannedDestPath = Path.Combine(InstallPath, $"{sm.Model.Name.Replace(" ", "_")}.gguf");
                var s2 = AppSettings.Load();
                s2.PendingDownloadModelName = sm.Model.Name;
                s2.PendingDownloadFilePath = plannedDestPath + ".partial";
                s2.PendingDownloadProgress = 0;
                s2.Save();

                double lastReportedProgress = -1;
                DateTimeOffset lastUiUpdateAt = DateTimeOffset.MinValue;

                var destPath = await _setup.DownloadAndBenchmarkAsync(
                    sm,
                    p =>
                    {
                        var now = DateTimeOffset.UtcNow;
                        bool shouldPost = p >= 100 || p - lastReportedProgress >= 0.5 || (now - lastUiUpdateAt).TotalMilliseconds >= 250;
                        if (!shouldPost)
                            return;

                        lastReportedProgress = p;
                        lastUiUpdateAt = now;

                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            if (p >= 100)
                            {
                                DownloadProgress = 100;
                                IsBenchmarking = false;
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
                    },
                    status => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        DownloadStatus = status;
                        if (status.StartsWith("Download complete.", StringComparison.Ordinal))
                        {
                            IsBenchmarking = true;
                            ShowNotificationAction?.Invoke(
                                "Download complete",
                                $"{sm.Model.Name} is ready. NovaLite is loading it and running a quick performance check.");

                            // Show native Windows Toast notification via PowerShell
                            try
                            {
                                var script = $"[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null; " +
                                             $"$template = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                                             $"$nodes = $template.GetElementsByTagName('text'); " +
                                             $"$nodes[0].AppendChild($template.CreateTextNode('Download Complete')) > $null; " +
                                             $"$nodes[1].AppendChild($template.CreateTextNode('{sm.Model.Name} has been downloaded and is being benchmarked.')) > $null; " +
                                             $"$toast = [Windows.UI.Notifications.ToastNotification]::new($template); " +
                                             $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('NovaLite').Show($toast);";
                                
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = "powershell",
                                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                });
                            }
                            catch { }
                        }
                    }),
                    _downloadCts.Token);

                // Save downloaded model info
                DownloadedModelPath = destPath;
                DownloadedModelName = sm.Model.Name;
            }

            IsBenchmarking = false;
            IsDownloadReady = true;
            DownloadProgress = 100;

            // Mark download complete in settings
            var sf = AppSettings.Load();
            sf.IsDownloadComplete = true;
            sf.PendingDownloadModelName = string.Empty;
            sf.PendingDownloadFilePath = string.Empty;
            sf.PendingDownloadProgress = 0;
            sf.Save();

            DownloadStatus = $"{DownloadedModelName ?? "Model"} downloaded successfully! Click Continue to load your model and proceed.";
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
            DownloadStatus = $"Setup interrupted: {ex.Message}";
            IsDownloadReady = false;
            var se = AppSettings.Load();
            se.PendingDownloadProgress = DownloadProgress;
            se.Save();
        }
        finally
        {
            IsBenchmarking = false;
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private async Task ProceedToChat()
    {
        if (string.IsNullOrEmpty(DownloadedModelPath) || !File.Exists(DownloadedModelPath))
        {
            // Fallback: try finding any downloaded model in InstallPath
            if (!string.IsNullOrEmpty(InstallPath) && Directory.Exists(InstallPath))
            {
                var files = Directory.GetFiles(InstallPath, "*.gguf");
                if (files.Length > 0)
                    DownloadedModelPath = files[0];
            }
        }

        if (string.IsNullOrEmpty(DownloadedModelPath) || !File.Exists(DownloadedModelPath))
        {
            DownloadStatus = "Model file not found. Please try re-downloading.";
            return;
        }

        try
        {
            DownloadStatus = $"Loading {DownloadedModelName ?? Path.GetFileName(DownloadedModelPath)} into memory…";
            IsBenchmarking = true;

            var loadedModel = await App.GgufLoader.LoadAsync(DownloadedModelPath);
            await App.Provider.LoadAsync(loadedModel);

            var s = AppSettings.Load();
            s.LastModelPath = DownloadedModelPath;
            s.ModelDirectory = InstallPath;
            s.IsFirstRun = false;
            s.IsDownloadComplete = true;
            s.PendingDownloadModelName = string.Empty;
            s.PendingDownloadFilePath = string.Empty;
            s.PendingDownloadProgress = 0;
            s.Save();

            CloseAction?.Invoke();
        }
        catch (Exception ex)
        {
            DownloadStatus = $"Failed to load model: {ex.Message}";
        }
        finally
        {
            IsBenchmarking = false;
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
                CurrentStep = 0;
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
public sealed partial class RecommendedModelViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public RecommendedModel Source { get; }

    // Convenience pass-throughs so XAML can still bind to the same names
    public string DisplayName   => Source.DisplayName;
    public int    StarRating    => Source.StarRating;
    public string Reason        => Source.Reason;
    public ModelCatalogEntry Model => Source.Model;

    public bool   IsPendingResume  { get; }
    public double ResumeProgress   { get; }

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isActiveDownload;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _isPaused;

    public string PauseLabel => IsPaused ? $"Paused • {Math.Round(ResumeProgress, 0)}%" : string.Empty;
    public string PrimaryActionText => IsPaused ? "Resume" : "Download & Load";

    public RecommendedModelViewModel(RecommendedModel source, bool isPendingResume, double resumeProgress)
    {
        Source          = source;
        IsPendingResume = isPendingResume;
        ResumeProgress  = resumeProgress;
    }
}
