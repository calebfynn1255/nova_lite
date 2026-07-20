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
            reservedRam = 4096;
        else if (profile.TotalRamMB >= 16000)
            reservedRam = 8192; // Leave 8GB for Windows and apps

        long usableSysRam = profile.TotalRamMB - reservedRam;
        if (usableSysRam < 1024) usableSysRam = 1024;

        // Usable VRAM (leave ~1GB for display/OS rendering if dedicated)
        long usableVRam = hasCapableGpu ? profile.TotalVRamMB - 1024 : 0;
        if (usableVRam < 0) usableVRam = 0;

        foreach (var model in catalogSnapshot)
        {
            // Use the model download size (in MB) when available; otherwise fall back to RecommendedRamMB
            var estimatedMemoryMB = model.DownloadSizeBytes is long sizeBytes && sizeBytes > 0
                ? (long)Math.Ceiling(sizeBytes / 1024.0 / 1024.0)
                : model.RecommendedRamMB;

            // 1. Filter out models whose minimum requirement exceeds total available memory
            if (model.MinRamMB > usableSysRam + usableVRam)
                continue;

            // Tiny models (<= 1GB) should only be recommended on very low-RAM systems (~4GB)
            if (estimatedMemoryMB <= 1024 && profile.TotalRamMB > 4500)
                continue;

            // 2. Filter out models whose estimated size exceeds usable system RAM
            bool willFitInVram = hasCapableGpu && estimatedMemoryMB <= usableVRam;
            if (!willFitInVram && estimatedMemoryMB > usableSysRam)
                continue;

            // 3. Filter out models that are WAY too small for high-end systems
            if (usableSysRam >= 16384 && estimatedMemoryMB <= 2048)
                continue;

            // 4. For systems with ~4GB RAM or less, only recommend models around 1GB
            if (profile.TotalRamMB <= 4500 && estimatedMemoryMB > 1536)
                continue;

            bool perfectlyFitsInVRam = hasCapableGpu && estimatedMemoryMB <= (usableVRam * 0.85);
            bool fitsInVRam = hasCapableGpu && estimatedMemoryMB <= usableVRam;
            bool mostlyFitsInVRam = hasCapableGpu && model.MinRamMB <= usableVRam;
            bool perfectlyFitsInSysRam = estimatedMemoryMB <= (usableSysRam * 0.7);
            bool fitsInSysRam = estimatedMemoryMB <= usableSysRam;
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
                stars = 4;
                reason = "Good performance. Runs reliably using your system's memory with plenty of headroom for other applications.";
                if (!profile.HasDedicatedGpu && model.CpuFriendly)
                {
                    stars = 5;
                    reason = "Optimal performance. CPU-friendly model tailored for systems without dedicated graphics.";
                }
                else if (hasCapableGpu)
                {
                    stars = 2; // Penalize so it sorts below models that fit in VRAM
                    reason = "Adequate performance. Runs entirely in your system's memory (too large for graphics card).";
                }
            }
            else if (fitsInSysRam)
            {
                stars = 3;
                reason = "Adequate performance. Runs in your system's memory but leaves less headroom for other applications.";
                if (!profile.HasDedicatedGpu && model.CpuFriendly)
                {
                    stars = 4;
                    reason = "Great performance. CPU-friendly model but uses most of your usable memory.";
                }
                else if (hasCapableGpu)
                {
                    stars = 2; // Penalize so it sorts below models that fit in VRAM
                    reason = "Basic performance. Runs entirely in your system's memory (too large for graphics card).";
                }
            }
            else if (mostlyFitsInSysRam)
            {
                stars = 2;
                reason = "Basic performance. Uses a significant portion of your system's memory and may run slowly.";
                if (hasCapableGpu)
                {
                    stars = 1; // Heavy penalty
                    reason = "Basic performance. Uses a significant portion of system memory (too large for graphics card).";
                }
            }
            else
            {
                stars = 2;
                reason = "Basic performance. Heavily relies on available memory across your system, which may result in slower generation times.";
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

        // Choose up to N recommendations (fewer if not enough fit). Prioritize by star rating,
        // CPU-friendliness, capability tier, and model size (already handled in sorting).
        const int MaxRecommendations = 5;
        var finalModels = sorted.Take(Math.Min(MaxRecommendations, sorted.Count)).ToList();

        // TESTING HOOK: force-include "Llama 3.2 1B" in recommendations for every PC.
        // Temporary: included for testing purposes only — remove later as needed.
        var testModel = catalogSnapshot.FirstOrDefault(m => string.Equals(m.Name, "Llama 3.2 1B", System.StringComparison.OrdinalIgnoreCase));
        if (testModel != null && !finalModels.Any(r => string.Equals(r.Model.Name, testModel.Name, System.StringComparison.OrdinalIgnoreCase)))
        {
            finalModels.Add(new RecommendedModel(testModel, 3, "Test override: included for testing", profile, false, false));
            if (finalModels.Count > MaxRecommendations)
                finalModels = finalModels.Take(MaxRecommendations).ToList();
        }

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
