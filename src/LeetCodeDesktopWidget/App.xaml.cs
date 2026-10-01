using System;
using System.Windows;
using LeetCodeDesktopWidget.Desktop;

namespace LeetCodeDesktopWidget;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // If the process is launched from an alternative or virtual desktop session,
        // attach the UI thread to the interactive "Default" desktop where Windows Explorer
        // and Progman / WorkerW reside.
        try
        {
            if (DesktopInterop.FindWindow("Progman", null) == IntPtr.Zero)
            {
                IntPtr hDesk = DesktopInterop.OpenDesktop("Default", 0, false, 0x1FF);
                if (hDesk != IntPtr.Zero)
                {
                    DesktopInterop.SetThreadDesktop(hDesk);
                }
            }
        }
        catch
        {
            // Fallback gracefully if permissions disallow switching desktops
        }

        base.OnStartup(e);
    }
}
