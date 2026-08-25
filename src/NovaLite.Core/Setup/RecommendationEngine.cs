namespace NovaLite.Setup;

public record RecommendedModel(
    ModelCatalogEntry Model,
    int StarRating,
    string Reason,
    HardwareProfile Hardware,
    bool IsRecommended = false,
    bool IsGpuRecommendation = false
)
{
    public string DisplayName => IsRecommended ? $"{Model.Name} (Recommended)" : Model.Name;
}

public class RecommendationEngine
{
    public IReadOnlyList<RecommendedModel> GetRecommendations(HardwareProfile profile)
    {
        var recommendations = new List<RecommendedModel>();
        // Defensive: snapshot catalog to protect against concurrent mutations
        var catalogSnapshot = ModelCatalog.Models?.ToList() ?? new List<ModelCatalogEntry>();
        bool hasCapableGpu = profile.HasDedicatedGpu && profile.TotalVRamMB >= 6000;
        
        long reservedRam = 6144;
        if (profile.TotalRamMB <= 8500)
            reservedRam = 3072;
        else if (profile.TotalRamMB >= 16000)
            reservedRam = 6144; // Leave 6GB for Windows and background apps

        long usableSysRam = profile.TotalRamMB - reservedRam;
        if (usableSysRam < 1024) usableSysRam = 1024;

        // Usable VRAM (leave ~1GB for display/OS rendering if dedicated)
        long usableVRam = hasCapableGpu ? profile.TotalVRamMB - 1024 : 0;
        if (usableVRam < 0) usableVRam = 0;

        foreach (var model in catalogSnapshot)
        {
            // Always estimate memory based on actual runtime operational requirement (RecommendedRamMB)
            var estimatedMemoryMB = model.RecommendedRamMB > 0
                ? model.RecommendedRamMB
                : (model.DownloadSizeBytes is long sizeBytes && sizeBytes > 0
                    ? (long)Math.Ceiling(sizeBytes / 1024.0 / 1024.0 * 1.5)
                    : 4096);

            // 1. Filter out models whose minimum requirement exceeds total available memory
            if (model.MinRamMB > usableSysRam + usableVRam)
                continue;

            // Tiny models (<= 1GB RAM) should only be recommended on low-RAM systems (~4GB) unless GPU-accelerated
            if (estimatedMemoryMB <= 1024 && profile.TotalRamMB > 8500 && !hasCapableGpu)
                continue;

            // 2. Filter out models whose estimated size exceeds usable system RAM
            bool willFitInVram = hasCapableGpu && estimatedMemoryMB <= usableVRam;
            if (!willFitInVram && estimatedMemoryMB > usableSysRam)
                continue;

            // 3. For systems with ~4GB RAM or less, only recommend models around 1.5GB
            if (profile.TotalRamMB <= 4500 && estimatedMemoryMB > 1536)
                continue;

            bool perfectlyFitsInVRam = hasCapableGpu && estimatedMemoryMB <= (usableVRam * 0.85);
            bool fitsInVRam = hasCapableGpu && estimatedMemoryMB <= usableVRam;
            bool mostlyFitsInVRam = hasCapableGpu && model.MinRamMB <= usableVRam;
            bool perfectlyFitsInSysRam = estimatedMemoryMB <= (usableSysRam * 0.5); // Uses <= 50% usable RAM (plenty of headroom)
            bool fitsInSysRam = estimatedMemoryMB <= (usableSysRam * 0.8);
            bool mostlyFitsInSysRam = model.MinRamMB <= usableSysRam;

            int stars = 1;
            string reason = "";

            // Score based on speed and headroom
            if (perfectlyFitsInVRam)
            {
                stars = 5;
                reason = "Optimal performance. Fully utilizes your dedicated graphics card with plenty of VRAM to spare.";
            }
            else if (fitsInVRam)
            {
                stars = 4;
                reason = "Great performance. Fits in your dedicated graphics card but uses almost all of your available VRAM.";
            }
            else if (mostlyFitsInVRam)
            {
                stars = 3;
                reason = "Good performance. Leverages your dedicated graphics card but may occasionally use system memory.";
            }
            else if (perfectlyFitsInSysRam)
            {
                stars = 5;
                reason = "Optimal performance. Ultra-lightweight model that runs fast with minimal memory footprint (leaves plenty of RAM free).";
            }
            else if (fitsInSysRam)
            {
                stars = 4;
                reason = "Great performance. Runs reliably using system memory with comfortable headroom for other applications.";
            }
            else if (mostlyFitsInSysRam)
            {
                stars = 2;
                reason = "Basic performance. Uses a significant portion of system RAM (6-9GB), which may leave less headroom for other applications.";
            }
            else
            {
                stars = 1;
                reason = "Heavy memory footprint. High RAM usage across system memory.";
            }

            // Penalty for CPU-heavy models on CPU-only machines
            if (!model.CpuFriendly && !profile.HasDedicatedGpu && stars > 1)
            {
                stars--;
                reason += " (CPU intensive).";
            }

            if (stars < 1) stars = 1;
            if (stars > 5) stars = 5;

            bool isGpuRecommendation = perfectlyFitsInVRam || fitsInVRam || mostlyFitsInVRam;
            recommendations.Add(new RecommendedModel(model, stars, reason, profile, false, isGpuRecommendation));
        }

        // Sort by Stars (speed/fit), then Tier (capability), then Size
        var sorted = recommendations
            .OrderByDescending(r => r.StarRating)
            .ThenByDescending(r => r.Model.TargetTier)
            .ThenByDescending(r => r.Model.DownloadSizeBytes ?? 0L)
            .ToList();

        var finalModels = sorted;

        try
        {
            for (int i = 0; i < finalModels.Count; i++)
            {
                finalModels[i] = finalModels[i] with { IsRecommended = false };
            }

            if (finalModels.Count > 0)
            {
                finalModels[0] = finalModels[0] with { IsRecommended = true };
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Defensive: if something changed concurrently, return a safe empty list
            return new List<RecommendedModel>();
        }

        return finalModels;
    }
}
