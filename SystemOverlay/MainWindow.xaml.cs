using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SystemOverlay.Models;
using SystemOverlay.Services;
using SystemOverlay.ViewModels;

namespace SystemOverlay;

public partial class MainWindow : Window
{
    private readonly HardwareService _hardware;
    private readonly MainViewModel _viewModel;
    private AppSettings _settings;

    public MainWindow()
    {
        InitializeComponent();

        _hardware = new HardwareService();
        try
        {
            _settings = SettingsService.Load();
            _viewModel = new MainViewModel(_hardware, _settings);
            DataContext = _viewModel;

            _viewModel.AlertStateChanged += OnAlertStateChanged;

            ApplySettings();
            _viewModel.StartPolling();
        }
        catch
        {
            _hardware.Dispose();
            throw;
        }
    }

    private void ApplySettings()
    {
        Left = _settings.WindowX;
        Top = _settings.WindowY;
        Topmost = _settings.IsPinned;
        PinMenuItem.IsChecked = _settings.IsPinned;
        PinButton.Opacity = _settings.IsPinned ? 1.0 : 0.4;

        // Only restore width if it's a real saved pixel value (not the first-run default)
        if (_settings.OverlayScale > 100)
            Width = _settings.OverlayScale;

        // Apply font scale
        if (_settings.FontScale > 0 && Math.Abs(_settings.FontScale - 1.0) > 0.01)
        {
            MainBorder.LayoutTransform = new ScaleTransform(_settings.FontScale, _settings.FontScale);
        }
        else
        {
            MainBorder.LayoutTransform = Transform.Identity;
        }
    }

    // ── Drag ──────────────────────────────────────────────
    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    // ── Pin Toggle ────────────────────────────────────────
    private void OnPinToggle(object sender, RoutedEventArgs e)
    {
        _settings.IsPinned = !_settings.IsPinned;
        Topmost = _settings.IsPinned;
        PinMenuItem.IsChecked = _settings.IsPinned;
        PinButton.Opacity = _settings.IsPinned ? 1.0 : 0.4;
    }

    // ── Expand/Collapse Processes ─────────────────────────
    private void OnToggleCpuProcesses(object sender, RoutedEventArgs e)
    {
        _viewModel.CpuExpanded = !_viewModel.CpuExpanded;
        AnimatePanel(CpuProcessPanel, CpuExpandBtn, _viewModel.CpuExpanded);
    }

    private void OnToggleGpuProcesses(object sender, RoutedEventArgs e)
    {
        _viewModel.GpuExpanded = !_viewModel.GpuExpanded;
        AnimatePanel(GpuProcessPanel, GpuExpandBtn, _viewModel.GpuExpanded);
    }

    private void OnToggleRamProcesses(object sender, RoutedEventArgs e)
    {
        _viewModel.RamExpanded = !_viewModel.RamExpanded;
        AnimatePanel(RamProcessPanel, RamExpandBtn, _viewModel.RamExpanded);
    }

    private void AnimatePanel(ItemsControl panel, Button btn, bool isExpanded)
    {
        btn.Content = isExpanded ? "▼" : "▶";

        if (panel.LayoutTransform is not ScaleTransform scale)
        {
            scale = new ScaleTransform(1, isExpanded ? 0 : 1);
            panel.LayoutTransform = scale;
        }

        if (isExpanded)
        {
            panel.Visibility = Visibility.Visible;
            var scaleAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) 
            { 
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } 
            };
            var opacityAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
            
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            panel.BeginAnimation(OpacityProperty, opacityAnim);
        }
        else
        {
            var scaleAnim = new DoubleAnimation(scale.ScaleY, 0, TimeSpan.FromMilliseconds(200)) 
            { 
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } 
            };
            var opacityAnim = new DoubleAnimation(panel.Opacity, 0, TimeSpan.FromMilliseconds(150));
            
            scaleAnim.Completed += (s, ev) => 
            { 
               // Check if it's still collapsed (prevent race condition if clicked rapidly)
               if (btn.Content.ToString() == "▶") 
                   panel.Visibility = Visibility.Collapsed; 
            };
            
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            panel.BeginAnimation(OpacityProperty, opacityAnim);
        }
    }

    // ── Settings ──────────────────────────────────────────
    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var win = new SettingsWindow(_settings);
            win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (win.ShowDialog() == true)
            {
                _settings = win.CurrentSettings;
                _viewModel.UpdateSettings(_settings);
                ApplySettings();
                SettingsService.Save(_settings);

                AutoStartService.SetEnabled(_settings.AutoStart);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening settings: {ex.Message}", "System Overlay",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Exit ──────────────────────────────────────────────
    private void OnExitClick(object sender, RoutedEventArgs e) => SavePositionAndClose();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _viewModel.Dispose();
        GpuUsageService.Cleanup();
        _settings.WindowX = Left;
        _settings.WindowY = Top;
        _settings.OverlayScale = Width;
        SettingsService.Save(_settings);
        _hardware.Dispose();
        base.OnClosing(e);
    }

    private void SavePositionAndClose()
    {
        _settings.WindowX = Left;
        _settings.WindowY = Top;
        _settings.OverlayScale = Width;
        SettingsService.Save(_settings);
        Close();
    }

    // ── Native Backdrop ───────────────────────────────────
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        
        // Remove WPF fallback solid background to reveal composition
        MainBorder.Background = new SolidColorBrush(Color.FromArgb(50, 26, 26, 46));
        
        // Apply native transparency
        WindowCompositionService.ApplyBackdrop(this);
    }

    // ── Alert Animation ───────────────────────────────────
    private void OnAlertStateChanged(object? sender, bool isActive)
    {
        if (isActive)
        {
            if (!_settings.IsPinned)
            {
                Topmost = true;
                Activate();
            }
            MainBorder.BorderThickness = new Thickness(1.5);
            MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 255, 34, 68));
            PulseAlert(true);
        }
        else
        {
            PulseAlert(false);
            if (!_settings.IsPinned)
                Topmost = false;
            MainBorder.BorderThickness = new Thickness(0);
            MainBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        }
    }

    private void PulseAlert(bool start)
    {
        if (start)
        {
            var anim = new DoubleAnimation(0.6, 1.0, TimeSpan.FromMilliseconds(500))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            MainBorder.BeginAnimation(OpacityProperty, anim);
        }
        else
        {
            MainBorder.BeginAnimation(OpacityProperty, null);
            MainBorder.Opacity = 1.0;
        }
    }
}