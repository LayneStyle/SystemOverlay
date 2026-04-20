namespace SystemOverlay.Models;

public class HardwareMetrics
{
    // CPU
    public float CpuUsage { get; set; }
    public float CpuTemperature { get; set; }
    public string CpuName { get; set; } = "CPU";

    // GPU
    public float GpuUsage { get; set; }
    public float GpuTemperature { get; set; }
    public string GpuName { get; set; } = "GPU";

    // RAM
    public float RamUsedGb { get; set; }
    public float RamTotalGb { get; set; }
    public float RamUsagePercent { get; set; }

    // Network
    public float NetUploadKBs { get; set; }
    public float NetDownloadKBs { get; set; }

    // Disk
    public List<DiskInfo> Disks { get; set; } = [];
}

public class DiskInfo
{
    public string Name { get; set; } = "";
    public float UsedGb { get; set; }
    public float TotalGb { get; set; }
    public float UsagePercent => TotalGb > 0 ? (UsedGb / TotalGb) * 100f : 0f;
    
    public float ReadMBps { get; set; }
    public float WriteMBps { get; set; }
    public bool IsWarning => UsagePercent >= 90f;
}
