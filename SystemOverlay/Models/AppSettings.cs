namespace SystemOverlay.Models;

public class AppSettings
{
    public double WindowX { get; set; } = 100;
    public double WindowY { get; set; } = 100;
    public bool IsPinned { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowNetwork { get; set; } = true;
    public bool ShowDisk { get; set; } = true;
    public int UpdateIntervalMs { get; set; } = 1000;

    // Appearance
    public double FontScale { get; set; } = 1.0;   // 0.8 – 1.5
    public double OverlayScale { get; set; } = 0;   // Saved window Width in px (0 = use XAML default)
    public bool AutoStart { get; set; }

    // Threshold alerts — overlay forces foreground when exceeded
    public bool AlertsEnabled { get; set; } = true;
    public float CpuUsageThreshold { get; set; } = 90f;
    public float CpuTempThreshold { get; set; } = 95f;
    public float GpuUsageThreshold { get; set; } = 95f;
    public float GpuTempThreshold { get; set; } = 90f;
    public float RamUsageThreshold { get; set; } = 90f;
}
