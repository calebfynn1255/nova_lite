using System.Collections.ObjectModel;
using NovaLite.Core.Helpers;

namespace NovaLite.Setup;

public class SetupService
{
    private readonly System.Threading.SemaphoreSlim _scanLock = new(1, 1);
    private readonly HardwareScanner _scanner = new();
    private readonly RecommendationEngine _recommender = new();
    private readonly DownloadManager _downloader = new();
    private readonly PerformanceConfigurator _configurator = new();

    public SetupService(NovaLite.Core.Interfaces.IModelLoader? loader = null)
    {
    }

    public event Action<int>? StepChanged;

    public HardwareProfile? Hardware { get; private set; }
    public ObservableCollection<RecommendedModel> Recommendations { get; } = new();

    public async Task ScanHardwareAsync()
    {
        await _scanLock.WaitAsync();
        try
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLite");
            Directory.CreateDirectory(appData);
            var logPath = Path.Combine(appData, "startup.log");
            try { File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] ScanHardwareAsync start\n"); } catch { }

            try
            {
                Hardware = await _scanner.ScanAsync();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] ScanHardwareAsync scanner failed: {ex}\n"); } catch { }
                throw;
            }

            List<RecommendedModel> recs;
            try
            {
                // Ensure catalog is refreshed so RecommendationEngine has up-to-date models
                try { await ModelCatalog.RefreshAsync(); } catch { }
                recs = _recommender.GetRecommendations(Hardware).ToList();
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] ScanHardwareAsync recommender failed: {ex}\n"); } catch { }
                recs = new List<RecommendedModel>();
            }

            Recommendations.Clear();
            foreach (var r in recs) Recommendations.Add(r);

            // Diagnostic logging to help UI debug when recommendations are populated
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] ScanHardwareAsync completed. Recommendations={recs.Count}\n");
                foreach (var r in recs)
                {
                    File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] - {r.Model.Name} ({r.StarRating} stars)\n");
                }
            }
            catch { }

            StepChanged?.Invoke(1);
        }
        finally
        {
            _scanLock.Release();
        }
    }

    public async Task<string> DownloadAndBenchmarkAsync(
        RecommendedModel selectedModel,
        Action<double> progressCallback,
        Action<string>? statusCallback = null,
        CancellationToken ct = default)
    {
        StepChanged?.Invoke(2); // Download step

        var dir = Core.Settings.AppSettings.Load().ModelDirectory;
        if (string.IsNullOrEmpty(dir))
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "NovaLiteModels");
            Directory.CreateDirectory(dir);
            var settings = Core.Settings.AppSettings.Load();
            settings.ModelDirectory = dir;
            settings.Save();
        }

        var destPath = Path.Combine(dir, $"{selectedModel.Model.Name.Replace(" ", "_")}.gguf");

        var actualSizeBytes = await _downloader.DownloadModelAsync(selectedModel.Model.DownloadUrls, selectedModel.Model.ExpectedSha256, destPath, progressCallback, ct);

        StepChanged?.Invoke(3); // Finalize step
        statusCallback?.Invoke($"Download complete. Model ready to use. Size: {actualSizeBytes.ToFileSizeString()}");

        return destPath;
    }

    public async Task<BenchmarkResult?> GetLatestBenchmarkAsync()
    {
        var db = await DatabaseManager.GetConnectionAsync();
        return await db.Table<BenchmarkResult>().OrderByDescending(x => x.RunDate).FirstOrDefaultAsync();
    }
}
