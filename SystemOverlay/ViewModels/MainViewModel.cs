using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using SystemOverlay.Models;
using SystemOverlay.Services;

namespace SystemOverlay.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly HardwareService _hardware;
    private AppSettings _settings;
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private double _windowOpacity = 1.0;

    [ObservableProperty]
    private Thickness _windowBorderThickness = new(0);

    [ObservableProperty]
    private System.Windows.Media.Brush _windowBorderBrush = System.Windows.Media.Brushes.Transparent;

    // CPU Metrics
    [ObservableProperty] private string _cpuText = "0%";
    [ObservableProperty] private double _cpuValue = 0;
    [ObservableProperty] private string _cpuTempText = "CPU";
    [ObservableProperty] private System.Windows.Media.Brush _cpuBarForeground = System.Windows.Media.Brushes.Cyan;
    public ObservableCollection<ProcessMonitorService.ProcessUsage> CpuProcesses { get; } = new();

    // GPU Metrics
    [ObservableProperty] private string _gpuText = "0%";
    [ObservableProperty] private double _gpuValue = 0;
    [ObservableProperty] private string _gpuTempText = "GPU";
    [ObservableProperty] private System.Windows.Media.Brush _gpuBarForeground = System.Windows.Media.Brushes.LightGreen;
    public ObservableCollection<ProcessMonitorService.ProcessUsage> GpuProcesses { get; } = new();

    // RAM Metrics
    [ObservableProperty] private string _ramText = "0 / 0 GB (0%)";
    [ObservableProperty] private double _ramValue = 0;
    [ObservableProperty] private System.Windows.Media.Brush _ramBarForeground = System.Windows.Media.Brushes.Orange;
    public ObservableCollection<ProcessMonitorService.ProcessUsage> RamProcesses { get; } = new();

    // Network Metrics
    [ObservableProperty] private string _netUpText = "↑ 0 KB/s";
    [ObservableProperty] private string _netDownText = "↓ 0 KB/s";

    // Disks
    public ObservableCollection<DiskInfo> Disks { get; } = new();

    // Expand state
    public bool CpuExpanded { get; set; }
    public bool GpuExpanded { get; set; }
    public bool RamExpanded { get; set; }

    public MainViewModel(HardwareService hardware, AppSettings settings)
    {
        _hardware = hardware;
        _settings = settings;

        // Initialize Brushes
        UpdateColors();
    }

    public void UpdateSettings(AppSettings newSettings)
    {
        bool intervalChanged = _settings.UpdateIntervalMs != newSettings.UpdateIntervalMs;
        _settings = newSettings;
        
        UpdateColors();

        if (intervalChanged)
        {
            RestartPolling();
        }
    }

    private static readonly System.Windows.Media.SolidColorBrush CyanB = new(System.Windows.Media.Color.FromRgb(0x00, 0xD4, 0xFF));
    private static readonly System.Windows.Media.SolidColorBrush GreenB = new(System.Windows.Media.Color.FromRgb(0x00, 0xFF, 0x88));
    private static readonly System.Windows.Media.SolidColorBrush OrangeB = new(System.Windows.Media.Color.FromRgb(0xFF, 0x88, 0x00));
    private static readonly System.Windows.Media.SolidColorBrush AlertB = new(System.Windows.Media.Color.FromRgb(0xFF, 0x22, 0x44));

    private void UpdateColors()
    {
        CpuBarForeground = CyanB;
        GpuBarForeground = GreenB;
        RamBarForeground = OrangeB;
    }

    public void StartPolling()
    {
        _cts = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_settings.UpdateIntervalMs));
        
        // Fire and forget background task
        _ = PollMetricsAsync(_cts.Token);
    }

    private void RestartPolling()
    {
        StopPolling();
        StartPolling();
    }

    public void StopPolling()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _timer?.Dispose();
        _timer = null;
    }

    private async Task PollMetricsAsync(CancellationToken token)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(token))
            {
                if (token.IsCancellationRequested) break;

                // 1. Get metrics in background thread (Ring0 calls wait here without blocking UI)
                var metrics = _hardware.GetMetrics();
                var disks = HardwareService.GetDiskSpaces();
                
                var topCpu = CpuExpanded ? ProcessMonitorService.GetTopCpuProcesses(3) : null;
                var topRam = RamExpanded ? ProcessMonitorService.GetTopRamProcesses(3) : null;
                var topGpu = GpuExpanded ? ProcessMonitorService.GetTopGpuProcesses(3) : null;

                // 2. Dispatch the results to UI thread bindings
                _ = Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    UpdateUI(metrics, disks, topCpu, topRam, topGpu);
                });
            }
        }
        catch (OperationCanceledException) { /* expected */ }
        catch (Exception) { /* log if needed */ }
    }

    private void UpdateUI(HardwareMetrics m, List<DiskInfo> disks, 
        List<ProcessMonitorService.ProcessUsage>? topCpu, 
        List<ProcessMonitorService.ProcessUsage>? topRam,
        List<ProcessMonitorService.ProcessUsage>? topGpu)
    {
        bool hasAlert = false;
        var alertBrush = AlertB;

        // CPU
        CpuText = $"{m.CpuUsage:F0}%";
        CpuValue = m.CpuUsage;
        CpuTempText = m.CpuTemperature > 0 ? $"{m.CpuName}  •  {m.CpuTemperature:F0}°C" : m.CpuName;

        if (_settings.AlertsEnabled && (m.CpuUsage >= _settings.CpuUsageThreshold || (m.CpuTemperature > 0 && m.CpuTemperature >= _settings.CpuTempThreshold)))
            hasAlert = true;

        CpuBarForeground = (_settings.AlertsEnabled && m.CpuUsage >= _settings.CpuUsageThreshold)
            ? alertBrush : CyanB;

        // GPU
        GpuText = $"{m.GpuUsage:F0}%";
        GpuValue = m.GpuUsage;
        GpuTempText = m.GpuTemperature > 0 ? $"{m.GpuName}  •  {m.GpuTemperature:F0}°C" : m.GpuName;

        if (_settings.AlertsEnabled && (m.GpuUsage >= _settings.GpuUsageThreshold || (m.GpuTemperature > 0 && m.GpuTemperature >= _settings.GpuTempThreshold)))
            hasAlert = true;

        GpuBarForeground = (_settings.AlertsEnabled && m.GpuUsage >= _settings.GpuUsageThreshold)
            ? alertBrush : GreenB;

        // RAM
        RamText = $"{m.RamUsedGb:F1} / {m.RamTotalGb:F1} GB  ({m.RamUsagePercent:F0}%)";
        RamValue = m.RamUsagePercent;

        if (_settings.AlertsEnabled && m.RamUsagePercent >= _settings.RamUsageThreshold)
            hasAlert = true;

        RamBarForeground = (_settings.AlertsEnabled && m.RamUsagePercent >= _settings.RamUsageThreshold)
            ? alertBrush : OrangeB;

        // Network
        NetUpText = $"↑ {FormatSpeed(m.NetUploadKBs)}";
        NetDownText = $"↓ {FormatSpeed(m.NetDownloadKBs)}";

        // Disks
        Disks.Clear();
        foreach (var disk in disks) Disks.Add(disk);

        // Processes
        if (topCpu != null) SyncCollection(CpuProcesses, topCpu);
        if (topRam != null) SyncCollection(RamProcesses, topRam);
        if (topGpu != null) SyncCollection(GpuProcesses, topGpu);

        // Alert
        HandleAlertState(hasAlert);
    }

    private void SyncCollection(ObservableCollection<ProcessMonitorService.ProcessUsage> target, List<ProcessMonitorService.ProcessUsage> source)
    {
        target.Clear();
        foreach (var item in source) target.Add(item);
    }

    private static string FormatSpeed(float kbs)
    {
        if (kbs >= 1024) return $"{kbs / 1024f:F1} MB/s";
        return $"{kbs:F0} KB/s";
    }

    // Handled by MainWindow.xaml.cs for the animation layer, but we toggle states here to alert
    public event EventHandler<bool>? AlertStateChanged;
    private bool _isAlertActive = false;

    private void HandleAlertState(bool hasAlert)
    {
        if (hasAlert && !_isAlertActive)
        {
            _isAlertActive = true;
            AlertStateChanged?.Invoke(this, true);
        }
        else if (!hasAlert && _isAlertActive)
        {
            _isAlertActive = false;
            AlertStateChanged?.Invoke(this, false);
        }
    }

    public void Dispose()
    {
        StopPolling();
        GC.SuppressFinalize(this);
    }
}
