using SQLite;

namespace NovaLite.Setup;

[Table("HardwareProfiles")]
public class HardwareProfile
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    public DateTime LastScanned { get; set; } = DateTime.UtcNow;

    // A score from 1 (lowest) to 5 (highest)
    public int PerformanceTier { get; set; }

    public string CpuName { get; set; } = string.Empty;
    public int CpuCores { get; set; }
    public long TotalRamBytes { get; set; }
    public string GpuName { get; set; } = string.Empty;
    public long TotalVRamBytes { get; set; }
    public string WindowsVersion { get; set; } = string.Empty;
    public bool SupportsAvx2 { get; set; }
    public bool SupportsAvx512 { get; set; }

    public string GpusJson { get; set; } = "[]";
    public string DrivesJson { get; set; } = "[]";

    [Ignore]
    public List<GpuInfo> Gpus
    {
        get => System.Text.Json.JsonSerializer.Deserialize<List<GpuInfo>>(GpusJson) ?? new();
        set => GpusJson = System.Text.Json.JsonSerializer.Serialize(value);
    }

    [Ignore]
    public List<DiskDriveInfo> Drives
    {
        get => System.Text.Json.JsonSerializer.Deserialize<List<DiskDriveInfo>>(DrivesJson) ?? new();
        set => DrivesJson = System.Text.Json.JsonSerializer.Serialize(value);
    }

    [Ignore]
    public long TotalRamMB => TotalRamBytes / 1024 / 1024;
    
    [Ignore]
    public long TotalVRamMB => TotalVRamBytes / 1024 / 1024;
}

public class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public long VRamBytes { get; set; }
    public long VRamMB => VRamBytes / 1024 / 1024;
    public string DisplayText => VRamMB > 0 ? $"{Name} ({VRamMB} MB VRAM)" : Name;
}

public class DiskDriveInfo
{
    public string Name { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public long TotalFreeSpaceBytes { get; set; }
    public long TotalSizeBytes { get; set; }
    public double FreeSpaceGB => TotalFreeSpaceBytes / 1024.0 / 1024.0 / 1024.0;
    public double TotalSizeGB => TotalSizeBytes / 1024.0 / 1024.0 / 1024.0;
    public string DisplayText => string.IsNullOrEmpty(VolumeLabel)
        ? $"{Name} — {FreeSpaceGB:F1} GB free of {TotalSizeGB:F1} GB"
        : $"{Name} ({VolumeLabel}) — {FreeSpaceGB:F1} GB free of {TotalSizeGB:F1} GB";
}
