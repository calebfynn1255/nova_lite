using System.Net.Http;
using System.Text.Json;

internal class ModelEntry
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long? MinRamMB { get; set; }
    public long? RecommendedRamMB { get; set; }
    public int? TargetTier { get; set; }
    public bool? CpuFriendly { get; set; }
    public bool? SupportsVision { get; set; }
    public string[]? DownloadUrls { get; set; }
    public string? ExpectedSha256 { get; set; }
    public long? DownloadSizeBytes { get; set; }
}

internal class UrlResult
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public int? Status { get; set; }
    public long? ContentLength { get; set; }
}

class Program
{
    static async Task<int> Main(string[] args)
    {
        var inputPath = args.Length > 0 ? args[0] : Path.Combine("..", "..", "src", "NovaLite.Core", "Setup", "catalog.json");
        var outJson = Path.Combine(Directory.GetCurrentDirectory(), "catalog-validation.json");
        var outTxt = Path.Combine(Directory.GetCurrentDirectory(), "catalog-status.txt");
        var backup = inputPath + ".bak";

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Catalog not found at {inputPath}");
            return 2;
        }

        if (File.Exists(backup)) File.Delete(backup);
        File.Copy(inputPath, backup);

        var json = await File.ReadAllTextAsync(inputPath);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var models = JsonSerializer.Deserialize<List<ModelEntry>>(json, opts) ?? new List<ModelEntry>();

        using var handler = new HttpClientHandler { AllowAutoRedirect = true };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

        var results = new List<UrlResult>();
        var validModels = new List<ModelEntry>();

        foreach (var m in models)
        {
            var modelOk = false;
            var sizes = new List<long>();
            if (m.DownloadUrls == null) continue;
            foreach (var u in m.DownloadUrls)
            {
                int? status = null;
                long? cl = null;
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, u);
                    using var resp = await client.SendAsync(req);
                    status = (int)resp.StatusCode;
                    if (resp.Content.Headers.ContentLength.HasValue) cl = resp.Content.Headers.ContentLength.Value;
                    else if (resp.Headers.TryGetValues("Content-Length", out var vals)) cl = long.Parse(vals.First());
                }
                catch
                {
                    try
                    {
                        using var req2 = new HttpRequestMessage(HttpMethod.Get, u);
                        req2.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                        using var resp2 = await client.SendAsync(req2, HttpCompletionOption.ResponseHeadersRead);
                        status = (int)resp2.StatusCode;
                        if (resp2.Content.Headers.ContentLength.HasValue) cl = resp2.Content.Headers.ContentLength.Value;
                        else if (resp2.Headers.TryGetValues("Content-Length", out var vals2)) cl = long.Parse(vals2.First());
                    }
                    catch
                    {
                        status = null;
                    }
                }

                results.Add(new UrlResult { Name = m.Name, Url = u, Status = status, ContentLength = cl });
                if (status == 200 && cl.HasValue && cl.Value > 0)
                {
                    modelOk = true;
                    sizes.Add(cl.Value);
                }
            }

            if (modelOk)
            {
                m.DownloadSizeBytes = sizes.Max();
                validModels.Add(m);
            }
        }

        var writeOpts = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(outJson, JsonSerializer.Serialize(results, writeOpts));
        await File.WriteAllLinesAsync(outTxt, results.Select(r => $"{r.Name} ||| {r.Url} ||| {r.Status} ||| {r.ContentLength}"));
        var filteredPath = Path.Combine(Path.GetDirectoryName(inputPath)!, "catalog.filtered.json");
        await File.WriteAllTextAsync(filteredPath, JsonSerializer.Serialize(validModels, writeOpts));

        // Overwrite original with filtered list (safe: we backed up above)
        await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(validModels, writeOpts));

        Console.WriteLine($"WROTE: {outJson}, {outTxt}, filtered catalog -> {filteredPath}, backup -> {backup}");
        return 0;
    }
}
