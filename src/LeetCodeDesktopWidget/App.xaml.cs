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
        // If launched in a secondary or test runner desktop, attach thread
        // to the interactive "Default" desktop where Windows Explorer and Progman reside.
        try
        {
            IntPtr hDesk = DesktopInterop.OpenDesktop("Default", 0, false, 0x1FF);
            if (hDesk != IntPtr.Zero)
            {
                DesktopInterop.SetThreadDesktop(hDesk);
            }
        }
        catch
        {
            // Gracefully continue if desktop switching is restricted
        }

        base.OnStartup(e);
    }
}
