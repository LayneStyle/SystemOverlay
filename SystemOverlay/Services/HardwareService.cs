using System.IO;
using LibreHardwareMonitor.Hardware;
using SystemOverlay.Models;

namespace SystemOverlay.Services;

public class HardwareService : IDisposable
{
    private readonly Computer _computer;

    public HardwareService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsNetworkEnabled = true,
            IsStorageEnabled = true
        };
        _computer.Open();
    }

    public HardwareMetrics GetMetrics()
    {
        var metrics = new HardwareMetrics();

        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Update();

            switch (hardware.HardwareType)
            {
                case HardwareType.Cpu:
                    ReadCpu(hardware, metrics);
                    break;

                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    ReadGpu(hardware, metrics);
                    break;

                case HardwareType.Memory:
                    ReadRam(hardware, metrics);
                    break;

                case HardwareType.Network:
                    ReadNetwork(hardware, metrics);
                    break;

                case HardwareType.Storage:
                    ReadStorage(hardware, metrics);
                    break;
            }
        }

        return metrics;
    }

    private static void ReadCpu(IHardware hw, HardwareMetrics m)
    {
        m.CpuName = ShortenName(hw.Name);
        foreach (var sensor in hw.Sensors)
        {
            if (sensor.SensorType == SensorType.Load && sensor.Name == "CPU Total")
                m.CpuUsage = sensor.Value ?? 0;
            if (sensor.SensorType == SensorType.Temperature && sensor.Name.Contains("Package"))
                m.CpuTemperature = sensor.Value ?? 0;
        }
        // Fallback: if no "Package" temp, grab first temperature sensor
        if (m.CpuTemperature == 0)
        {
            foreach (var sensor in hw.Sensors)
            {
                if (sensor.SensorType == SensorType.Temperature && sensor.Value > 0)
                {
                    m.CpuTemperature = sensor.Value ?? 0;
                    break;
                }
            }
        }
    }

    private static void ReadGpu(IHardware hw, HardwareMetrics m)
    {
        m.GpuName = ShortenName(hw.Name);
        foreach (var sensor in hw.Sensors)
        {
            if (sensor.SensorType == SensorType.Load && sensor.Name == "GPU Core")
                m.GpuUsage = sensor.Value ?? 0;
            if (sensor.SensorType == SensorType.Temperature && sensor.Name == "GPU Core")
                m.GpuTemperature = sensor.Value ?? 0;
        }
    }

    private static void ReadRam(IHardware hw, HardwareMetrics m)
    {
        foreach (var sensor in hw.Sensors)
        {
            if (sensor.SensorType == SensorType.Data && sensor.Name == "Memory Used")
                m.RamUsedGb = sensor.Value ?? 0;
            if (sensor.SensorType == SensorType.Data && sensor.Name == "Memory Available")
                m.RamTotalGb = (sensor.Value ?? 0) + m.RamUsedGb;
            if (sensor.SensorType == SensorType.Load && sensor.Name == "Memory")
                m.RamUsagePercent = sensor.Value ?? 0;
        }
    }

    private static void ReadNetwork(IHardware hw, HardwareMetrics m)
    {
        foreach (var sensor in hw.Sensors)
        {
            if (sensor.SensorType == SensorType.Throughput && sensor.Name == "Upload Speed")
                m.NetUploadKBs += (sensor.Value ?? 0) / 1024f;
            if (sensor.SensorType == SensorType.Throughput && sensor.Name == "Download Speed")
                m.NetDownloadKBs += (sensor.Value ?? 0) / 1024f;
        }
    }

    private static void ReadStorage(IHardware hw, HardwareMetrics m)
    {
        // Only get logical drives via DriveInfo for space data
        // LibreHardwareMonitor storage gives physical disk temp/health, not space
    }

    private static readonly Dictionary<string, System.Diagnostics.PerformanceCounter> _readCounters = new();
    private static readonly Dictionary<string, System.Diagnostics.PerformanceCounter> _writeCounters = new();

    /// <summary>
    /// Get disk space info and I/O rates from the OS
    /// </summary>
    public static List<DiskInfo> GetDiskSpaces()
    {
        var disks = new List<DiskInfo>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                continue;

            var driveName = drive.Name.TrimEnd('\\');
            var totalGb = drive.TotalSize / (1024f * 1024f * 1024f);
            var freeGb = drive.AvailableFreeSpace / (1024f * 1024f * 1024f);
            
            float readMBps = 0;
            float writeMBps = 0;

            try
            {
                // Format for counter is physical disk (e.g. "0 C:") but logical disk "C:" also exists in "LogicalDisk" category
                if (!_readCounters.ContainsKey(driveName))
                {
                    _readCounters[driveName] = new System.Diagnostics.PerformanceCounter("LogicalDisk", "Disk Read Bytes/sec", driveName);
                    _writeCounters[driveName] = new System.Diagnostics.PerformanceCounter("LogicalDisk", "Disk Write Bytes/sec", driveName);
                    _readCounters[driveName].NextValue();
                    _writeCounters[driveName].NextValue();
                }
                else
                {
                    readMBps = _readCounters[driveName].NextValue() / (1024f * 1024f);
                    writeMBps = _writeCounters[driveName].NextValue() / (1024f * 1024f);
                }
            }
            catch
            {
                // Performance counters might be broken on the user's system, ignore silently
            }

            disks.Add(new DiskInfo
            {
                Name = driveName,
                TotalGb = totalGb,
                UsedGb = totalGb - freeGb,
                ReadMBps = readMBps,
                WriteMBps = writeMBps
            });
        }
        return disks;
    }

    private static string ShortenName(string name)
    {
        // Trim verbose names like "Intel Core i7-12700K" to something readable
        if (name.Length > 30)
            return name[..30] + "…";
        return name;
    }

    public void Dispose()
    {
        _computer.Close();
        GC.SuppressFinalize(this);
    }
}
