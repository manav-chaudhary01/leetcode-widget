using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LeetCodeDesktopWidget.Helpers;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using LeetCodeDesktopWidget.Views;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

public class AdaptivePresetAndSettingsTests : IDisposable
{
    private readonly string _tempDir;

    private class FakeLeetCodeService : ILeetCodeService
    {
        public LeetCodeUserStats? StatsResponse { get; set; }

        public Task<LeetCodeResult<string>> GetUserProfileAboutMeAsync(string username, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LeetCodeResult<string>.Success("test profile"));
        }

        public Task<LeetCodeResult<LeetCodeUserStats>> GetUserActivityAsync(string username, int? year = null, CancellationToken cancellationToken = default)
        {
            var stats = StatsResponse ?? new LeetCodeUserStats
            {
                Username = username,
                TotalSolved = 150,
                EasySolved = 90,
                MediumSolved = 50,
                HardSolved = 10,
                ActiveYears = new List<int> { 2026, 2025 },
                LastSyncedAt = DateTime.UtcNow
            };
            return Task.FromResult(LeetCodeResult<LeetCodeUserStats>.Success(stats));
        }

        public IReadOnlyDictionary<DateOnly, int> ParseSubmissionCalendar(string? calendarJson)
        {
            return new Dictionary<DateOnly, int>();
        }
    }

    public AdaptivePresetAndSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lc_preset_tests_" + Guid.NewGuid().ToString("N"));
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
            // Best effort cleanup
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
        thread.Join();

        if (threadEx != null)
        {
            throw new Exception("STA test thread threw an exception: " + threadEx.Message, threadEx);
        }
    }

    [Fact]
    public void PresetDimensions_AllFourPresets_HaveExactExpectedDimensionsAndSettings()
    {
        // 1. Compact (2×2)
        var compact = WidgetPresetDimensions.Compact;
        Assert.Equal(WidgetPreset.Compact, compact.Preset);
        Assert.Equal("Compact", compact.DisplayName);
        Assert.Equal("2×2", compact.GridLabel);
        Assert.Equal(420, compact.Width);
        Assert.Equal(260, compact.Height);
        Assert.Equal(11.5, compact.HeatmapCellSize);
        Assert.Equal(3.0, compact.HeatmapCellSpacing);
        Assert.Equal(24, compact.HeatmapMaxColumns);

        // 2. Standard (4×2)
        var standard = WidgetPresetDimensions.Standard;
        Assert.Equal(WidgetPreset.Standard, standard.Preset);
        Assert.Equal("Standard", standard.DisplayName);
        Assert.Equal("4×2", standard.GridLabel);
        Assert.Equal(845, standard.Width);
        Assert.Equal(285, standard.Height);
        Assert.Equal(11.5, standard.HeatmapCellSize);
        Assert.Equal(3.0, standard.HeatmapCellSpacing);
        Assert.Null(standard.HeatmapMaxColumns);

        // 3. Wide (6×2)
        var wide = WidgetPresetDimensions.Wide;
        Assert.Equal(WidgetPreset.Wide, wide.Preset);
        Assert.Equal("Wide", wide.DisplayName);
        Assert.Equal("6×2", wide.GridLabel);
        Assert.Equal(1180, wide.Width);
        Assert.Equal(285, wide.Height);
        Assert.Equal(15.0, wide.HeatmapCellSize);
        Assert.Equal(3.0, wide.HeatmapCellSpacing);
        Assert.Null(wide.HeatmapMaxColumns);

        // 4. Large (6×3)
        var large = WidgetPresetDimensions.Large;
        Assert.Equal(WidgetPreset.Large, large.Preset);
        Assert.Equal("Large", large.DisplayName);
        Assert.Equal("6×3", large.GridLabel);
        Assert.Equal(1180, large.Width);
        Assert.Equal(410, large.Height);
        Assert.Equal(16.5, large.HeatmapCellSize);
        Assert.Equal(4.0, large.HeatmapCellSpacing);
        Assert.Null(large.HeatmapMaxColumns);
    }

    [Theory]
    [InlineData(WidgetPreset.Compact, 420, 260)]
    [InlineData(WidgetPreset.Standard, 845, 285)]
    [InlineData(WidgetPreset.Wide, 1180, 285)]
    [InlineData(WidgetPreset.Large, 1180, 410)]
    public void ForPreset_ReturnsExpectedInstance(WidgetPreset preset, double expectedWidth, double expectedHeight)
    {
        var dims = WidgetPresetDimensions.ForPreset(preset);
        Assert.Equal(preset, dims.Preset);
        Assert.Equal(expectedWidth, dims.Width);
        Assert.Equal(expectedHeight, dims.Height);
    }

    [Fact]
    public void Configuration_DefaultPreset_IsStandard()
    {
        var config = new WidgetConfig();
        Assert.Equal(WidgetPreset.Standard, config.Preset);
        Assert.False(config.LockPosition);
        Assert.True(config.RememberPosition);
        Assert.False(config.StartWithWindows);
    }

    [Fact]
    public async Task Configuration_LegacyConfigWithoutPreset_DefaultsToStandard()
    {
        var configPath = Path.Combine(_tempDir, "legacy_config.json");
        // Legacy JSON without Preset property
        var legacyJson = @"{
            ""Username"": ""testuser"",
            ""WindowX"": 120.0,
            ""WindowY"": 80.0,
            ""VerificationVersion"": 1
        }";
        await File.WriteAllTextAsync(configPath, legacyJson);

        var configService = new ConfigurationService(configPath);
        var loaded = await configService.LoadConfigurationAsync();

        Assert.Equal(WidgetPreset.Standard, loaded.Preset);
        Assert.Equal("testuser", loaded.Username);
        Assert.Equal(120.0, loaded.WindowX);
    }

    [Fact]
    public async Task Configuration_CorruptPreset_FallsBackSafelyToStandard()
    {
        var configPath = Path.Combine(_tempDir, "corrupt_config.json");
        var corruptJson = @"{
            ""Username"": ""testuser"",
            ""Preset"": ""UltraMegaGiant"",
            ""VerificationVersion"": 1
        }";
        await File.WriteAllTextAsync(configPath, corruptJson);

        var configService = new ConfigurationService(configPath);
        var loaded = await configService.LoadConfigurationAsync();

        Assert.Equal(WidgetPreset.Standard, loaded.Preset);
    }

    [Fact]
    public async Task Configuration_PersistsPresetAndReloadsAccurately()
    {
        var configService = CreateTestConfigService();
        var config = (await configService.LoadConfigurationAsync()) with
        {
            Preset = WidgetPreset.Wide,
            LockPosition = true,
            RememberPosition = true,
            StartWithWindows = false
        };

        await configService.SaveConfigurationAsync(config);

        var reloaded = await configService.LoadConfigurationAsync();
        Assert.Equal(WidgetPreset.Wide, reloaded.Preset);
        Assert.True(reloaded.LockPosition);
        Assert.True(reloaded.RememberPosition);
        Assert.False(reloaded.StartWithWindows);
    }

    [Fact]
    public async Task MainViewModel_InitializesWithConfiguredPreset()
    {
        var configService = CreateTestConfigService();
        var cfg = new WidgetConfig { Preset = WidgetPreset.Large };
        await configService.SaveConfigurationAsync(cfg);

        var mockLeetCode = new FakeLeetCodeService();
        var vm = new MainViewModel(
            mockLeetCode,
            configService,
            new CacheService(Path.Combine(_tempDir, "cache")),
            new RefreshCoordinator(),
            new AccountVerificationService(mockLeetCode));

        await vm.InitializeAsync();

        Assert.Equal(WidgetPreset.Large, vm.CurrentPreset);
        Assert.True(vm.IsLargePreset);
        Assert.False(vm.IsCompactPreset);
        Assert.False(vm.IsStandardPreset);
        Assert.False(vm.IsWidePreset);
        Assert.Equal(1180, vm.CurrentPresetDimensions.Width);
        Assert.Equal(410, vm.CurrentPresetDimensions.Height);
    }

    [Fact]
    public async Task MainViewModel_SelectPresetCommand_SwitchesPresetAndFiresEvent()
    {
        var configService = CreateTestConfigService();
        var mockLeetCode = new FakeLeetCodeService();
        var vm = new MainViewModel(
            mockLeetCode,
            configService,
            new CacheService(Path.Combine(_tempDir, "cache")),
            new RefreshCoordinator(),
            new AccountVerificationService(mockLeetCode));

        await vm.InitializeAsync();
        Assert.Equal(WidgetPreset.Standard, vm.CurrentPreset);

        WidgetPreset? eventPreset = null;
        vm.PresetChanged += (s, p) => eventPreset = p;

        // Switch with enum
        vm.SelectPresetCommand.Execute(WidgetPreset.Compact);
        Assert.Equal(WidgetPreset.Compact, vm.CurrentPreset);
        Assert.Equal(WidgetPreset.Compact, eventPreset);
        Assert.True(vm.IsCompactPreset);
        Assert.False(vm.IsStandardPreset);

        // Switch with string parameter (e.g. from XAML CommandParameter="Wide")
        vm.SelectPresetCommand.Execute("Wide");
        Assert.Equal(WidgetPreset.Wide, vm.CurrentPreset);
        Assert.Equal(WidgetPreset.Wide, eventPreset);
        Assert.True(vm.IsWidePreset);
        Assert.False(vm.IsCompactPreset);
    }

    [Fact]
    public void MainViewModel_SettingsModalCommands_ToggleStateProperly()
    {
        var configService = CreateTestConfigService();
        var mockLeetCode = new FakeLeetCodeService();
        var vm = new MainViewModel(
            mockLeetCode,
            configService,
            new CacheService(Path.Combine(_tempDir, "cache")),
            new RefreshCoordinator(),
            new AccountVerificationService(mockLeetCode));

        Assert.False(vm.IsSettingsOpen);

        vm.OpenSettingsCommand.Execute(null);
        Assert.True(vm.IsSettingsOpen);

        vm.CloseSettingsCommand.Execute(null);
        Assert.False(vm.IsSettingsOpen);

        vm.ToggleSettingsCommand.Execute(null);
        Assert.True(vm.IsSettingsOpen);

        vm.ToggleSettingsCommand.Execute(null);
        Assert.False(vm.IsSettingsOpen);
    }

    [Fact]
    public void MainViewModel_DesktopOptionToggles_UpdateViewModelProperties()
    {
        var configService = CreateTestConfigService();
        var mockLeetCode = new FakeLeetCodeService();
        var vm = new MainViewModel(
            mockLeetCode,
            configService,
            new CacheService(Path.Combine(_tempDir, "cache")),
            new RefreshCoordinator(),
            new AccountVerificationService(mockLeetCode));

        Assert.False(vm.IsPositionLocked);
        vm.ToggleLockPositionCommand.Execute(null);
        Assert.True(vm.IsPositionLocked);

        Assert.True(vm.RememberPosition);
        vm.ToggleRememberPositionCommand.Execute(null);
        Assert.False(vm.RememberPosition);
    }

    [Fact]
    public void MainViewModel_SetRefreshIntervalCommand_UpdatesInterval()
    {
        var configService = CreateTestConfigService();
        var mockLeetCode = new FakeLeetCodeService();
        var coordinator = new RefreshCoordinator();
        var vm = new MainViewModel(
            mockLeetCode,
            configService,
            new CacheService(Path.Combine(_tempDir, "cache")),
            coordinator,
            new AccountVerificationService(mockLeetCode));

        // Set to 15 min
        vm.SetRefreshIntervalCommand.Execute("15");
        Assert.Equal(15, vm.RefreshIntervalMinutes);
        Assert.Equal(TimeSpan.FromMinutes(15), coordinator.Interval);

        // Set to 30 min
        vm.SetRefreshIntervalCommand.Execute(30);
        Assert.Equal(30, vm.RefreshIntervalMinutes);
        Assert.Equal(TimeSpan.FromMinutes(30), coordinator.Interval);

        // Set to 0 (Manual)
        vm.SetRefreshIntervalCommand.Execute(0);
        Assert.Equal(0, vm.RefreshIntervalMinutes);
        Assert.False(coordinator.IsRunning);
    }

    [Fact]
    public void MainViewModel_ResetPositionCommand_TriggersRequestResetPositionEvent()
    {
        var configService = CreateTestConfigService();
        var mockLeetCode = new FakeLeetCodeService();
        var vm = new MainViewModel(
            mockLeetCode,
            configService,
            new CacheService(Path.Combine(_tempDir, "cache")),
            new RefreshCoordinator(),
            new AccountVerificationService(mockLeetCode));

        bool resetRequested = false;
        vm.RequestResetPosition += (s, e) => resetRequested = true;

        vm.ResetPositionCommand.Execute(null);
        Assert.True(resetRequested);
    }

    [Fact]
    public void MainViewModel_ContextMenuCommands_AreInstantiated()
    {
        var configService = CreateTestConfigService();
        var mockLeetCode = new FakeLeetCodeService();
        var vm = new MainViewModel(
            mockLeetCode,
            configService,
            new CacheService(Path.Combine(_tempDir, "cache")),
            new RefreshCoordinator(),
            new AccountVerificationService(mockLeetCode));

        Assert.NotNull(vm.OpenDailyChallengeCommand);
        Assert.NotNull(vm.OpenContestCommand);
        Assert.NotNull(vm.OpenLeetCodeProfileCommand);
        Assert.NotNull(vm.ExitApplicationCommand);
    }

    [Fact]
    public void HeatmapControl_RespectsCellSize_AndMaxColumns()
    {
        RunInSta(() =>
        {
            var heatmap = new HeatmapControl
            {
                CellSize = 16.5,
                CellSpacing = 3.5,
                MaxColumns = 24
            };

            Assert.Equal(16.5, heatmap.CellSize);
            Assert.Equal(3.5, heatmap.CellSpacing);
            Assert.Equal(24, heatmap.MaxColumns);

            heatmap.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            // 24 cols * (16.5 + 3.5) + GridOriginX(28) = 24 * 20 + 28 = 508
            Assert.Equal(508, heatmap.DesiredSize.Width);
            // 7 rows * (16.5 + 3.5) + GridOriginY(18) = 7 * 20 + 18 = 158
            Assert.Equal(158, heatmap.DesiredSize.Height);
        });
    }

    [Fact]
    public void MainWindow_ApplyPresetDimensions_UpdatesDimensionsAccurately()
    {
        RunInSta(() =>
        {
            var mockLeetCode = new FakeLeetCodeService();
            var vm = new MainViewModel(
                mockLeetCode,
                CreateTestConfigService(),
                new CacheService(Path.Combine(_tempDir, "cache")),
                new RefreshCoordinator(),
                new AccountVerificationService(mockLeetCode));

            var window = new MainWindow(vm);
            window.ApplyPresetDimensions(WidgetPresetDimensions.Compact);
            Assert.Equal(420, window.Width);
            Assert.Equal(260, window.Height);

            window.ApplyPresetDimensions(WidgetPresetDimensions.Wide);
            Assert.Equal(1180, window.Width);
            Assert.Equal(285, window.Height);

            window.ApplyPresetDimensions(WidgetPresetDimensions.Large);
            Assert.Equal(1180, window.Width);
            Assert.Equal(410, window.Height);
        });
    }

    [Fact]
    public void MainWindow_PositionLocked_BlocksDragInitiation()
    {
        RunInSta(() =>
        {
            var mockLeetCode = new FakeLeetCodeService();
            var vm = new MainViewModel(
                mockLeetCode,
                CreateTestConfigService(),
                new CacheService(Path.Combine(_tempDir, "cache")),
                new RefreshCoordinator(),
                new AccountVerificationService(mockLeetCode))
            {
                IsPositionLocked = true
            };

            var window = new MainWindow(vm);
            var mouseEventArgs = new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                0,
                MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };

            window.OnDragHandleMouseLeftButtonDown(window, mouseEventArgs);

            // Drag should be blocked when locked
            Assert.False(window.IsDragCandidate);
            Assert.False(window.IsDragging);
        });
    }

    [Fact]
    public void MainWindow_PresetCycle_MaintainsValidDimensions()
    {
        RunInSta(() =>
        {
            var mockLeetCode = new FakeLeetCodeService();
            var vm = new MainViewModel(
                mockLeetCode,
                CreateTestConfigService(),
                new CacheService(Path.Combine(_tempDir, "cache")),
                new RefreshCoordinator(),
                new AccountVerificationService(mockLeetCode));

            var window = new MainWindow(vm);

            // Cycle through all presets
            var presets = new[] { WidgetPreset.Compact, WidgetPreset.Standard, WidgetPreset.Wide, WidgetPreset.Large, WidgetPreset.Compact };
            foreach (var preset in presets)
            {
                var dims = WidgetPresetDimensions.ForPreset(preset);
                window.ApplyPresetDimensions(dims);
                Assert.Equal(dims.Width, window.Width);
                Assert.Equal(dims.Height, window.Height);
            }
        });
    }

    [Fact]
    public void StartupHelper_IsStartWithWindowsEnabled_DoesNotThrow()
    {
        var result = StartupHelper.IsStartWithWindowsEnabled();
        // Result is boolean and executes safely
        Assert.True(result || !result);
    }

    [Fact]
    public void IsInteractiveControl_IdentifiesAllInteractiveControlTypes()
    {
        RunInSta(() =>
        {
            // ButtonBase and derivatives
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.Button()));
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.CheckBox()));
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.RadioButton()));
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.Primitives.ToggleButton()));

            // Text inputs
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.TextBox()));
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.PasswordBox()));
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.RichTextBox()));

            // Dropdowns & menus
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.ComboBox()));
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.ComboBoxItem()));
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.MenuItem()));

            // Scrollbars & heatmap
            Assert.True(MainWindow.IsInteractiveControl(new System.Windows.Controls.Primitives.ScrollBar()));
            Assert.True(MainWindow.IsInteractiveControl(new HeatmapControl()));

            // Interactive TextBlock with Hand cursor or MouseBinding
            var handTb = new System.Windows.Controls.TextBlock { Cursor = Cursors.Hand };
            Assert.True(MainWindow.IsInteractiveControl(handTb));

            var boundTb = new System.Windows.Controls.TextBlock();
            boundTb.InputBindings.Add(new MouseBinding());
            Assert.True(MainWindow.IsInteractiveControl(boundTb));

            // Nested child inside button
            var innerText = new System.Windows.Controls.TextBlock { Text = "Click me" };
            var btn = new System.Windows.Controls.Button { Content = innerText };
            Assert.True(MainWindow.IsInteractiveControl(innerText));

            // Non-interactive controls
            Assert.False(MainWindow.IsInteractiveControl(new System.Windows.Controls.Border()));
            Assert.False(MainWindow.IsInteractiveControl(new System.Windows.Controls.Grid()));
            Assert.False(MainWindow.IsInteractiveControl(new System.Windows.Controls.TextBlock { Text = "Plain" }));
            Assert.False(MainWindow.IsInteractiveControl(null));
        });
    }

    [Fact]
    public void MainWindow_DragInitiation_BlockedWhenSettingsOrVerificationOpen()
    {
        RunInSta(() =>
        {
            var mockLeetCode = new FakeLeetCodeService();
            var vm = new MainViewModel(
                mockLeetCode,
                CreateTestConfigService(),
                new CacheService(Path.Combine(_tempDir, "cache")),
                new RefreshCoordinator(),
                new AccountVerificationService(mockLeetCode));

            var window = new MainWindow(vm);
            var mouseEventArgs = new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                0,
                MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };

            // 1. Settings Open -> Drag must be blocked
            vm.IsSettingsOpen = true;
            window.OnDragHandleMouseLeftButtonDown(window, mouseEventArgs);
            Assert.False(window.IsDragCandidate);
            Assert.False(window.IsDragging);

            // 2. Verification Prompt Open -> Drag must be blocked
            vm.IsSettingsOpen = false;
            vm.IsVerificationPromptOpen = true;
            window.OnDragHandleMouseLeftButtonDown(window, mouseEventArgs);
            Assert.False(window.IsDragCandidate);
            Assert.False(window.IsDragging);

            // 3. Both closed and unlocked -> Drag candidate is allowed
            vm.IsVerificationPromptOpen = false;
            vm.IsPositionLocked = false;
            window.OnDragHandleMouseLeftButtonDown(window, mouseEventArgs);
            Assert.True(window.IsDragCandidate);
        });
    }

    [Fact]
    public void MainWindow_SettingsBackdrop_DismissesSettingsOverlay()
    {
        RunInSta(() =>
        {
            var mockLeetCode = new FakeLeetCodeService();
            var vm = new MainViewModel(
                mockLeetCode,
                CreateTestConfigService(),
                new CacheService(Path.Combine(_tempDir, "cache")),
                new RefreshCoordinator(),
                new AccountVerificationService(mockLeetCode))
            {
                IsSettingsOpen = true
            };

            var window = new MainWindow(vm);
            var mouseEventArgs = new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                0,
                MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };

            // Clicking backdrop dismisses settings
            window.OnSettingsBackdropMouseLeftButtonDown(window, mouseEventArgs);
            Assert.False(vm.IsSettingsOpen);
            Assert.True(mouseEventArgs.Handled);

            // Clicking card absorbs click
            var cardEventArgs = new MouseButtonEventArgs(
                Mouse.PrimaryDevice,
                0,
                MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent
            };
            window.OnSettingsCardMouseLeftButtonDown(window, cardEventArgs);
            Assert.True(cardEventArgs.Handled);
        });
    }

    [Fact]
    public void MainWindow_PresetTemplates_AllContainConsistentTopBarControls()
    {
        RunInSta(() =>
        {
            var mockLeetCode = new FakeLeetCodeService();
            var vm = new MainViewModel(
                mockLeetCode,
                CreateTestConfigService(),
                new CacheService(Path.Combine(_tempDir, "cache")),
                new RefreshCoordinator(),
                new AccountVerificationService(mockLeetCode));

            var window = new MainWindow(vm);

            var templateKeys = new[]
            {
                "CompactPresetTemplate",
                "StandardPresetTemplate",
                "WidePresetTemplate",
                "LargePresetTemplate"
            };

            foreach (var key in templateKeys)
            {
                var template = window.Resources[key] as DataTemplate;
                Assert.NotNull(template);

                var content = template.LoadContent() as FrameworkElement;
                Assert.NotNull(content);

                // Find ComboBox (Year selector)
                var comboBoxes = FindVisualChildren<System.Windows.Controls.ComboBox>(content);
                Assert.NotEmpty(comboBoxes);

                // Find buttons: must include Refresh and Settings
                var buttons = FindVisualChildren<System.Windows.Controls.Button>(content);
                Assert.True(buttons.Count >= 2, $"Template {key} should have at least 2 top bar buttons");

                // Check button min dimensions / hit target consistency
                bool hasRefresh = false;
                bool hasSettings = false;
                foreach (var btn in buttons)
                {
                    if (btn.Content is string s && s.Contains("↻")) hasRefresh = true;
                    if (btn.Content is string s2 && s2.Contains("⚙")) hasSettings = true;
                }
                Assert.True(hasRefresh, $"Template {key} missing refresh button (↻)");
                Assert.True(hasSettings, $"Template {key} missing settings button (⚙)");
            }
        });
    }

    private static List<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
    {
        var list = new List<T>();
        if (depObj == null) return list;

        int count = LogicalTreeHelper.GetChildren(depObj).OfType<object>().Count();
        foreach (var child in LogicalTreeHelper.GetChildren(depObj))
        {
            if (child is DependencyObject d)
            {
                if (d is T match) list.Add(match);
                list.AddRange(FindVisualChildren<T>(d));
            }
        }
        return list;
    }

    [Fact]
    public void RenderPresetSnapshots_GeneratesAllPresetImages()
    {
        RunInSta(() =>
        {
            // Generate realistic submissions across 2026
            var subMap = new Dictionary<DateOnly, int>();
            var today = new DateOnly(2026, 11, 17);
            var rand = new Random(42);
            for (var d = new DateOnly(2026, 1, 1); d <= today; d = d.AddDays(1))
            {
                if (rand.NextDouble() > 0.35)
                {
                    subMap[d] = rand.Next(1, 7);
                }
            }

            var populatedStats = new LeetCodeUserStats
            {
                Username = "manav_chaudhary01",
                TotalSolved = 74,
                EasySolved = 46,
                MediumSolved = 26,
                HardSolved = 2,
                ActiveYears = new List<int> { 2026, 2025 },
                SubmissionCounts = subMap,
                LastSyncedAt = DateTime.UtcNow
            };

            var mockLeetCode = new FakeLeetCodeService { StatsResponse = populatedStats };
            var configService = CreateTestConfigService();
            var cfg = new WidgetConfig
            {
                Username = "manav_chaudhary01",
                VerifiedUsername = "manav_chaudhary01",
                VerifiedAt = DateTime.UtcNow,
                VerificationVersion = WidgetConfig.CurrentVerificationVersion,
                SelectedYear = 2026,
                Preset = WidgetPreset.Compact
            };
            configService.SaveConfigurationAsync(cfg).Wait();

            var vm = new MainViewModel(
                mockLeetCode,
                configService,
                new CacheService(Path.Combine(_tempDir, "cache")),
                new RefreshCoordinator(),
                new AccountVerificationService(mockLeetCode));

            vm.InitializeAsync().Wait();

            string artifactDir = @"C:\Users\Acer\.gemini\antigravity\brain\d96fd338-1171-4bb6-a590-42cd0836b046";

            // 1. Compact (420x260)
            RenderWindowSnapshot(vm, WidgetPreset.Compact, 420, 260, Path.Combine(artifactDir, "widget_compact_screenshot.png"), false);

            // 2. Standard (845x285)
            RenderWindowSnapshot(vm, WidgetPreset.Standard, 845, 285, Path.Combine(artifactDir, "widget_standard_screenshot.png"), false);

            // 3. Wide (1180x285)
            RenderWindowSnapshot(vm, WidgetPreset.Wide, 1180, 285, Path.Combine(artifactDir, "widget_wide_screenshot.png"), false);

            // 4. Large (1180x410)
            RenderWindowSnapshot(vm, WidgetPreset.Large, 1180, 410, Path.Combine(artifactDir, "widget_large_screenshot.png"), false);

            // 5. Settings Modal Overlay (845x285)
            RenderWindowSnapshot(vm, WidgetPreset.Standard, 845, 285, Path.Combine(artifactDir, "widget_settings_screenshot.png"), true);

            // Also update the main widget_runtime_screenshot.png
            RenderWindowSnapshot(vm, WidgetPreset.Standard, 845, 285, Path.Combine(artifactDir, "widget_runtime_screenshot.png"), false);

            Assert.True(File.Exists(Path.Combine(artifactDir, "widget_compact_screenshot.png")));
            Assert.True(File.Exists(Path.Combine(artifactDir, "widget_standard_screenshot.png")));
            Assert.True(File.Exists(Path.Combine(artifactDir, "widget_wide_screenshot.png")));
            Assert.True(File.Exists(Path.Combine(artifactDir, "widget_large_screenshot.png")));
            Assert.True(File.Exists(Path.Combine(artifactDir, "widget_settings_screenshot.png")));
        });
    }

    private static void RenderWindowSnapshot(MainViewModel vm, WidgetPreset preset, int width, int height, string outPath, bool openSettings)
    {
        vm.CurrentPreset = preset;
        vm.IsSettingsOpen = openSettings;

        var window = new MainWindow(vm)
        {
            Width = width,
            Height = height
        };

        if (window.Content is UIElement content)
        {
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(content);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Open(outPath, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        }
    }
}
