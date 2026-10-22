using System;
using System.Windows;
using System.Windows.Input;
using LeetCodeDesktopWidget.Desktop;

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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _desktopManager = new DesktopWindowManager(this);
        _desktopManager.ExplorerRestarted += OnExplorerRestarted;
        _desktopManager.DisplayChanged += OnDisplayChanged;
        _desktopManager.AttachToDesktop();

        UpdateHostStatusBadge();
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
        Dispatcher.Invoke(UpdateHostStatusBadge);
    }

    private void OnDisplayChanged()
    {
        Dispatcher.Invoke(() =>
        {
            _desktopManager?.SendToBottom();
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _desktopManager?.Dispose();
        base.OnClosed(e);
    }
}
