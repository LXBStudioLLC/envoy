using System.Runtime.InteropServices;

namespace Envoy.Core.Services;

public record HardwareProfile
{
    public int TotalVramMB { get; set; }
    public int AvailableVramMB { get; set; }
    public string GpuName { get; set; } = string.Empty;
    public bool HasGpu { get; set; }
    public long TotalSystemRamMB { get; set; }
    public string RecommendedModel { get; set; } = string.Empty;
    public string RecommendedQuantization { get; set; } = string.Empty;
}

public class HardwareProfiler
{
    public HardwareProfile DetectHardware()
    {
        var profile = new HardwareProfile();

        profile.TotalSystemRamMB = GetTotalSystemRamMB();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            DetectWindowsGpu(profile);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            DetectMacGpu(profile);
        }
        else
        {
            DetectLinuxGpu(profile);
        }

        CalculateRecommendation(profile);
        return profile;
    }

    private long GetTotalSystemRamMB()
    {
        try
        {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            return gcMemoryInfo.TotalAvailableMemoryBytes / (1024 * 1024);
        }
        catch
        {
            return 8192;
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void DetectWindowsGpu(HardwareProfile profile)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            using var results = searcher.Get();
            foreach (System.Management.ManagementObject obj in results)
            {
                using (obj)
                {
                    var adapterRam = Convert.ToUInt64(obj["AdapterRAM"]);
                    profile.TotalVramMB += (int)(adapterRam / (1024 * 1024));
                    profile.GpuName = obj["Name"]?.ToString() ?? "Unknown";
                }
            }
            profile.HasGpu = profile.TotalVramMB > 0;
        }
        catch
        {
            profile.HasGpu = false;
        }
    }

    private void DetectMacGpu(HardwareProfile profile)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "system_profiler",
                    Arguments = "SPDisplaysDataType -json",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (output.Contains("VRAM"))
            {
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("VRAM") && line.Contains(":"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 1 && int.TryParse(parts[1].Trim().Split(' ')[0], out var vram))
                        {
                            profile.TotalVramMB = vram;
                        }
                    }
                }
            }
            profile.HasGpu = profile.TotalVramMB > 0;
            if (profile.HasGpu) profile.GpuName = "Apple Silicon / AMD";
        }
        catch
        {
            profile.HasGpu = false;
        }
    }

    private void DetectLinuxGpu(HardwareProfile profile)
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "nvidia-smi",
                    Arguments = "--query-gpu=memory.total,name --format=csv,noheader,nounits",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (!string.IsNullOrEmpty(output))
            {
                var parts = output.Split(',');
                if (parts.Length >= 2)
                {
                    if (int.TryParse(parts[0].Trim(), out var vram))
                    {
                        profile.TotalVramMB = vram;
                    }
                    profile.GpuName = parts[1].Trim();
                }
                profile.HasGpu = true;
            }
        }
        catch
        {
            profile.HasGpu = false;
        }
    }

    private void CalculateRecommendation(HardwareProfile profile)
    {
        if (profile.TotalVramMB >= 24000)
        {
            profile.RecommendedModel = "glm-5.1";
            profile.RecommendedQuantization = "Q4_K_M";
        }
        else if (profile.TotalVramMB >= 12000)
        {
            profile.RecommendedModel = "qwen2.5-coder:14b";
            profile.RecommendedQuantization = "Q4_K_M";
        }
        else if (profile.TotalVramMB >= 8000)
        {
            profile.RecommendedModel = "gemma4:9b";
            profile.RecommendedQuantization = "Q4_K_M";
        }
        else if (profile.TotalVramMB >= 4000)
        {
            profile.RecommendedModel = "llama3.1:8b";
            profile.RecommendedQuantization = "Q3_K_S";
        }
        else
        {
            profile.RecommendedModel = "llama3.1:8b";
            profile.RecommendedQuantization = "Q3_K_S";
        }
    }
}
