using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SystemOverlay.Services;

public static class WindowCompositionService
{
    // DWM API Constants
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_MICA_EFFECT = 1029; // For older Windows 11 builds

    // Backdrop types
    private const int DWMSBT_AUTO = 0;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2;   // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4; // Tabbed

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // Build versions
    private const int Windows11_22H2 = 22621;
    private const int Windows11_21H2 = 22000;
    private const int Windows10_1803 = 17134;

    public static void ApplyBackdrop(Window window)
    {
        var build = Environment.OSVersion.Version.Build;
        var handle = new WindowInteropHelper(window).EnsureHandle();

        // Always try to set dark mode for the titlebar/frame
        int trueValue = 1;
        DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueValue, Marshal.SizeOf(typeof(int)));

        if (build >= Windows11_22H2)
        {
            // Windows 11 22H2+: Use modern SystemBackdropType
            int backdropType = DWMSBT_MAINWINDOW; // Mica
            DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, Marshal.SizeOf(typeof(int)));
        }
        else if (build >= Windows11_21H2)
        {
            // Windows 11 21H2: Use legacy Mica flag
            int micaEffect = 1;
            DwmSetWindowAttribute(handle, DWMWA_MICA_EFFECT, ref micaEffect, Marshal.SizeOf(typeof(int)));
        }
        else if (build >= Windows10_1803)
        {
            // Windows 10: SetWindowCompositionAttribute for Acrylic
            EnableWindows10Acrylic(handle);
        }
    }

    // Windows 10 Undocumented API for Acrylic
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private static void EnableWindows10Acrylic(IntPtr handle)
    {
        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            // Format: AABBGGRR - 0x992E1A1A (Appr. 60% opacity dark purple-blue)
            GradientColor = unchecked((int)0x992E1A1A)
        };

        var accentStructSize = Marshal.SizeOf(accent);
        var accentPtr = Marshal.AllocHGlobal(accentStructSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };

        SetWindowCompositionAttribute(handle, ref data);
        Marshal.FreeHGlobal(accentPtr);
    }
}
