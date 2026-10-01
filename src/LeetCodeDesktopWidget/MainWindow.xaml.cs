using System;
using System.Windows;
using System.Windows.Input;
using LeetCodeDesktopWidget.Desktop;

namespace LeetCodeDesktopWidget;

/// <summary>
/// Interaction logic for MainWindow.xaml - Desktop Widget Prototype.
/// </summary>
public partial class MainWindow : Window
{
    private DesktopWindowManager? _desktopManager;
    private int _clickCount;

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

        UpdateStatusDisplay();
    }

    private void UpdateStatusDisplay()
    {
        if (_desktopManager == null) return;

        var host = _desktopManager.CurrentHostInfo;
        HostingModeValue.Text = host.HostType switch
        {
            DesktopHostType.WorkerW => $"WorkerW (0x{host.TargetParentHandle.ToInt64():X})",
            DesktopHostType.Progman => $"Progman (0x{host.TargetParentHandle.ToInt64():X})",
            _ => "Bottom-Pinned Fallback"
        };

        HostTypeTextBlock.Text = host.IsAttached ? "Desktop Child Window" : "Z-Order Bottom";
    }

    private void OnExplorerRestarted()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateStatusDisplay();
        });
    }

    private void OnDisplayChanged()
    {
        Dispatcher.Invoke(() =>
        {
            // Keep widget pinned at bottom upon display/DPI adjustments
            _desktopManager?.SendToBottom();
        });
    }

    private void OnTestButtonClicked(object sender, RoutedEventArgs e)
    {
        _clickCount++;
        TestButton.Content = $"Test Click ({_clickCount})";
        ClickFeedbackText.Text = $"Input received without focus steal! Count: {_clickCount}";
    }

    protected override void OnClosed(EventArgs e)
    {
        _desktopManager?.Dispose();
        base.OnClosed(e);
    }
}
