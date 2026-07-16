using NovaLite.Core.Models;

namespace NovaLite.Setup;

public class PerformanceConfigurator
{
    public InferenceOptions Configure(RecommendedModel recommendation)
    {
        var hw = recommendation.Hardware;
        var options = InferenceOptions.Default;

        // Base threads on available CPU cores
        options.Threads = hw.CpuCores > 4 ? hw.CpuCores - 2 : Math.Max(1, hw.CpuCores - 1);
        
        // Setup GPU offloading based on Tier and VRAM
        if (hw.PerformanceTier >= 4 && hw.TotalVRamMB > 6000)
        {
            options.GpuLayers = 99; // Try to offload everything
            options.BatchSize = 1024;
        }
        else if (hw.PerformanceTier >= 3 && hw.TotalVRamMB > 4000)
        {
            options.GpuLayers = 32;
            options.BatchSize = 512;
        }
        else if (hw.PerformanceTier >= 2 && hw.TotalVRamMB > 2000)
        {
            options.GpuLayers = 16;
            options.BatchSize = 256;
        }
        else
        {
            options.GpuLayers = 0; // CPU only
            options.BatchSize = 128;
        }

        // Adjust Context Window based on total RAM
        if (hw.TotalRamMB > 16000)
            options.MaxTokens = 8192;
        else if (hw.TotalRamMB > 8000)
            options.MaxTokens = 4096;
        else
            options.MaxTokens = 2048;

        return options;
    }
}
