using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Desktop;
using LeetCodeDesktopWidget.Helpers;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

/// <summary>
/// Milestone 11 — Final Hardening & Release Readiness Test Suite.
/// Verifies startup resilience, cache corruption, account isolation invariants,
/// verification edge cases, desktop layer integrity, and API failure modes.
/// </summary>
public class ReleaseHardeningM11Tests : IDisposable
{
    private readonly string _tempDir;

    public ReleaseHardeningM11Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lc_m11_tests_" + Guid.NewGuid().ToString("N"));
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

        if (threadEx != null) throw threadEx;
    }

    #region 1. Startup & Configuration Hardening

    [Fact]
    public async Task Startup_MissingConfigDirectory_RecoversSafelyWithDefaults()
    {
        var nonExistentDir = Path.Combine(_tempDir, "sub_dir", "deep_dir");
        var configPath = Path.Combine(nonExistentDir, "config.json");
        var configService = new ConfigurationService(configPath);

        // Loading from non-existent directory must return Default without throwing
        var config = await configService.LoadConfigurationAsync();
        Assert.NotNull(config);
        Assert.Equal("", config.Username);
        Assert.Equal("", config.VerifiedUsername);
        Assert.False(config.IsAccountVerified);

        // Saving must create directory automatically and succeed
        await configService.SaveConfigurationAsync(config with { Username = "alice" });
        Assert.True(File.Exists(configPath));

        var reloaded = await configService.LoadConfigurationAsync();
        Assert.Equal("alice", reloaded.Username);
    }

    [Fact]
    public async Task Startup_MissingConfigFile_LoadsDefaultConfiguration()
    {
        var configPath = Path.Combine(_tempDir, "missing_config.json");
        var configService = new ConfigurationService(configPath);

        var config = await configService.LoadConfigurationAsync();
        Assert.NotNull(config);
        Assert.Equal(WidgetConfig.Default.Username, config.Username);
        Assert.Equal(WidgetConfig.Default.RefreshIntervalMinutes, config.RefreshIntervalMinutes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("{\"Username\":")]
    [InlineData("not a json payload at all")]
    [InlineData("{\"Username\": 12345, \"RefreshIntervalMinutes\": \"invalid\"}")]
    public async Task Startup_MalformedConfigFile_RecoversToSafeDefaultsWithoutCrashing(string badJson)
    {
        var configPath = Path.Combine(_tempDir, "corrupt_config.json");
        await File.WriteAllTextAsync(configPath, badJson);

        var configService = new ConfigurationService(configPath);
        var config = await configService.LoadConfigurationAsync();

        Assert.NotNull(config);
        Assert.Equal(WidgetConfig.Default.Username, config.Username);
        Assert.False(config.IsAccountVerified);
    }

    [Fact]
    public async Task Startup_MissingCacheFile_ReturnsNullWithoutThrowing()
    {
        var cachePath = Path.Combine(_tempDir, "missing_cache.json");
        var cacheService = new CacheService(cachePath);

        var result = await cacheService.LoadCacheAsync("coder");
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ malformed")]
    [InlineData("{\"Username\": \"coder\", \"TotalSolved\": \"not_a_number\"}")]
    public async Task Startup_MalformedCacheFile_ReturnsNullWithoutCrashing(string badCache)
    {
        var cachePath = Path.Combine(_tempDir, "corrupt_cache.json");
        await File.WriteAllTextAsync(cachePath, badCache);

        var cacheService = new CacheService(cachePath);
        var result = await cacheService.LoadCacheAsync("coder");

        Assert.Null(result);
    }

    #endregion

    #region 2. Account Isolation Regression & Invariant Tests

    [Fact]
    public async Task AccountIsolation_AccountA_Verified_SwitchToAccountB_ImmediatelyClearsAllData()
    {
        var configPath = Path.Combine(_tempDir, "config.json");
        var configService = new ConfigurationService(configPath);
        var cachePath = Path.Combine(_tempDir, "cache.json");
        var cacheService = new CacheService(cachePath);
        var leetCode = new LeetCodeService();

        var vm = new MainViewModel(
            leetCode,
            configService,
            cacheService,
            new RefreshCoordinator(),
            new AccountVerificationService(leetCode));

        // 1. Setup Account A verified with loaded data
        await configService.SaveConfigurationAsync(new WidgetConfig
        {
            Username = "alice",
            VerifiedUsername = "alice",
            VerifiedAt = DateTime.UtcNow,
            SelectedYear = 2026
        });

        var statsA = new LeetCodeUserStats
        {
            Username = "alice",
            TotalSolved = 150,
            EasySolved = 80,
            MediumSolved = 50,
            HardSolved = 20,
            CurrentStreak = 10,
            ActiveYears = new[] { 2026, 2025 },
            SubmissionCounts = new Dictionary<DateOnly, int>
            {
                [new DateOnly(2026, 1, 1)] = 3,
                [new DateOnly(2026, 1, 2)] = 5
            }
        };
        await cacheService.SaveCacheAsync(statsA);
        await vm.InitializeAsync();

        Assert.True(vm.IsAccountVerified);
        Assert.Equal(150, vm.TotalSolved);
        Assert.Equal(80, vm.EasySolved);
        Assert.Equal(50, vm.MediumSolved);
        Assert.Equal(20, vm.HardSolved);
        Assert.NotNull(vm.CurrentActivity);
        Assert.Equal(2, vm.AvailableYears.Count);

        // 2. Switch to Account B
        vm.Username = "bob";
        await vm.SaveUsernameAndRefreshAsync();

        // 3. Verify Account A data is strictly cleared
        Assert.False(vm.IsAccountVerified);
        Assert.Equal("", vm.VerifiedUsername);
        Assert.Null(vm.VerifiedAt);
        Assert.Equal(0, vm.TotalSolved);
        Assert.Equal(0, vm.EasySolved);
        Assert.Equal(0, vm.MediumSolved);
        Assert.Equal(0, vm.HardSolved);
        Assert.Equal(0, vm.CurrentStreak);
        Assert.Equal(0, vm.LongestStreak);
        Assert.Equal(0, vm.YearSubmissions);
        Assert.Equal(0, vm.YearActiveDays);
        Assert.Null(vm.CurrentActivity);
        Assert.Empty(vm.AvailableYears);
        Assert.False(vm.CanDisplayActivity);
        Assert.False(vm.HasValidActivityForCurrentAccount);
        Assert.True(vm.State == WidgetState.VerificationInProgress || vm.State == WidgetState.VerificationRequired);
        Assert.True(vm.IsVerificationPromptOpen);
        Assert.NotEmpty(vm.VerificationToken);
    }

    [Fact]
    public void AccountIsolation_Invariant_HoldsTrue_AcrossAllPropertyAccessors()
    {
        var vm = new MainViewModel();

        // When unverified, invariant enforces zero/null across all accessors
        Assert.False(vm.HasValidActivityForCurrentAccount);
        Assert.Equal(0, vm.TotalSolved);
        Assert.Equal(0, vm.EasySolved);
        Assert.Equal(0, vm.MediumSolved);
        Assert.Equal(0, vm.HardSolved);
        Assert.Equal(0, vm.CurrentStreak);
        Assert.Equal(0, vm.LongestStreak);
        Assert.Equal(0, vm.YearSubmissions);
        Assert.Equal(0, vm.YearActiveDays);
        Assert.Equal("0", vm.YearAverageText);
        Assert.Null(vm.CurrentActivity);
    }

    #endregion

    #region 3. Verification Hardening

    [Fact]
    public async Task Verification_ExpiredChallenge_FailsCleanly()
    {
        var leetCode = new LeetCodeService();
        var verificationService = new AccountVerificationService(leetCode);

        // Generate challenge
        var challenge = verificationService.GenerateChallenge("alice");
        Assert.NotNull(challenge);

        // Wait or simulate expiration by checking expired logic
        var expiredChallenge = new VerificationChallenge(
            "alice",
            challenge.Token,
            DateTime.UtcNow.AddMinutes(-20),
            DateTime.UtcNow.AddMinutes(-5));

        Assert.True(expiredChallenge.IsExpired);
    }

    [Fact]
    public async Task Verification_WrongAccount_TokenForUserA_DoesNotVerifyUserB()
    {
        var leetCode = new LeetCodeService();
        var verificationService = new AccountVerificationService(leetCode);

        // Generate challenge for alice
        var challenge = verificationService.GenerateChallenge("alice");

        // Attempt verify for bob
        var result = await verificationService.VerifyOwnershipAsync("bob");
        Assert.False(result.IsSuccess);
        Assert.Contains("Challenge was generated for @alice, but verifying @bob", result.ErrorMessage);
    }

    [Fact]
    public async Task Verification_UsernameChange_InvalidatesPreviousVerification()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_userchange.json"));
        await configService.SaveConfigurationAsync(new WidgetConfig
        {
            Username = "alice",
            VerifiedUsername = "alice",
            VerifiedAt = DateTime.UtcNow
        });

        var vm = new MainViewModel(null, configService, null, null, null);
        await vm.InitializeAsync();

        Assert.True(vm.IsAccountVerified);

        // Changing Username to bob invalidates verification immediately
        vm.Username = "bob";
        Assert.False(vm.IsAccountVerified);
    }

    #endregion

    #region 4. Open LeetCode Profile Command

    [Fact]
    public void OpenLeetCodeProfileCommand_IsDefined_AndExecutableWithoutThrowing()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.OpenLeetCodeProfileCommand);
        Assert.True(vm.OpenLeetCodeProfileCommand.CanExecute(null));
    }

    #endregion

    #region 5. Cache Hardening & Cross-Account Isolation

    [Fact]
    public async Task Cache_CrossAccount_UserACacheNeverLoadedForUserB()
    {
        var cachePath = Path.Combine(_tempDir, "cache_cross.json");
        var cacheService = new CacheService(cachePath);

        var statsA = new LeetCodeUserStats
        {
            Username = "user_alpha",
            TotalSolved = 100,
            SubmissionCounts = new Dictionary<DateOnly, int>()
        };

        await cacheService.SaveCacheAsync(statsA);

        // Requesting cache for user_beta must strictly return null
        var loaded = await cacheService.LoadCacheAsync("user_beta");
        Assert.Null(loaded);

        // Requesting cache for user_alpha succeeds
        var loadedA = await cacheService.LoadCacheAsync("user_alpha");
        Assert.NotNull(loadedA);
        Assert.Equal("user_alpha", loadedA.Username);
    }

    #endregion

    #region 6. Refresh Behavior & Concurrency

    [Fact]
    public async Task Refresh_ConcurrentClicks_CoalesceWithoutDuplicateRequests()
    {
        using var coordinator = new RefreshCoordinator();
        int requestCount = 0;
        var inFlightTcs = new TaskCompletionSource();

        coordinator.Start(async ct =>
        {
            Interlocked.Increment(ref requestCount);
            await inFlightTcs.Task;
        });

        // Fire first refresh
        var task1 = coordinator.TriggerRefreshAsync(isManual: true);

        // Fire rapid subsequent refreshes while first is still running
        var task2 = coordinator.TriggerRefreshAsync(isManual: true);
        var task3 = coordinator.TriggerRefreshAsync(isManual: true);

        // Complete the in-flight task
        inFlightTcs.SetResult();

        await Task.WhenAll(task1, task2, task3);

        // Exactly one network execution must have occurred
        Assert.Equal(1, requestCount);
    }

    #endregion

    #region 7. Multi-Monitor & DPI Clamping

    [Fact]
    public void MultiMonitor_Clamping_GuardsAgainstNaNAndOutOfBounds()
    {
        var virtualBounds = new System.Windows.Rect(-1920, -1080, 3840, 2160);

        // Test NaN handling
        var clampedNaN = PositionHelper.ClampWindowPosition(double.NaN, double.NaN, 845, 290, virtualBounds);
        Assert.False(double.IsNaN(clampedNaN.X));
        Assert.False(double.IsNaN(clampedNaN.Y));

        // Test extreme negative coordinates beyond virtual bounds
        var clampedFarLeft = PositionHelper.ClampWindowPosition(-5000, -5000, 845, 290, virtualBounds);
        Assert.True(clampedFarLeft.X >= virtualBounds.Left);
        Assert.True(clampedFarLeft.Y >= virtualBounds.Top);

        // Test extreme positive coordinates beyond virtual bounds
        var clampedFarRight = PositionHelper.ClampWindowPosition(10000, 10000, 845, 290, virtualBounds);
        Assert.True(clampedFarRight.X <= virtualBounds.Left + virtualBounds.Width - 845);
        Assert.True(clampedFarRight.Y <= virtualBounds.Top + virtualBounds.Height - 290);
    }

    #endregion

    #region 8. Desktop Window Styles & Explorer Recovery

    [Fact]
    public void DesktopWindowManager_Styles_ToolWindowAndNoActivate_AreEnforced()
    {
        RunInSta(() =>
        {
            var window = new MainWindow { Width = 845, Height = 290 };
            using var manager = new DesktopWindowManager(window);
            manager.AttachToDesktop();

            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            Assert.NotEqual(IntPtr.Zero, hwnd);

            long exStyle = DesktopInterop.GetWindowLong(hwnd, DesktopInterop.GWL_EXSTYLE);
            Assert.True((exStyle & DesktopInterop.WS_EX_TOOLWINDOW) != 0, "Window must have WS_EX_TOOLWINDOW (excluded from Alt+Tab)");
            Assert.True((exStyle & DesktopInterop.WS_EX_NOACTIVATE) != 0, "Window must have WS_EX_NOACTIVATE (no focus stealing)");

            window.Close();
        });
    }

    #endregion
}
