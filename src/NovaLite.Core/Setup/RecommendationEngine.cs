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

        foreach (var model in ModelCatalog.Models)
        {
            // 1. Filter out models whose minimum requirement exceeds total available memory
            if (model.MinRamMB > usableSysRam + usableVRam)
                continue;

            // 2. Filter out RAM models whose recommended size exceeds usable system RAM
            //    (e.g. don't show a 32GB model on a 32GB system - Windows takes 8GB leaving only 24GB)
            bool willFitInVram = hasCapableGpu && model.RecommendedRamMB <= usableVRam;
            if (!willFitInVram && model.RecommendedRamMB > usableSysRam)
                continue;

            // 3. Filter out models that are WAY too small for high-end systems
            // E.g., if you have 16GB+ RAM, we don't need to show tiny 1GB models
            if (usableSysRam >= 16384 && model.RecommendedRamMB <= 2048)
                continue;

            // 4. For systems with ~4GB RAM or less, only recommend models around 1GB
            if (profile.TotalRamMB <= 4500 && model.RecommendedRamMB > 1536)
                continue;

            bool perfectlyFitsInVRam = hasCapableGpu && model.RecommendedRamMB <= (usableVRam * 0.85);
            bool fitsInVRam = hasCapableGpu && model.RecommendedRamMB <= usableVRam;
            bool mostlyFitsInVRam = hasCapableGpu && model.MinRamMB <= usableVRam;
            bool perfectlyFitsInSysRam = model.RecommendedRamMB <= (usableSysRam * 0.7);
            bool fitsInSysRam = model.RecommendedRamMB <= usableSysRam;
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
            .ThenByDescending(r => r.Model.RecommendedRamMB)
            .ToList();

        var finalModels = new List<RecommendedModel>();

        if (hasCapableGpu)
        {
            // 1. Top model should be the best VRAM model
            var bestGpuModel = sorted.FirstOrDefault(m => m.IsGpuRecommendation);
            if (bestGpuModel != null)
            {
                finalModels.Add(bestGpuModel);
                sorted.Remove(bestGpuModel);
            }
            
            // 2. Next can be best RAM model
            var bestRamModel = sorted.FirstOrDefault(m => !m.IsGpuRecommendation);
            if (bestRamModel != null)
            {
                finalModels.Add(bestRamModel);
                sorted.Remove(bestRamModel);
            }
            
            // 3. The other 2 follow
            finalModels.AddRange(sorted.Take(4 - finalModels.Count));
        }
        else
        {
            // Just recommend RAM models
            var ramModels = sorted.Where(m => !m.IsGpuRecommendation).ToList();
            if (ramModels.Count >= 4)
                finalModels.AddRange(ramModels.Take(4));
            else
                finalModels.AddRange(sorted.Take(4));
        }

        for (int i = 0; i < finalModels.Count; i++)
        {
            finalModels[i] = finalModels[i] with { IsRecommended = false };
        }

        if (finalModels.Count > 0)
        {
            finalModels[0] = finalModels[0] with { IsRecommended = true };
        }

        return finalModels;
    }
}
