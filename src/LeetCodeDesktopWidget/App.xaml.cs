using System;
using System.IO;
using System.Windows;
using LeetCodeDesktopWidget.Desktop;
using LeetCodeDesktopWidget.Helpers;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;

namespace LeetCodeDesktopWidget;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            SyncLogger.Log(0, "AppDomain UnhandledException", args.ExceptionObject?.ToString());
        };

        DispatcherUnhandledException += (s, args) =>
        {
            SyncLogger.Log(0, "Dispatcher UnhandledException", args.Exception?.ToString());
        };

        SyncLogger.Log(0, "App OnStartup started");

        // 1. If launched in a secondary or test runner desktop, attach thread
        // to the interactive "Default" desktop where Windows Explorer and Progman reside.
        try
        {
            IntPtr hDesk = DesktopInterop.OpenDesktop("Default", 0, false, 0x1FF);
            if (hDesk != IntPtr.Zero)
            {
                DesktopInterop.SetThreadDesktop(hDesk);
            }
        }
        catch (Exception ex)
        {
            SyncLogger.Log(0, "SetThreadDesktop exception", ex.Message);
        }

        base.OnStartup(e);

        try
        {
            // 2. Composition root: construct services and authoritative MainViewModel
            var leetCodeService = new LeetCodeService();
            var configService = new ConfigurationService();
            var cacheService = new CacheService();
            var refreshCoordinator = new RefreshCoordinator();
            var verificationService = new AccountVerificationService(leetCodeService);

            var viewModel = new MainViewModel(
                leetCodeService,
                configService,
                cacheService,
                refreshCoordinator,
                verificationService);

            if (e.Args.Length > 0 && Array.Exists(e.Args, a => a.Equals("--verify-prompt", StringComparison.OrdinalIgnoreCase)))
            {
                viewModel.Username = "manav_chaudhary01";
                viewModel.StartVerificationCommand.Execute(null);
            }

            var mainWindow = new MainWindow(viewModel);
            MainWindow = mainWindow;
            mainWindow.Show();
            SyncLogger.Log(0, "MainWindow.Show() called");
        }
        catch (Exception ex)
        {
            SyncLogger.Log(0, "CompositionRoot or MainWindow.Show failed", ex.ToString());
            throw;
        }
    }
}
