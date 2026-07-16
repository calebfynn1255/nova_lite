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

        // Determine available memory for the model
        long availableMemMB = 0;
        bool hasDedicatedGpu = profile.TotalVRamMB > 512; // Basic check for dedicated GPU

        if (hasDedicatedGpu)
        {
            // If there's a dedicated GPU, we try to fit models mostly into VRAM
            // E.g., 16GB VRAM -> leave 1-2GB for OS -> ~14GB available. Let's use 85% of VRAM.
            availableMemMB = (long)(profile.TotalVRamMB * 0.85);
        }
        else
        {
            // If no dedicated GPU, use System RAM but leave 4GB (4096MB) for Windows.
            availableMemMB = profile.TotalRamMB - 4096;
            if (availableMemMB < 1024) availableMemMB = 1024; // Ensure at least 1GB headroom for minimal models
        }

        foreach (var model in ModelCatalog.Models)
        {
            int stars = 1;
            string reason = "";

            if (availableMemMB >= model.RecommendedRamMB)
            {
                stars = 5;
                reason = "Perfect fit for your hardware.";
            }
            else if (availableMemMB >= model.MinRamMB)
            {
                stars = 4;
                reason = "Good fit, but might push memory limits slightly.";
            }
            else if (profile.TotalRamMB >= model.MinRamMB)
            {
                stars = 3;
                reason = "Will run, but will consume most of your available system RAM.";
            }
            else
            {
                stars = 1;
                reason = "Not recommended. Your system has less than the minimum required memory.";
            }

            // Downgrade if it's not CPU friendly and we have no dedicated GPU
            if (!model.CpuFriendly && !hasDedicatedGpu)
            {
                stars--;
                reason += " (Model is heavy on CPU).";
            }

            // Bound stars
            if (stars < 1) stars = 1;
            if (stars > 5) stars = 5;

            recommendations.Add(new RecommendedModel(model, stars, reason, profile));
        }

        // Sort by stars descending, then by name
        var sorted = recommendations.OrderByDescending(r => r.StarRating).ThenBy(r => r.Model.Name).ToList();
        
        if (sorted.Count > 0)
        {
            sorted[0] = sorted[0] with { IsRecommended = true };
        }

        return sorted;
    }
}
