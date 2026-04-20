using System.Windows;
using SystemOverlay.Models;
using SystemOverlay.Services;

namespace SystemOverlay;

public partial class SettingsWindow : Window
{
    public AppSettings CurrentSettings { get; private set; }
    private bool _initialized;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();

        CurrentSettings = new AppSettings
        {
            WindowX = settings.WindowX,
            WindowY = settings.WindowY,
            IsPinned = settings.IsPinned,
            ShowCpu = settings.ShowCpu,
            ShowGpu = settings.ShowGpu,
            ShowRam = settings.ShowRam,
            ShowNetwork = settings.ShowNetwork,
            ShowDisk = settings.ShowDisk,
            UpdateIntervalMs = settings.UpdateIntervalMs,
            FontScale = settings.FontScale,
            OverlayScale = settings.OverlayScale,
            AutoStart = settings.AutoStart,
            AlertsEnabled = settings.AlertsEnabled,
            CpuUsageThreshold = settings.CpuUsageThreshold,
            CpuTempThreshold = settings.CpuTempThreshold,
            GpuUsageThreshold = settings.GpuUsageThreshold,
            GpuTempThreshold = settings.GpuTempThreshold,
            RamUsageThreshold = settings.RamUsageThreshold,
        };

        // General
        ChkAutoStart.IsChecked = AutoStartService.IsEnabled();

        // Metrics
        ChkCpu.IsChecked = CurrentSettings.ShowCpu;
        ChkGpu.IsChecked = CurrentSettings.ShowGpu;
        ChkRam.IsChecked = CurrentSettings.ShowRam;
        ChkNet.IsChecked = CurrentSettings.ShowNetwork;
        ChkDisk.IsChecked = CurrentSettings.ShowDisk;

        // Appearance
        SliderFontScale.Value = CurrentSettings.FontScale * 100;

        // Alerts
        ChkAlerts.IsChecked = CurrentSettings.AlertsEnabled;
        SliderCpuUsage.Value = CurrentSettings.CpuUsageThreshold;
        SliderCpuTemp.Value = CurrentSettings.CpuTempThreshold;
        SliderGpuUsage.Value = CurrentSettings.GpuUsageThreshold;
        SliderGpuTemp.Value = CurrentSettings.GpuTempThreshold;
        SliderRamUsage.Value = CurrentSettings.RamUsageThreshold;

        _initialized = true;
        UpdateSliderLabels();
        UpdateAppearanceLabels();
    }

    internal void OnSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        UpdateSliderLabels();
    }

    internal void OnAppearanceSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized) return;
        UpdateAppearanceLabels();
    }

    private void UpdateSliderLabels()
    {
        CpuUsageVal.Text = $"{SliderCpuUsage.Value:F0}";
        CpuTempVal.Text = $"{SliderCpuTemp.Value:F0}";
        GpuUsageVal.Text = $"{SliderGpuUsage.Value:F0}";
        GpuTempVal.Text = $"{SliderGpuTemp.Value:F0}";
        RamUsageVal.Text = $"{SliderRamUsage.Value:F0}";
    }

    private void UpdateAppearanceLabels()
    {
        FontScaleVal.Text = $"{SliderFontScale.Value:F0}";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        CurrentSettings.ShowCpu = ChkCpu.IsChecked == true;
        CurrentSettings.ShowGpu = ChkGpu.IsChecked == true;
        CurrentSettings.ShowRam = ChkRam.IsChecked == true;
        CurrentSettings.ShowNetwork = ChkNet.IsChecked == true;
        CurrentSettings.ShowDisk = ChkDisk.IsChecked == true;

        CurrentSettings.AutoStart = ChkAutoStart.IsChecked == true;
        CurrentSettings.FontScale = SliderFontScale.Value / 100.0;

        CurrentSettings.AlertsEnabled = ChkAlerts.IsChecked == true;
        CurrentSettings.CpuUsageThreshold = (float)SliderCpuUsage.Value;
        CurrentSettings.CpuTempThreshold = (float)SliderCpuTemp.Value;
        CurrentSettings.GpuUsageThreshold = (float)SliderGpuUsage.Value;
        CurrentSettings.GpuTempThreshold = (float)SliderGpuTemp.Value;
        CurrentSettings.RamUsageThreshold = (float)SliderRamUsage.Value;

        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
