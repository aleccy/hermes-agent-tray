using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HermesAgentTray.Services;

public enum AppTheme { Light, Dark, System }

public static class ThemeService
{
    /// <summary>
    /// Check if Windows is currently in dark mode.
    /// </summary>
    public static bool IsWindowsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Resolve the effective theme based on user setting.
    /// </summary>
    public static AppTheme ResolveEffectiveTheme(AppTheme setting)
    {
        if (setting == AppTheme.System)
            return IsWindowsDarkMode() ? AppTheme.Dark : AppTheme.Light;
        return setting;
    }

    [DllImport("dwmapi.dll", PreserveSig = false)]
    private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>
    /// Apply dark title bar to a WPF window.
    /// </summary>
    public static void ApplyTitleBarDarkMode(System.Windows.Window window, bool dark)
    {
        try
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch { }
    }
}
