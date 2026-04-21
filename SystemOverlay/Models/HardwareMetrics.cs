using System.ComponentModel;
using System.Runtime.CompilerServices;

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

public class DiskInfo : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _name = "";
    private float _usedGb;
    private float _totalGb;
    private float _readMBps;
    private float _writeMBps;

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public float UsedGb
    {
        get => _usedGb;
        set
        {
            if (Math.Abs(_usedGb - value) > 0.01f)
            {
                _usedGb = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UsagePercent));
                OnPropertyChanged(nameof(IsWarning));
            }
        }
    }

    public float TotalGb
    {
        get => _totalGb;
        set
        {
            if (Math.Abs(_totalGb - value) > 0.01f)
            {
                _totalGb = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UsagePercent));
                OnPropertyChanged(nameof(IsWarning));
            }
        }
    }

    public float UsagePercent => TotalGb > 0 ? (UsedGb / TotalGb) * 100f : 0f;

    public float ReadMBps
    {
        get => _readMBps;
        set { if (Math.Abs(_readMBps - value) > 0.01f) { _readMBps = value; OnPropertyChanged(); } }
    }

    public float WriteMBps
    {
        get => _writeMBps;
        set { if (Math.Abs(_writeMBps - value) > 0.01f) { _writeMBps = value; OnPropertyChanged(); } }
    }

    public bool IsWarning => UsagePercent >= 90f;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
