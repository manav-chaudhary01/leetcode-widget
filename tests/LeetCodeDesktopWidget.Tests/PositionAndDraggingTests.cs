using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeetCodeDesktopWidget.Desktop;
using LeetCodeDesktopWidget.Helpers;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using LeetCodeDesktopWidget.Views;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

public class PositionAndDraggingTests : IDisposable
{
    private readonly string _tempDir;

    public PositionAndDraggingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lc_widget_pos_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    private ConfigurationService CreateTestConfigService()
    {
        var configPath = Path.Combine(_tempDir, "config.json");
        return new ConfigurationService(configPath);
    }

    private static void RunInSta(Action action)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(10000);

        if (threadEx != null)
        {
            throw threadEx;
        }
    }

    // ==========================================
    // 1. POSITION PERSISTENCE & CONFIG TESTS
    // ==========================================

    [Fact]
    public void WidgetConfig_Default_HasNullWindowCoordinates()
    {
        var config = WidgetConfig.Default;
        Assert.Null(config.WindowX);
        Assert.Null(config.WindowY);
    }

    [Fact]
    public void WidgetConfig_SerializesAndDeserializes_WindowCoordinatesCorrectly()
    {
        var config = new WidgetConfig
        {
            Username = "coder",
            SelectedYear = 2026,
            RefreshIntervalMinutes = 30,
            VerifiedUsername = "coder",
            WindowX = 350.5,
            WindowY = 420.25
        };

        var json = JsonSerializer.Serialize(config);
        var restored = JsonSerializer.Deserialize<WidgetConfig>(json);

        Assert.NotNull(restored);
        Assert.Equal(350.5, restored.WindowX);
        Assert.Equal(420.25, restored.WindowY);
        Assert.Equal("coder", restored.Username);
    }

    [Fact]
    public async Task MainViewModel_SaveWindowPositionAsync_PersistsCoordinatesToDisk()
    {
        var configService = CreateTestConfigService();
        using var vm = new MainViewModel(
            new LeetCodeService(),
            configService,
            new CacheService(_tempDir),
            new RefreshCoordinator(),
            new AccountVerificationService(new LeetCodeService()));

        await vm.SaveWindowPositionAsync(500.0, 300.0);

        Assert.Equal(500.0, vm.WindowX);
        Assert.Equal(300.0, vm.WindowY);

        var loadedConfig = await configService.LoadConfigurationAsync();
        Assert.Equal(500.0, loadedConfig.WindowX);
        Assert.Equal(300.0, loadedConfig.WindowY);
    }

    [Fact]
    public async Task MainViewModel_InitializeAsync_RestoresSavedWindowPosition()
    {
        var configService = CreateTestConfigService();
        await configService.SaveConfigurationAsync(new WidgetConfig
        {
            Username = "testuser",
            SelectedYear = 2026,
            VerifiedUsername = "testuser",
            WindowX = 120.0,
            WindowY = 240.0
        });

        using var vm = new MainViewModel(
            new LeetCodeService(),
            configService,
            new CacheService(_tempDir),
            new RefreshCoordinator(),
            new AccountVerificationService(new LeetCodeService()));

        await vm.InitializeAsync();

        Assert.Equal(120.0, vm.WindowX);
        Assert.Equal(240.0, vm.WindowY);
    }

    [Fact]
    public async Task MainViewModel_InitializeAsync_HandlesMissingOrNullCoordinates()
    {
        var configService = CreateTestConfigService();
        await configService.SaveConfigurationAsync(new WidgetConfig
        {
            Username = "testuser",
            SelectedYear = 2026,
            VerifiedUsername = "testuser",
            WindowX = null,
            WindowY = null
        });

        using var vm = new MainViewModel(
            new LeetCodeService(),
            configService,
            new CacheService(_tempDir),
            new RefreshCoordinator(),
            new AccountVerificationService(new LeetCodeService()));

        await vm.InitializeAsync();

        Assert.Null(vm.WindowX);
        Assert.Null(vm.WindowY);
    }

    // ==========================================
    // 2. BOUNDS CLAMPING & VIRTUAL DESKTOP TESTS
    // ==========================================

    [Fact]
    public void PositionHelper_GetVirtualScreenBounds_ReturnsSensibleBounds()
    {
        var bounds = PositionHelper.GetVirtualScreenBounds();
        Assert.True(bounds.Width > 0, "Virtual screen width must be positive");
        Assert.True(bounds.Height > 0, "Virtual screen height must be positive");
    }

    [Fact]
    public void PositionHelper_GetDefaultPosition_UsesSensibleOffsetAndClamps()
    {
        var workArea = new Rect(0, 0, 1920, 1040);
        var virtualBounds = new Rect(0, 0, 1920, 1080);
        var pos = PositionHelper.GetDefaultPosition(845, 275, workArea, virtualBounds);

        Assert.Equal(60.0, pos.X);
        Assert.Equal(60.0, pos.Y);
    }

    [Fact]
    public void PositionHelper_ClampWindowPosition_ClampsWithinSingleMonitorBounds()
    {
        var virtualBounds = new Rect(0, 0, 1920, 1080);
        double w = 845;
        double h = 275;

        // Normal inside bounds
        var p1 = PositionHelper.ClampWindowPosition(100, 150, w, h, virtualBounds);
        Assert.Equal(100, p1.X);
        Assert.Equal(150, p1.Y);

        // Negative coordinates clamped to 0
        var p2 = PositionHelper.ClampWindowPosition(-50, -30, w, h, virtualBounds);
        Assert.Equal(0, p2.X);
        Assert.Equal(0, p2.Y);

        // Exceeding right/bottom clamped so entire window is visible
        var p3 = PositionHelper.ClampWindowPosition(2000, 1500, w, h, virtualBounds);
        Assert.Equal(1920 - w, p3.X);
        Assert.Equal(1080 - h, p3.Y);
    }

    [Fact]
    public void PositionHelper_ClampWindowPosition_SupportsNegativeMultiMonitorCoordinates()
    {
        // Secondary monitor is located to the left of the primary monitor: [-1920, 0] to [0, 1080]
        // Virtual desktop spans: Left = -1920, Top = 0, Width = 3840, Height = 1080
        var virtualBounds = new Rect(-1920, 0, 3840, 1080);
        double w = 845;
        double h = 275;

        // Position on secondary monitor (negative X)
        var p1 = PositionHelper.ClampWindowPosition(-1000, 200, w, h, virtualBounds);
        Assert.Equal(-1000, p1.X);
        Assert.Equal(200, p1.Y);

        // Past left edge of secondary monitor
        var p2 = PositionHelper.ClampWindowPosition(-2500, 200, w, h, virtualBounds);
        Assert.Equal(-1920, p2.X);

        // Past right edge of primary monitor
        var p3 = PositionHelper.ClampWindowPosition(3000, 200, w, h, virtualBounds);
        Assert.Equal(-1920 + 3840 - w, p3.X); // 1920 - 845 = 1075
    }

    [Fact]
    public void PositionHelper_ClampWindowPosition_SupportsSecondaryMonitorAboveOrBelow()
    {
        // Secondary monitor located above: Top = -1080, Height = 2160
        var virtualBounds = new Rect(0, -1080, 1920, 2160);
        double w = 845;
        double h = 275;

        // On upper monitor
        var p1 = PositionHelper.ClampWindowPosition(100, -500, w, h, virtualBounds);
        Assert.Equal(100, p1.X);
        Assert.Equal(-500, p1.Y);

        // Far past top edge
        var p2 = PositionHelper.ClampWindowPosition(100, -2000, w, h, virtualBounds);
        Assert.Equal(-1080, p2.Y);

        // Far past bottom edge
        var p3 = PositionHelper.ClampWindowPosition(100, 3000, w, h, virtualBounds);
        Assert.Equal(-1080 + 2160 - h, p3.Y);
    }

    [Fact]
    public void PositionHelper_ClampWindowPosition_GuardsAgainstNaNAndInfinity()
    {
        var virtualBounds = new Rect(0, 0, 1920, 1080);
        double w = 845;
        double h = 275;

        var pNan = PositionHelper.ClampWindowPosition(double.NaN, double.NaN, w, h, virtualBounds);
        Assert.False(double.IsNaN(pNan.X));
        Assert.False(double.IsNaN(pNan.Y));
        Assert.Equal(PositionHelper.DefaultOffset, pNan.X);
        Assert.Equal(PositionHelper.DefaultOffset, pNan.Y);

        var pPosInf = PositionHelper.ClampWindowPosition(double.PositiveInfinity, double.PositiveInfinity, w, h, virtualBounds);
        Assert.False(double.IsInfinity(pPosInf.X));
        Assert.False(double.IsInfinity(pPosInf.Y));

        var pNegInf = PositionHelper.ClampWindowPosition(double.NegativeInfinity, double.NegativeInfinity, w, h, virtualBounds);
        Assert.False(double.IsInfinity(pNegInf.X));
        Assert.False(double.IsInfinity(pNegInf.Y));
    }

    // ==========================================
    // 3. CONTROL ISOLATION & INTERACTIVITY TESTS
    // ==========================================

    [Fact]
    public void MainWindow_IsInteractiveControl_IdentifiesButtonsAndInputsCorrectly()
    {
        RunInSta(() =>
        {
            var button = new Button();
            var textBox = new TextBox();
            var comboBox = new ComboBox();
            var heatmap = new HeatmapControl();
            var clickableText = new TextBlock { Cursor = Cursors.Hand };
            var normalText = new TextBlock();
            var border = new Border();
            var stackPanel = new StackPanel();

            Assert.True(MainWindow.IsInteractiveControl(button), "Button must be interactive");
            Assert.True(MainWindow.IsInteractiveControl(textBox), "TextBox must be interactive");
            Assert.True(MainWindow.IsInteractiveControl(comboBox), "ComboBox must be interactive");
            Assert.True(MainWindow.IsInteractiveControl(heatmap), "HeatmapControl must be interactive");
            Assert.True(MainWindow.IsInteractiveControl(clickableText), "Clickable TextBlock must be interactive");

            Assert.False(MainWindow.IsInteractiveControl(normalText), "Normal TextBlock should not be interactive");
            Assert.False(MainWindow.IsInteractiveControl(border), "Border should not be interactive");
            Assert.False(MainWindow.IsInteractiveControl(stackPanel), "StackPanel should not be interactive");
            Assert.False(MainWindow.IsInteractiveControl(null), "Null should return false");
        });
    }

    [Fact]
    public void MainWindow_IsInteractiveControl_DetectsNestedChildrenInsideButtons()
    {
        RunInSta(() =>
        {
            var button = new Button();
            var innerText = new TextBlock { Text = "Submit" };
            button.Content = innerText;

            // When user clicks the TextBlock inside a Button, IsInteractiveControl must return true
            Assert.True(MainWindow.IsInteractiveControl(innerText), "Child element inside Button must be recognized as interactive");
        });
    }

    // ==========================================
    // 4. WINDOW RESTORATION & DRAGGING LIFECYCLE
    // ==========================================

    [Fact]
    public void MainWindow_RestorePositionFromConfig_AppliesValidSavedCoordinates()
    {
        RunInSta(() =>
        {
            var window = new MainWindow
            {
                Width = 845,
                Height = 275
            };

            window.RestorePositionFromConfig(250.0, 180.0);

            var bounds = PositionHelper.GetVirtualScreenBounds();
            Assert.True(window.Left >= bounds.Left);
            Assert.True(window.Top >= bounds.Top);
            Assert.True(window.Left <= bounds.Left + bounds.Width - window.Width);
            Assert.True(window.Top <= bounds.Top + bounds.Height - window.Height);
            window.Close();
        });
    }

    [Fact]
    public void MainWindow_RestorePositionFromConfig_UsesDefaultWhenNull()
    {
        RunInSta(() =>
        {
            var window = new MainWindow
            {
                Width = 845,
                Height = 275
            };

            window.RestorePositionFromConfig(null, null);

            var expected = PositionHelper.GetDefaultPosition(window.Width, window.Height);
            Assert.Equal(expected.X, window.Left);
            Assert.Equal(expected.Y, window.Top);
            window.Close();
        });
    }

    [Fact]
    public void MainWindow_RestorePositionFromConfig_ClampsWildCoordinates()
    {
        RunInSta(() =>
        {
            var window = new MainWindow
            {
                Width = 845,
                Height = 275
            };

            // Way off-screen to the right/bottom
            window.RestorePositionFromConfig(999999.0, 888888.0);

            var bounds = PositionHelper.GetVirtualScreenBounds();
            Assert.Equal(bounds.Left + bounds.Width - window.Width, window.Left);
            Assert.Equal(bounds.Top + bounds.Height - window.Height, window.Top);
            window.Close();
        });
    }

    [Fact]
    public void MainWindow_DragStateMachine_StartsAndEndsCorrectly()
    {
        RunInSta(() =>
        {
            var configService = CreateTestConfigService();
            var vm = new MainViewModel(
                new LeetCodeService(),
                configService,
                new CacheService(_tempDir),
                new RefreshCoordinator(),
                new AccountVerificationService(new LeetCodeService()));

            var window = new MainWindow(vm)
            {
                Width = 845,
                Height = 275,
                Left = 100,
                Top = 100
            };

            Assert.False(window.IsDragCandidate);
            Assert.False(window.IsDragging);

            // Verify clean drag end on LostMouseCapture without prior drag does not crash
            window.OnWindowLostMouseCapture(window, new MouseEventArgs(Mouse.PrimaryDevice, 0));
            Assert.False(window.IsDragging);
            Assert.False(window.IsDragCandidate);

            window.Close();
        });
    }

    [Fact]
    public void PositionHelper_ClampWindowPosition_OversizedWindow_ClampsToMin()
    {
        var virtualBounds = new Rect(0, 0, 1000, 800);
        double oversizedWidth = 1200; // Wider than 1000
        double oversizedHeight = 900; // Taller than 800

        var clamped = PositionHelper.ClampWindowPosition(50, 50, oversizedWidth, oversizedHeight, virtualBounds);
        Assert.Equal(0, clamped.X);
        Assert.Equal(0, clamped.Y);
    }

    [Fact]
    public async Task MainViewModel_SaveConfigAsync_PreservesWindowCoordinatesOnUsernameOrYearChange()
    {
        var configService = CreateTestConfigService();
        using var vm = new MainViewModel(
            new LeetCodeService(),
            configService,
            new CacheService(_tempDir),
            new RefreshCoordinator(),
            new AccountVerificationService(new LeetCodeService()));

        // Set position first
        await vm.SaveWindowPositionAsync(440.0, 220.0);

        // Change year
        vm.SelectedYear = 2025;

        var loadedConfig = await configService.LoadConfigurationAsync();
        Assert.Equal(440.0, loadedConfig.WindowX);
        Assert.Equal(220.0, loadedConfig.WindowY);
        Assert.Equal(2025, loadedConfig.SelectedYear);
    }

    [Fact]
    public void MainWindow_DragHandleMouseLeftButtonDown_OnInteractiveControl_DoesNotBecomeDragCandidate()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            var button = new Button();

            var mouseArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                Source = button
            };

            window.OnDragHandleMouseLeftButtonDown(button, mouseArgs);

            Assert.False(window.IsDragCandidate, "Clicking an interactive control must not initiate drag candidacy");
            Assert.False(window.IsDragging, "Clicking an interactive control must not start dragging");
            window.Close();
        });
    }

    [Fact]
    public void MainWindow_DesktopLayerStyles_ArePreservedWithPositionChanges()
    {
        RunInSta(() =>
        {
            var window = new MainWindow
            {
                Width = 845,
                Height = 275,
                Left = 100,
                Top = 100
            };

            using var manager = new DesktopWindowManager(window);
            manager.AttachToDesktop();

            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Assert.NotEqual(IntPtr.Zero, hwnd);

            var exStyleBefore = DesktopInterop.GetWindowLong(hwnd, DesktopInterop.GWL_EXSTYLE);
            Assert.True((exStyleBefore & DesktopInterop.WS_EX_TOOLWINDOW) != 0);
            Assert.True((exStyleBefore & DesktopInterop.WS_EX_NOACTIVATE) != 0);

            // Change position
            window.Left = 200;
            window.Top = 220;
            manager.SendToBottom();

            var exStyleAfter = DesktopInterop.GetWindowLong(hwnd, DesktopInterop.GWL_EXSTYLE);
            Assert.True((exStyleAfter & DesktopInterop.WS_EX_TOOLWINDOW) != 0);
            Assert.True((exStyleAfter & DesktopInterop.WS_EX_NOACTIVATE) != 0);

            window.Close();
        });
    }
}
