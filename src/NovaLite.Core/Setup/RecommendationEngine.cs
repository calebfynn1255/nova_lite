namespace NovaLite.Setup;

public record RecommendedModel(
    ModelCatalogEntry Model,
    int StarRating,
    string Reason,
    HardwareProfile Hardware,
    bool IsRecommended = false
)
{
    public string DisplayName => IsRecommended ? $"{Model.Name} (Recommended)" : Model.Name;
}

public class RecommendationEngine
{
    public IReadOnlyList<RecommendedModel> GetRecommendations(HardwareProfile profile)
    {
        var recommendations = new List<RecommendedModel>();
        
        bool hasDedicatedGpu = profile.TotalVRamMB > 512;
        
        // Usable System RAM (leave 4GB for Windows/Apps)
        long usableSysRam = profile.TotalRamMB - 4096;
        if (usableSysRam < 1024) usableSysRam = 1024;

        // Usable VRAM (leave ~1GB for display/OS rendering if dedicated)
        long usableVRam = hasDedicatedGpu ? profile.TotalVRamMB - 1024 : 0;
        if (usableVRam < 0) usableVRam = 0;

        foreach (var model in ModelCatalog.Models)
        {
            // 1. Filter out models that are WAY too big
            if (model.MinRamMB > usableSysRam + usableVRam)
                continue;

            // 2. Filter out models that are WAY too small for high-end systems
            // E.g., if you have 16GB+ RAM, we don't need to show tiny 1GB models
            if (usableSysRam >= 16384 && model.RecommendedRamMB <= 2048)
                continue;

            int stars = 1;
            string reason = "";

            // Score based on speed and headroom
            if (hasDedicatedGpu && model.RecommendedRamMB <= usableVRam)
            {
                stars = 5;
                reason = "Lightning fast! Fits entirely in your dedicated GPU.";
            }
            else if (hasDedicatedGpu && model.MinRamMB <= usableVRam)
            {
                stars = 5;
                reason = "Very fast! Mostly fits in your dedicated GPU.";
            }
            else if (model.RecommendedRamMB <= usableSysRam)
            {
                stars = 4;
                reason = "Perfect fit. Leaves plenty of headroom for Windows and apps.";
                if (!hasDedicatedGpu && model.CpuFriendly) stars = 5; // Best case for CPU-only
            }
            else if (model.MinRamMB <= usableSysRam)
            {
                stars = 3;
                reason = "Good fit, but uses most of your available RAM.";
            }
            else
            {
                stars = 2;
                reason = "Will run, but might slow down your computer.";
            }

            // Penalty for CPU-heavy models on CPU-only machines
            if (!model.CpuFriendly && !hasDedicatedGpu)
            {
                stars--;
                reason += " (Heavy on CPU).";
            }

            if (stars < 1) stars = 1;
            if (stars > 5) stars = 5;

            recommendations.Add(new RecommendedModel(model, stars, reason, profile));
        }

        // Sort by Stars (speed/fit), then Tier (capability), then Size
        var sorted = recommendations
            .OrderByDescending(r => r.StarRating)
            .ThenByDescending(r => r.Model.TargetTier)
            .ThenByDescending(r => r.Model.RecommendedRamMB)
            .ToList();

        // Take only the top 4 best matches
        var finalModels = sorted.Take(4).ToList();
        
        if (finalModels.Count > 0)
        {
            finalModels[0] = finalModels[0] with { IsRecommended = true };
        }

        return finalModels;
    }
}
