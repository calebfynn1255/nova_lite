using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using NovaLite.Core.Helpers;

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
    string ExpectedSha256,
    long? DownloadSizeBytes = null
)
{
    public string DownloadSizeDisplay => DownloadSizeBytes is long bytes && bytes > 0
        ? bytes.ToFileSizeString()
        : "Size unavailable";
}

public static class ModelCatalog
{
    private static IReadOnlyList<ModelCatalogEntry>? _models;
    private static readonly string CatalogUrl = "https://raw.githubusercontent.com/calebfynn1255/novalite-models/main/catalog.json";
    private static readonly HttpClient _http = new();

    // Synchronous fallback/cached access
    public static IReadOnlyList<ModelCatalogEntry> Models => _models ?? LoadEmbedded();

    public static async Task RefreshAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(CatalogUrl);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var remote = JsonSerializer.Deserialize<List<ModelCatalogEntry>>(json, options);
            if (remote != null && remote.Count > 0)
                _models = await EnrichWithDownloadSizesAsync(remote);
            else
                _models = LoadEmbedded();
        }
        catch
        {
            // Network unavailable — fall back to bundled catalog
            _models = LoadEmbedded();
        }
    }

    private static async Task<IReadOnlyList<ModelCatalogEntry>> EnrichWithDownloadSizesAsync(IReadOnlyList<ModelCatalogEntry> models)
    {
        var embedded = LoadEmbedded();
        var fallbackLookup = embedded.ToDictionary(m => m.Name, m => m.DownloadSizeBytes, StringComparer.OrdinalIgnoreCase);

        var enriched = new List<ModelCatalogEntry>(models.Count);
        foreach (var model in models)
        {
            long? sizeBytes = model.DownloadSizeBytes;
            if (sizeBytes is null && fallbackLookup.TryGetValue(model.Name, out var fallbackSize))
                sizeBytes = fallbackSize;

            if (sizeBytes is null)
            {
                foreach (var url in model.DownloadUrls)
                {
                    sizeBytes = await TryResolveDownloadSizeAsync(url);
                    if (sizeBytes is > 0)
                        break;
                }
            }

            enriched.Add(model with { DownloadSizeBytes = sizeBytes });
        }

        return enriched;
    }

    private static async Task<long?> TryResolveDownloadSizeAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(0, 0);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is long contentLength && contentLength > 0)
                return contentLength;

            if (response.Content.Headers.TryGetValues("Content-Length", out var values) &&
                long.TryParse(values.FirstOrDefault(), out var headerLength) && headerLength > 0)
                return headerLength;
        }
        catch
        {
            // Ignore and fall back to the embedded catalog size if the endpoint does not expose it.
        }

        return null;
    }

    private static IReadOnlyList<ModelCatalogEntry> LoadEmbedded()
    {
        var assembly = typeof(ModelCatalog).Assembly;
        var resourceName = "NovaLite.Core.Setup.catalog.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        if (stream != null)
        {
            return JsonSerializer.Deserialize<List<ModelCatalogEntry>>(stream, options)
                   ?? new List<ModelCatalogEntry>();
        }

        // Fallback: attempt to load a local catalog.json next to the running assembly
        try
        {
            var asmPath = AppContext.BaseDirectory ?? Directory.GetCurrentDirectory();
            var candidatePaths = new[]
            {
                Path.Combine(asmPath, "catalog.json"),
                Path.Combine(asmPath, "Setup", "catalog.json"),
                Path.Combine(asmPath, "NovaLite.Core.Setup.catalog.json")
            };

            foreach (var p in candidatePaths)
            {
                if (File.Exists(p))
                {
                    var json = File.ReadAllText(p);
                    return JsonSerializer.Deserialize<List<ModelCatalogEntry>>(json, options)
                           ?? new List<ModelCatalogEntry>();
                }
            }
        }
        catch
        {
            // ignore and fall through to empty list
        }

        return new List<ModelCatalogEntry>();
    }
}
