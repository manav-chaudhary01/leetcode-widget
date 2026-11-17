using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace LeetCodeDesktopWidget.Helpers;

/// <summary>
/// Manages Windows startup registration (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).
/// Safe across test and headless environments.
/// </summary>
public static class StartupHelper
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "LeetCodeDesktopWidget";

    public static bool IsStartWithWindowsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static bool SetStartWithWindows(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true);
            if (key == null) return false;

            if (enable)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrWhiteSpace(exePath) && exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue(AppName, $"\"{exePath}\"");
                    return true;
                }
            }
            else
            {
                key.DeleteValue(AppName, false);
                return true;
            }
        }
        catch
        {
            // Non-critical: fail safely in test/sandboxed environments
        }
        return false;
    }
}
