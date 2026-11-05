using System;
using System.Windows;
using System.Windows.Input;
using LeetCodeDesktopWidget.Desktop;
using LeetCodeDesktopWidget.ViewModels;

namespace LeetCodeDesktopWidget;

/// <summary>
/// Interaction logic for MainWindow.xaml - Desktop Heatmap Widget.
/// </summary>
public partial class MainWindow : Window
{
    private DesktopWindowManager? _desktopManager;

    public DesktopWindowManager? DesktopManager => _desktopManager;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        EnsureVisibleOnScreen();

        _desktopManager = new DesktopWindowManager(this);
        _desktopManager.ExplorerRestarted += OnExplorerRestarted;
        _desktopManager.DisplayChanged += OnDisplayChanged;
        _desktopManager.AttachToDesktop();

        UpdateHostStatusBadge();

        if (DataContext is MainViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    public void EnsureVisibleOnScreen()
    {
        double screenWidth = SystemParameters.WorkArea.Width;
        double screenHeight = SystemParameters.WorkArea.Height;
        if (screenWidth <= 0 || screenHeight <= 0) return;

        double maxLeft = Math.Max(0, screenWidth - Width - 20);
        double maxTop = Math.Max(0, screenHeight - Height - 20);

        if (Left > maxLeft)
        {
            Left = Math.Max(20, maxLeft);
        }
        if (Top > maxTop)
        {
            Top = Math.Max(20, maxTop);
        }
        if (Left < 0) Left = 20;
        if (Top < 0) Top = 20;
    }

    private void UpdateHostStatusBadge()
    {
        if (_desktopManager == null) return;

        var host = _desktopManager.CurrentHostInfo;
        string statusText = host.HostType switch
        {
            DesktopHostType.WorkerW => "Desktop Layer (WorkerW)",
            DesktopHostType.Progman => "Desktop Layer (Progman)",
            _ => "Z-Order Bottom"
        };

        Title = $"LeetCode Desktop Widget - {statusText}";
        if (DesktopStatusDot != null)
        {
            DesktopStatusDot.ToolTip = statusText;
        }
    }

    private void OnExplorerRestarted()
    {
        Dispatcher.Invoke(() =>
        {
            EnsureVisibleOnScreen();
            UpdateHostStatusBadge();
        });
    }

    private void OnDisplayChanged()
    {
        Dispatcher.Invoke(() =>
        {
            EnsureVisibleOnScreen();
            _desktopManager?.SendToBottom();
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _desktopManager?.Dispose();
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
