using System.Collections.ObjectModel;

namespace NovaLite.Setup;

public class SetupService
{
    private readonly HardwareScanner _scanner = new();
    private readonly RecommendationEngine _recommender = new();
    private readonly DownloadManager _downloader = new();
    private readonly BenchmarkRunner _benchmark;
    private readonly PerformanceConfigurator _configurator = new();

    public SetupService(NovaLite.Core.Interfaces.IModelLoader? loader = null)
    {
        _benchmark = new BenchmarkRunner(loader);
    }

    public event Action<int>? StepChanged;

    public HardwareProfile? Hardware { get; private set; }
    public ObservableCollection<RecommendedModel> Recommendations { get; } = new();

    public async Task ScanHardwareAsync()
    {
        Hardware = await _scanner.ScanAsync();
        
        var recs = _recommender.GetRecommendations(Hardware);
        Recommendations.Clear();
        foreach (var r in recs) Recommendations.Add(r);
        
        StepChanged?.Invoke(1);
    }

    public async Task DownloadAndBenchmarkAsync(
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

        await _downloader.DownloadModelAsync(selectedModel.Model.DownloadUrls, selectedModel.Model.ExpectedSha256, destPath, progressCallback, ct);

        statusCallback?.Invoke($"Download complete. Loading {selectedModel.Model.Name} for a quick performance check…");
        StepChanged?.Invoke(3); // Benchmark step

        // Apply configuration based on hardware
        var options = _configurator.Configure(selectedModel);

        var result = await _benchmark.RunAsync(destPath, selectedModel.Model.Name, options);
        
        // Save benchmark result to DB
        statusCallback?.Invoke("Saving performance results…");
        var db = await DatabaseManager.GetConnectionAsync();
        await db.InsertAsync(result);

        return; // Complete
    }

    public async Task<BenchmarkResult?> GetLatestBenchmarkAsync()
    {
        var db = await DatabaseManager.GetConnectionAsync();
        return await db.Table<BenchmarkResult>().OrderByDescending(x => x.RunDate).FirstOrDefaultAsync();
    }
}
