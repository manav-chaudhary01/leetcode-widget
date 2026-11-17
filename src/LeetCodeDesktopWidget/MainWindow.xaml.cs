using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using LeetCodeDesktopWidget.Desktop;
using LeetCodeDesktopWidget.Helpers;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;

namespace LeetCodeDesktopWidget;

/// <summary>
/// Interaction logic for MainWindow.xaml - Desktop Heatmap Widget.
/// Supports deliberate dragging, virtual multi-monitor clamping, and persistent positioning.
/// </summary>
public partial class MainWindow : Window
{
    private DesktopWindowManager? _desktopManager;

    private bool _isDragCandidate;
    private bool _isDragging;
    private Point _dragStartScreenPoint;
    private double _dragStartWindowLeft;
    private double _dragStartWindowTop;

    public DesktopWindowManager? DesktopManager => _desktopManager;
    public bool IsDragging => _isDragging;
    public bool IsDragCandidate => _isDragCandidate;

    public MainWindow() : this(CreateDefaultViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private static MainViewModel CreateDefaultViewModel()
    {
        var leetCodeService = new LeetCodeService();
        return new MainViewModel(
            leetCodeService,
            new ConfigurationService(),
            new CacheService(),
            new RefreshCoordinator(),
            new AccountVerificationService(leetCodeService));
    }

    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            _desktopManager = new DesktopWindowManager(this);
            _desktopManager.ExplorerRestarted += OnExplorerRestarted;
            _desktopManager.DisplayChanged += OnDisplayChanged;
            _desktopManager.AttachToDesktop();

            UpdateHostStatusBadge();

            if (DataContext is MainViewModel vm)
            {
                vm.PresetChanged += OnPresetChanged;
                vm.RequestResetPosition += OnRequestResetPosition;

                await vm.InitializeAsync();

                ApplyPresetDimensions(vm.CurrentPresetDimensions);
                RestorePositionFromConfig(vm.WindowX, vm.WindowY);

                if (!vm.IsAccountVerified)
                {
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        OnboardingUsernameBox?.Focus();
                        OnboardingUsernameBox?.SelectAll();
                    });
                }
            }
            else
            {
                EnsureVisibleOnScreen();
            }
        }
        catch (Exception ex)
        {
            SyncLogger.Log(0, "OnSourceInitialized exception", ex.ToString());
            EnsureVisibleOnScreen();
        }
    }

    /// <summary>
    /// Restores the window position from saved coordinates or calculates a default position.
    /// Clamps position to the virtual desktop across all active monitors.
    /// </summary>
    public void RestorePositionFromConfig(double? savedX, double? savedY)
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;

        if (savedX.HasValue && savedY.HasValue &&
            !double.IsNaN(savedX.Value) && !double.IsInfinity(savedX.Value) &&
            !double.IsNaN(savedY.Value) && !double.IsInfinity(savedY.Value))
        {
            var clamped = PositionHelper.ClampWindowPosition(savedX.Value, savedY.Value, w, h);
            Left = clamped.X;
            Top = clamped.Y;
        }
        else
        {
            var defaultPos = PositionHelper.GetDefaultPosition(w, h);
            Left = defaultPos.X;
            Top = defaultPos.Y;
        }

        EnsureVisibleOnScreen();
    }

    /// <summary>
    /// Clamps current window coordinates to remain reachable within the virtual screen bounds.
    /// </summary>
    public void EnsureVisibleOnScreen()
    {
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        var clamped = PositionHelper.ClampWindowPosition(Left, Top, w, h);
        Left = clamped.X;
        Top = clamped.Y;
    }

    public void ApplyPresetDimensions(WidgetPresetDimensions dims)
    {
        Width = dims.Width;
        Height = dims.Height;
    }

    private void OnPresetChanged(object? sender, WidgetPreset preset)
    {
        if (DataContext is MainViewModel vm)
        {
            ApplyPresetDimensions(vm.CurrentPresetDimensions);
            if (vm.RememberPosition)
            {
                var clamped = PositionHelper.ClampWindowPosition(Left, Top, Width, Height);
                Left = clamped.X;
                Top = clamped.Y;
                _ = vm.SaveWindowPositionAsync(Left, Top);
            }
            else
            {
                var def = PositionHelper.GetDefaultPosition(Width, Height);
                Left = def.X;
                Top = def.Y;
            }
            EnsureVisibleOnScreen();
            _desktopManager?.SendToBottom();
        }
    }

    private void OnRequestResetPosition(object? sender, EventArgs e)
    {
        var def = PositionHelper.GetDefaultPosition(Width, Height);
        Left = def.X;
        Top = def.Y;
        if (DataContext is MainViewModel vm && vm.RememberPosition)
        {
            _ = vm.SaveWindowPositionAsync(Left, Top);
        }
        EnsureVisibleOnScreen();
        _desktopManager?.SendToBottom();
    }

    public void OnUsernameInputBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _ = Dispatcher.InvokeAsync(() =>
            {
                tb.Focus();
                tb.SelectAll();
            });
        }
    }

    public void OnDragHandleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (DataContext is MainViewModel vm && vm.IsPositionLocked) return;
        if (IsInteractiveControl(e.OriginalSource as DependencyObject)) return;

        _isDragCandidate = true;
        _isDragging = false;
        _dragStartScreenPoint = PositionHelper.GetCursorPositionDips(this);
        _dragStartWindowLeft = Left;
        _dragStartWindowTop = Top;

        CaptureMouse();
        e.Handled = true;
    }

    public void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragCandidate && !_isDragging) return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndDrag(savePosition: _isDragging);
            return;
        }

        var currentScreenPoint = PositionHelper.GetCursorPositionDips(this);
        double deltaX = currentScreenPoint.X - _dragStartScreenPoint.X;
        double deltaY = currentScreenPoint.Y - _dragStartScreenPoint.Y;

        if (!_isDragging)
        {
            double thresholdX = SystemParameters.MinimumHorizontalDragDistance;
            double thresholdY = SystemParameters.MinimumVerticalDragDistance;

            if (Math.Abs(deltaX) >= thresholdX || Math.Abs(deltaY) >= thresholdY)
            {
                _isDragging = true;
            }
        }

        if (_isDragging)
        {
            double targetLeft = _dragStartWindowLeft + deltaX;
            double targetTop = _dragStartWindowTop + deltaY;

            double w = ActualWidth > 0 ? ActualWidth : Width;
            double h = ActualHeight > 0 ? ActualHeight : Height;

            var clamped = PositionHelper.ClampWindowPosition(targetLeft, targetTop, w, h);
            Left = clamped.X;
            Top = clamped.Y;
            e.Handled = true;
        }
    }

    public void OnWindowMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragCandidate || _isDragging)
        {
            EndDrag(savePosition: _isDragging);
            e.Handled = true;
        }
    }

    public void OnWindowLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDragCandidate || _isDragging)
        {
            EndDrag(savePosition: _isDragging);
        }
    }

    private void EndDrag(bool savePosition)
    {
        bool wasDragging = _isDragging;
        _isDragCandidate = false;
        _isDragging = false;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (wasDragging && savePosition)
        {
            EnsureVisibleOnScreen();
            if (DataContext is MainViewModel vm)
            {
                _ = vm.SaveWindowPositionAsync(Left, Top);
            }
            _desktopManager?.SendToBottom();
        }
    }

    /// <summary>
    /// Traverses the visual tree upwards from source to determine if an interactive control was targeted.
    /// Prevents dragging when interacting with buttons, textboxes, dropdowns, or heatmap cells.
    /// </summary>
    public static bool IsInteractiveControl(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (current is System.Windows.Controls.Button ||
                current is System.Windows.Controls.TextBox ||
                current is System.Windows.Controls.ComboBox ||
                current is System.Windows.Controls.ComboBoxItem ||
                current is Views.HeatmapControl)
            {
                return true;
            }

            if (current is System.Windows.Controls.TextBlock tb &&
                (tb.InputBindings.Count > 0 || tb.Cursor == Cursors.Hand))
            {
                return true;
            }

            DependencyObject? parent = null;
            if (current is Visual || current is Visual3D)
            {
                try
                {
                    parent = VisualTreeHelper.GetParent(current);
                }
                catch
                {
                    // Fallback to logical tree if visual tree is not connected
                }
            }

            parent ??= (current as FrameworkElement)?.Parent
                     ?? (current as FrameworkContentElement)?.Parent
                     ?? LogicalTreeHelper.GetParent(current);

            current = parent;
        }
        return false;
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
        if (OnboardingStatusDot != null)
        {
            OnboardingStatusDot.ToolTip = statusText;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            if (DataContext is MainViewModel vm)
            {
                if (vm.IsSettingsOpen)
                {
                    vm.IsSettingsOpen = false;
                    e.Handled = true;
                }
                else if (vm.IsVerificationPromptOpen)
                {
                    vm.CancelVerificationCommand.Execute(null);
                    e.Handled = true;
                }
                else if (vm.IsEditingUsername)
                {
                    vm.CancelEditUsernameCommand.Execute(null);
                    e.Handled = true;
                }
            }
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
        if (DataContext is MainViewModel vm)
        {
            vm.PresetChanged -= OnPresetChanged;
            vm.RequestResetPosition -= OnRequestResetPosition;
        }
        _desktopManager?.Dispose();
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
