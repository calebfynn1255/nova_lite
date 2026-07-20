using System.Text.Json;

record ModelEntry(
    string Name,
    string Description,
    long MinRamMB,
    long RecommendedRamMB,
    int TargetTier,
    bool CpuFriendly,
    bool SupportsVision,
    string[] DownloadUrls,
    string ExpectedSha256,
    long? DownloadSizeBytes
);

record Recommended(string Name, int Stars, string Reason, bool IsGpuRecommendation, long? DownloadSizeBytes, int TargetTier, bool CpuFriendly);

class Program
{
    static void Main(string[] args)
    {
        var catalogPath = args.Length > 0 ? args[0] : Path.Combine("src", "NovaLite.Core", "Setup", "catalog.filtered.json");
        if (!File.Exists(catalogPath))
        {
            Console.Error.WriteLine($"Filtered catalog not found at {catalogPath}");
            Environment.Exit(2);
        }

        var json = File.ReadAllText(catalogPath);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var models = JsonSerializer.Deserialize<List<ModelEntry>>(json, opts) ?? new();

        var profiles = new[] { 4096L, 8192L, 16384L, 32768L };
        var outLines = new List<string>();

        foreach (var ram in profiles)
        {
            outLines.Add($"=== Recommendations for Total RAM {ram/1024} GB ({ram} MB) ===");
            var recs = GetRecommendations(models, ram, 0);
            if (recs.Count == 0) outLines.Add("(no suitable models found)");
            foreach (var r in recs)
            {
                outLines.Add($"{r.Stars}★ - {r.Name} - Tier {r.TargetTier} - Size: {(r.DownloadSizeBytes.HasValue ? (r.DownloadSizeBytes.Value/1024/1024 + " MB") : "unknown")} - CPU-friendly: {r.CpuFriendly} - GPU: {r.IsGpuRecommendation}");
            }
            outLines.Add(string.Empty);
        }

        File.WriteAllLines("recommendations.txt", outLines);
        Console.WriteLine("WROTE recommendations.txt");
    }

    // Ported logic from RecommendationEngine.cs (simplified for standalone use)
    static List<Recommended> GetRecommendations(List<ModelEntry> catalog, long totalRamMB, long totalVramMB)
    {
        var recommendations = new List<Recommended>();
        bool hasCapableGpu = totalVramMB >= 6000;

        long reservedRam = 6144;
        if (totalRamMB <= 8500)
            reservedRam = 4096;
        else if (totalRamMB >= 16000)
            reservedRam = 8192;

        long usableSysRam = totalRamMB - reservedRam;
        if (usableSysRam < 1024) usableSysRam = 1024;

        long usableVRam = hasCapableGpu ? totalVramMB - 1024 : 0;
        if (usableVRam < 0) usableVRam = 0;

        foreach (var model in catalog)
        {
            var estimatedMemoryMB = model.DownloadSizeBytes.HasValue && model.DownloadSizeBytes.Value > 0
                ? (long)Math.Ceiling(model.DownloadSizeBytes.Value / 1024.0 / 1024.0)
                : model.RecommendedRamMB;

            if (model.MinRamMB > usableSysRam + usableVRam) continue;
            if (estimatedMemoryMB <= 1024 && totalRamMB > 4500) continue;
            bool willFitInVram = hasCapableGpu && estimatedMemoryMB <= usableVRam;
            if (!willFitInVram && estimatedMemoryMB > usableSysRam) continue;
            if (usableSysRam >= 16384 && estimatedMemoryMB <= 2048) continue;
            if (totalRamMB <= 4500 && estimatedMemoryMB > 1536) continue;

            bool perfectlyFitsInVRam = hasCapableGpu && estimatedMemoryMB <= (usableVRam * 0.85);
            bool fitsInVRam = hasCapableGpu && estimatedMemoryMB <= usableVRam;
            bool mostlyFitsInVRam = hasCapableGpu && model.MinRamMB <= usableVRam;
            bool perfectlyFitsInSysRam = estimatedMemoryMB <= (usableSysRam * 0.7);
            bool fitsInSysRam = estimatedMemoryMB <= usableSysRam;
            bool mostlyFitsInSysRam = model.MinRamMB <= usableSysRam;

            int stars = 1; string reason = "";
            if (perfectlyFitsInVRam)
            {
                stars = 5; reason = "Optimal performance (fits VRAM).";
            }
            else if (fitsInVRam)
            {
                stars = 4; reason = "Great performance (fits VRAM).";
            }
            else if (mostlyFitsInVRam)
            {
                stars = 3; reason = "Good performance (uses VRAM occasionally).";
            }
            else if (perfectlyFitsInSysRam)
            {
                stars = 4; reason = "Good performance in system RAM.";
                if (!hasCapableGpu && model.CpuFriendly) { stars = 5; reason = "Optimal CPU-friendly model."; }
                else if (hasCapableGpu) { stars = 2; reason = "Runs in system memory (penalized vs VRAM)."; }
            }
            else if (fitsInSysRam)
            {
                stars = 3; reason = "Adequate (in system RAM).";
                if (!hasCapableGpu && model.CpuFriendly) { stars = 4; reason = "Great CPU-friendly model."; }
                else if (hasCapableGpu) { stars = 2; reason = "Basic (in system RAM)."; }
            }
            else if (mostlyFitsInSysRam)
            {
                stars = 2; reason = "Basic; may run slowly.";
                if (hasCapableGpu) { stars = 1; reason = "Basic; too large for GPU."; }
            }
            else
            {
                stars = 2; reason = "May be slow due to memory usage.";
            }

            if (!model.CpuFriendly && !hasCapableGpu && stars > 1)
            {
                stars--; reason += " (CPU intensive).";
            }

            if (stars < 1) stars = 1; if (stars > 5) stars = 5;
            bool isGpuRecommendation = perfectlyFitsInVRam || fitsInVRam || mostlyFitsInVRam;
            recommendations.Add(new Recommended(model.Name, stars, reason, isGpuRecommendation, model.DownloadSizeBytes, model.TargetTier, model.CpuFriendly));
        }

        var sorted = recommendations.OrderByDescending(r => r.Stars).ThenByDescending(r => r.TargetTier).ThenByDescending(r => r.DownloadSizeBytes ?? 0L).ToList();
        var final = sorted.Take(Math.Min(5, sorted.Count)).ToList();
        return final;
    }
}
