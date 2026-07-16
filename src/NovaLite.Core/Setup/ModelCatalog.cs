using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace NovaLite.Setup;

public record ModelCatalogEntry(
    string Name,
    string Description,
    long MinRamMB,
    long RecommendedRamMB,
    int TargetTier,
    bool CpuFriendly,
    bool SupportsVision,
    IReadOnlyList<string> DownloadUrls,
    string ExpectedSha256
);

public static class ModelCatalog
{
    private static IReadOnlyList<ModelCatalogEntry>? _models;
    private static readonly string CatalogUrl = "https://raw.githubusercontent.com/fynn-user/novalite-models/main/catalog.json"; // Placeholder URL
    private static readonly HttpClient _http = new();

    // Synchronous fallback/cached access
    public static IReadOnlyList<ModelCatalogEntry> Models => _models ?? LoadEmbedded();

    public static async Task RefreshAsync()
    {
        try
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("NovaLite/1.0");
            var json = await _http.GetStringAsync(CatalogUrl);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var fetched = JsonSerializer.Deserialize<List<ModelCatalogEntry>>(json, options);
            if (fetched != null && fetched.Count > 0)
            {
                _models = fetched;
                return;
            }
        }
        catch
        {
            // Fallback to embedded on failure
        }
        
        _models = LoadEmbedded();
    }

    private static IReadOnlyList<ModelCatalogEntry> LoadEmbedded()
    {
        var assembly = typeof(ModelCatalog).Assembly;
        var resourceName = "NovaLite.Core.Setup.catalog.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return new List<ModelCatalogEntry>();

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<List<ModelCatalogEntry>>(stream, options) 
                  ?? new List<ModelCatalogEntry>();
    }
}
