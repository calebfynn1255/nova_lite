namespace NovaLite.Core.Settings;

/// <summary>User-persisted application settings, serialised to JSON.</summary>
public sealed class AppSettings
{
    // Appearance
    public string Theme { get; set; } = "Dark";    // "Light" | "Dark" | "OledBlack"

    // Model
    public string ModelDirectory { get; set; } = string.Empty;
    public string LastModelPath { get; set; } = string.Empty;

    // Inference
    public float Temperature { get; set; } = 0.7f;
    public float TopP { get; set; } = 0.9f;
    public float RepetitionPenalty { get; set; } = 1.1f;
    public int MaxTokens { get; set; } = 2048;
    public int ContextLength { get; set; } = 4096;
    // Setup
    public bool IsFirstRun { get; set; } = true;
    public bool IsDownloadComplete { get; set; } = false;
    public string PendingDownloadModelName { get; set; } = string.Empty;
    public string PendingDownloadFilePath { get; set; } = string.Empty;
    public double PendingDownloadProgress { get; set; } = 0;

    // Advanced
    public int GpuLayers { get; set; } = 0;   // 0 = CPU-only
    public int Threads { get; set; } = 0;      // 0 = auto

    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NovaLite", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            }
        }
        catch { /* return defaults on any error */ }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var json = System.Text.Json.JsonSerializer.Serialize(this,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch { /* best-effort save */ }
    }
}
