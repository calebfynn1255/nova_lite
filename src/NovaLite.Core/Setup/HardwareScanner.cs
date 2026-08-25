using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using Microsoft.Win32;

namespace NovaLite.Setup;

public class HardwareScanner
{
    public async Task<HardwareProfile> ScanAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new PlatformNotSupportedException("Scanner currently only supports Windows.");
        }

        var db = await DatabaseManager.GetConnectionAsync();
        var existing = await db.Table<HardwareProfile>().OrderByDescending(x => x.LastScanned).FirstOrDefaultAsync();

        string cpuName = "Unknown CPU";
        int cpuCores = Environment.ProcessorCount;
        long totalRam = 0;
        string gpuName = "Unknown GPU";
        long totalVram = 0;
        string winVer = RuntimeInformation.OSDescription;

        var gpus = new List<GpuInfo>();
        var drives = new List<DiskDriveInfo>();

        await Task.Run(() => 
        {
            // 1. Scan CPU and RAM via WMI
            try
            {
                using var mcCpu = new ManagementClass("Win32_Processor");
                foreach (var mo in mcCpu.GetInstances().Cast<ManagementObject>())
                {
                    cpuName = mo["Name"]?.ToString()?.Trim() ?? cpuName;
                    break; // Just grab the first CPU
                }

                using var mcRam = new ManagementClass("Win32_ComputerSystem");
                foreach (var mo in mcRam.GetInstances().Cast<ManagementObject>())
                {
                    if (long.TryParse(mo["TotalPhysicalMemory"]?.ToString(), out var ramBytes))
                    {
                        totalRam = ramBytes;
                    }
                    break;
                }
            }
            catch
            {
                // Fallback for RAM if WMI fails (very unlikely on Windows)
                var gcMemInfo = GC.GetGCMemoryInfo();
                totalRam = gcMemInfo.TotalAvailableMemoryBytes;
            }

            // 2. Scan GPU via WMI (VideoController)
            try
            {
                using var mcGpu = new ManagementClass("Win32_VideoController");
                foreach (var mo in mcGpu.GetInstances().Cast<ManagementObject>())
                {
                    var name = mo["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        long vram = 0;
                        if (long.TryParse(mo["AdapterRAM"]?.ToString(), out var ramVal))
                        {
                            vram = ramVal;
                        }

                        // WMI caps AdapterRAM at 4GB. We pull from Registry for accurate >4GB VRAM
                        long regVRam = GetVRamFromRegistry(name);
                        if (regVRam > 0)
                        {
                            vram = regVRam;
                        }

                        // Intel integrated graphics often erroneously report 2047MB or 1GB via WMI/Registry
                        // but their true dedicated VRAM is usually ~128MB. 
                        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) && !name.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                        {
                            vram = 128L * 1024 * 1024;
                        }

                        gpus.Add(new GpuInfo { Name = name, VRamBytes = vram });
                        
                        // Keep the "best" one as primary for legacy properties
                        if (name.Contains("NVIDIA") || name.Contains("AMD") || name.Contains("Radeon") || gpuName == "Unknown GPU")
                        {
                            gpuName = name;
                            totalVram = vram;
                        }
                    }
                }
            }
            catch
            {
                // Ignore WMI errors
            }

            // 3. Scan Drives
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.IsReady && d.DriveType == DriveType.Fixed)
                    {
                        drives.Add(new DiskDriveInfo
                        {
                            Name = d.Name,
                            VolumeLabel = d.VolumeLabel,
                            TotalFreeSpaceBytes = d.TotalFreeSpace,
                            TotalSizeBytes = d.TotalSize
                        });
                    }
                }
            }
            catch { }
        });

        var profile = new HardwareProfile
        {
            CpuName = cpuName,
            CpuCores = cpuCores,
            TotalRamBytes = totalRam,
            GpuName = gpuName,
            TotalVRamBytes = totalVram,
            WindowsVersion = winVer,
            SupportsAvx2 = Avx2.IsSupported,
            SupportsAvx512 = Avx512F.IsSupported,
            Gpus = gpus,
            Drives = drives
        };

        // Calculate Tier (1-5)
        long ramGb = profile.TotalRamMB / 1024;
        long vramGb = profile.TotalVRamMB / 1024;
        long score = (cpuCores) + (ramGb * 2) + (vramGb * 4);

        if (score < 20) profile.PerformanceTier = 1;
        else if (score < 40) profile.PerformanceTier = 2;
        else if (score < 60) profile.PerformanceTier = 3;
        else if (score < 80) profile.PerformanceTier = 4;
        else profile.PerformanceTier = 5;

        // Check if anything changed compared to previous
        if (existing != null &&
            existing.CpuName == profile.CpuName &&
            existing.TotalRamBytes == profile.TotalRamBytes &&
            existing.GpuName == profile.GpuName)
        {
            existing.LastScanned = DateTime.UtcNow;
            existing.TotalVRamBytes = profile.TotalVRamBytes;
            existing.Gpus = profile.Gpus;
            existing.Drives = profile.Drives;
            await db.UpdateAsync(existing);
            return existing;
        }

        await db.InsertAsync(profile);
        return profile;
    }

    private long GetVRamFromRegistry(string adapterName)
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\ControlSet001\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (baseKey != null)
            {
                string normName = new string(adapterName.Where(char.IsLetterOrDigit).ToArray());

                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    using var key = baseKey.OpenSubKey(subKeyName);
                    if (key != null)
                    {
                        var desc = key.GetValue("DriverDesc")?.ToString();
                        if (desc != null)
                        {
                            string normDesc = new string(desc.Where(char.IsLetterOrDigit).ToArray());
                            if (normDesc.Contains(normName, StringComparison.OrdinalIgnoreCase) || normName.Contains(normDesc, StringComparison.OrdinalIgnoreCase))
                            {
                                var qwMem = key.GetValue("HardwareInformation.qwMemorySize");
                                if (qwMem != null)
                                {
                                    if (qwMem is long l) return l;
                                    if (qwMem is byte[] b && b.Length == 8) return BitConverter.ToInt64(b, 0);
                                }
                                
                                var mem = key.GetValue("HardwareInformation.MemorySize");
                                if (mem != null)
                                {
                                    if (mem is int i) return (uint)i;
                                    if (mem is long l) return l;
                                    if (mem is byte[] b && b.Length >= 4) return BitConverter.ToUInt32(b, 0);
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }
        return 0;
    }
}
