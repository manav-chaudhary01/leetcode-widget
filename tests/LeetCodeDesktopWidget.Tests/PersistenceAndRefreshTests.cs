using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Helpers;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

public class PersistenceAndRefreshTests : IDisposable
{
    private readonly string _tempDirectory;

    public PersistenceAndRefreshTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LeetCodeWidget_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore temp folder cleanup issues
        }
    }

    #region 1. Configuration Tests

    [Fact]
    public async Task ConfigService_SaveAndLoadValidConfig_PreservesAllFields()
    {
        string configPath = Path.Combine(_tempDirectory, "test_config.json");
        var service = new ConfigurationService(configPath);

        var config = new WidgetConfig
        {
            Username = "leetcode_master",
            SelectedYear = 2024,
            RefreshIntervalMinutes = 30
        };

        await service.SaveConfigurationAsync(config);
        Assert.True(File.Exists(configPath));

        var loaded = await service.LoadConfigurationAsync();
        Assert.Equal("leetcode_master", loaded.Username);
        Assert.Equal(2024, loaded.SelectedYear);
        Assert.Equal(30, loaded.RefreshIntervalMinutes);
    }

    [Fact]
    public async Task ConfigService_MissingFile_ReturnsDefaultConfig()
    {
        string configPath = Path.Combine(_tempDirectory, "non_existent.json");
        var service = new ConfigurationService(configPath);

        var loaded = await service.LoadConfigurationAsync();
        Assert.NotNull(loaded);
        Assert.Equal("", loaded.Username);
        Assert.Equal(60, loaded.RefreshIntervalMinutes);
    }

    [Fact]
    public async Task ConfigService_CorruptJson_ReturnsDefaultConfigGracefully()
    {
        string configPath = Path.Combine(_tempDirectory, "corrupt_config.json");
        await File.WriteAllTextAsync(configPath, "{ invalid json ::: ");
        var service = new ConfigurationService(configPath);

        var loaded = await service.LoadConfigurationAsync();
        Assert.NotNull(loaded);
        Assert.Equal("", loaded.Username);
    }

    [Theory]
    [InlineData("valid_user", "valid_user")]
    [InlineData("@valid_user", "valid_user")]
    [InlineData("  user-name_123  ", "user-name_123")]
    [InlineData("@user$invalid", "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void UsernameSanitization_HandlesVariousInputs(string input, string expected)
    {
        var sanitized = WidgetConfig.SanitizeUsername(input);
        Assert.Equal(expected, sanitized);
    }

    [Fact]
    public void UsernameSanitization_EnforcesMax40Characters()
    {
        string longUser = new string('a', 60);
        var sanitized = WidgetConfig.SanitizeUsername(longUser);
        Assert.Equal(40, sanitized.Length);
        Assert.Equal(new string('a', 40), sanitized);
    }

    [Fact]
    public async Task ConfigService_SanitizesOnSaveAndClampsInterval()
    {
        string configPath = Path.Combine(_tempDirectory, "clamp_config.json");
        var service = new ConfigurationService(configPath);

        var config = new WidgetConfig
        {
            Username = "  @alpha_beta  ",
            RefreshIntervalMinutes = 2 // Below minimum 5
        };

        await service.SaveConfigurationAsync(config);
        var loaded = await service.LoadConfigurationAsync();

        Assert.Equal("alpha_beta", loaded.Username);
        Assert.Equal(5, loaded.RefreshIntervalMinutes);
    }

    #endregion

    #region 2. Cache Tests

    [Fact]
    public async Task CacheService_SaveAndLoadValidCache_PreservesAllStatistics()
    {
        string cachePath = Path.Combine(_tempDirectory, "test_cache.json");
        var service = new CacheService(cachePath);

        var now = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);
        var stats = new LeetCodeUserStats
        {
            Username = "dev_coder",
            TotalSolved = 450,
            EasySolved = 200,
            MediumSolved = 180,
            HardSolved = 70,
            ActiveYears = new List<int> { 2026, 2025, 2024 },
            SubmissionCounts = new Dictionary<DateOnly, int>
            {
                { new DateOnly(2026, 3, 14), 5 },
                { new DateOnly(2026, 3, 15), 10 }
            },
            LastSyncedAt = now
        };

        await service.SaveCacheAsync(stats);
        Assert.True(File.Exists(cachePath));

        var cached = await service.LoadCacheAsync("dev_coder");
        Assert.NotNull(cached);
        Assert.Equal("dev_coder", cached.Username);
        Assert.Equal(450, cached.TotalSolved);
        Assert.Equal(200, cached.EasySolved);
        Assert.Equal(180, cached.MediumSolved);
        Assert.Equal(70, cached.HardSolved);
        Assert.Equal(now, cached.LastSuccessfulSync);
        Assert.Equal(2, cached.Submissions.Count);
        Assert.Equal(5, cached.Submissions["2026-03-14"]);
        Assert.Equal(10, cached.Submissions["2026-03-15"]);

        // Verify conversion roundtrip to user stats
        var roundtrip = cached.ToUserStats();
        Assert.Equal(stats.Username, roundtrip.Username);
        Assert.Equal(stats.TotalSolved, roundtrip.TotalSolved);
        Assert.Equal(2, roundtrip.SubmissionCounts.Count);
        Assert.Equal(10, roundtrip.SubmissionCounts[new DateOnly(2026, 3, 15)]);
    }

    [Fact]
    public async Task CacheService_UsernameMismatch_ReturnsNull()
    {
        string cachePath = Path.Combine(_tempDirectory, "mismatch_cache.json");
        var service = new CacheService(cachePath);

        var stats = new LeetCodeUserStats
        {
            Username = "alice",
            TotalSolved = 100
        };

        await service.SaveCacheAsync(stats);

        // Requesting for bob should discard alice's cache
        var cached = await service.LoadCacheAsync("bob");
        Assert.Null(cached);

        // Requesting for alice returns it
        var aliceCached = await service.LoadCacheAsync("alice");
        Assert.NotNull(aliceCached);
    }

    [Fact]
    public async Task CacheService_CorruptJson_ReturnsNullGracefully()
    {
        string cachePath = Path.Combine(_tempDirectory, "corrupt_cache.json");
        await File.WriteAllTextAsync(cachePath, "Not Valid JSON {{{");
        var service = new CacheService(cachePath);

        var cached = await service.LoadCacheAsync("any_user");
        Assert.Null(cached);
    }

    [Fact]
    public async Task CacheService_MissingFile_ReturnsNull()
    {
        string cachePath = Path.Combine(_tempDirectory, "missing_cache.json");
        var service = new CacheService(cachePath);

        var cached = await service.LoadCacheAsync("any_user");
        Assert.Null(cached);
    }

    [Fact]
    public async Task CacheService_AtomicWrite_CleansUpTempFile()
    {
        string cachePath = Path.Combine(_tempDirectory, "atomic_cache.json");
        var service = new CacheService(cachePath);

        await service.SaveCacheAsync(new LeetCodeUserStats
        {
            Username = "test_atomic",
            TotalSolved = 10
        });

        Assert.True(File.Exists(cachePath));
        Assert.False(File.Exists(cachePath + ".tmp"));
    }

    #endregion

    #region 3. Freshness Formatter Tests

    [Fact]
    public void FreshnessFormatter_GetRelativeTime_FormatsBoundariesCorrectly()
    {
        var refTime = new DateTime(2026, 3, 15, 12, 0, 0);

        // Future or sub-minute
        Assert.Equal("just now", FreshnessFormatter.GetRelativeTime(refTime.AddSeconds(10), refTime));
        Assert.Equal("just now", FreshnessFormatter.GetRelativeTime(refTime.AddSeconds(-30), refTime));

        // Minutes
        Assert.Equal("12m ago", FreshnessFormatter.GetRelativeTime(refTime.AddMinutes(-12), refTime));
        Assert.Equal("59m ago", FreshnessFormatter.GetRelativeTime(refTime.AddMinutes(-59), refTime));

        // Hours
        Assert.Equal("1h ago", FreshnessFormatter.GetRelativeTime(refTime.AddHours(-1), refTime));
        Assert.Equal("5h ago", FreshnessFormatter.GetRelativeTime(refTime.AddHours(-5), refTime));

        // Yesterday
        var yesterday = refTime.Date.AddDays(-1).AddHours(8); // 28h ago on previous calendar day
        Assert.Equal("yesterday", FreshnessFormatter.GetRelativeTime(yesterday, refTime));

        // Days ago (< 7)
        Assert.Equal("3d ago", FreshnessFormatter.GetRelativeTime(refTime.AddDays(-3), refTime));

        // Older than 7 days: formatted date
        var oldDate = new DateTime(2026, 1, 10, 10, 0, 0);
        Assert.Equal("Jan 10", FreshnessFormatter.GetRelativeTime(oldDate, refTime));
    }

    [Fact]
    public void FreshnessFormatter_FormatSyncStatus_FormatsStatesCorrectly()
    {
        var refTime = new DateTime(2026, 3, 15, 12, 0, 0);
        var syncTime = refTime.AddMinutes(-25);

        // Online sync
        string online = FreshnessFormatter.FormatSyncStatus(syncTime, isOffline: false, referenceTime: refTime);
        Assert.Equal("Synced 25m ago", online);

        // Offline with cached data
        string offlineCached = FreshnessFormatter.FormatSyncStatus(syncTime, isOffline: true, referenceTime: refTime);
        Assert.Equal("Offline · cached 25m ago", offlineCached);

        // Never synced
        string neverSynced = FreshnessFormatter.FormatSyncStatus(null, isOffline: false);
        Assert.Equal("Not synced", neverSynced);

        // Offline with no data and error
        string offlineError = FreshnessFormatter.FormatSyncStatus(null, isOffline: true, failureReason: "Network unreachable");
        Assert.Equal("Sync failed · Network unreachable", offlineError);
    }

    #endregion

    #region 4. Refresh Coordinator & Concurrency Protection

    [Fact]
    public async Task RefreshCoordinator_ManualTrigger_ExecutesCallback()
    {
        using var coordinator = new RefreshCoordinator();
        int executionCount = 0;

        coordinator.Start(ct =>
        {
            Interlocked.Increment(ref executionCount);
            return Task.CompletedTask;
        });

        bool triggered = await coordinator.TriggerRefreshAsync(isManual: true);
        Assert.True(triggered);
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public async Task RefreshCoordinator_DuplicateAutomaticTriggers_AreIgnoredWhileRunning()
    {
        using var coordinator = new RefreshCoordinator();
        var tcs = new TaskCompletionSource();
        int executionCount = 0;

        coordinator.Start(async ct =>
        {
            Interlocked.Increment(ref executionCount);
            await tcs.Task;
        });

        // Start first refresh
        var firstTask = coordinator.TriggerRefreshAsync(isManual: true);
        Assert.True(coordinator.IsRefreshing);

        // Second automatic trigger while first is running should be rejected
        bool secondAuto = await coordinator.TriggerRefreshAsync(isManual: false);
        Assert.False(secondAuto);
        Assert.Equal(1, executionCount);

        // Complete first refresh
        tcs.SetResult();
        await firstTask;
        Assert.False(coordinator.IsRefreshing);
    }

    [Fact]
    public async Task RefreshCoordinator_ManualTriggerDuringActiveRefresh_CoalescesWithoutSecondRequest()
    {
        using var coordinator = new RefreshCoordinator();
        var tcs = new TaskCompletionSource();
        int executionCount = 0;

        coordinator.Start(async ct =>
        {
            Interlocked.Increment(ref executionCount);
            await tcs.Task;
        });

        // Start first refresh
        var task1 = coordinator.TriggerRefreshAsync(isManual: true);

        // Second manual trigger while first is running: should coalesce with existing task
        var task2 = coordinator.TriggerRefreshAsync(isManual: true);

        // Unblock callback
        tcs.SetResult();

        var (res1, res2) = (await task1, await task2);
        Assert.True(res1);
        Assert.True(res2);
        // Execution count must strictly be 1 — no second concurrent execution
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public void RefreshCoordinator_StopAndDispose_TerminatesCleanly()
    {
        var coordinator = new RefreshCoordinator();
        coordinator.Start(ct => Task.CompletedTask);
        Assert.True(coordinator.IsRunning);

        coordinator.Stop();
        Assert.False(coordinator.IsRunning);

        coordinator.Dispose();
        Assert.False(coordinator.IsRunning);
    }

    #endregion

    #region 5. MainViewModel Startup, Cache & Recovery Flow

    [Fact]
    public async Task MainViewModel_InitializeAsync_RestoresConfigurationAndCacheImmediately()
    {
        string configPath = Path.Combine(_tempDirectory, "startup_config.json");
        string cachePath = Path.Combine(_tempDirectory, "startup_cache.json");

        var configService = new ConfigurationService(configPath);
        var cacheService = new CacheService(cachePath);

        // Pre-populate persisted config
        await configService.SaveConfigurationAsync(new WidgetConfig
        {
            Username = "persisted_user",
            SelectedYear = 2025,
            RefreshIntervalMinutes = 45
        });

        // Pre-populate persisted cache
        var lastSync = new DateTime(2026, 3, 15, 11, 45, 0);
        await cacheService.SaveCacheAsync(new LeetCodeUserStats
        {
            Username = "persisted_user",
            TotalSolved = 280,
            EasySolved = 100,
            MediumSolved = 130,
            HardSolved = 50,
            ActiveYears = new List<int> { 2026, 2025 },
            SubmissionCounts = new Dictionary<DateOnly, int>
            {
                { new DateOnly(2025, 6, 1), 3 }
            },
            LastSyncedAt = lastSync
        });

        // HTTP handler throws if network is attempted, proving cache restores offline/instantly
        var handler = new MockHttpMessageHandler(req => throw new HttpRequestException("Offline"));
        var leetService = new LeetCodeService(new HttpClient(handler));

        var vm = new MainViewModel(leetService, configService, cacheService)
        {
            ReferenceDateTime = new DateTime(2026, 3, 15, 12, 0, 0)
        };

        // Initialize (simulating application startup)
        await vm.InitializeAsync();

        // 1. Config restored
        Assert.Equal("persisted_user", vm.Username);
        Assert.Equal(2025, vm.SelectedYear);

        // 2. Cache restored immediately
        Assert.Equal(280, vm.TotalSolved);
        Assert.Equal(100, vm.EasySolved);
        Assert.Equal(130, vm.MediumSolved);
        Assert.Equal(50, vm.HardSolved);
        Assert.True(vm.HasSolvedStats);
        Assert.Equal(3, vm.YearSubmissions);
        Assert.Equal(1, vm.YearActiveDays);

        // 3. Freshness indicates cached time
        Assert.Contains("15m ago", vm.SyncStatusText);
    }

    [Fact]
    public async Task MainViewModel_InitializeAsync_WithNoConfig_ShowsDefaultPrompt()
    {
        string configPath = Path.Combine(_tempDirectory, "empty_config.json");
        string cachePath = Path.Combine(_tempDirectory, "empty_cache.json");

        var vm = new MainViewModel(
            new LeetCodeService(),
            new ConfigurationService(configPath),
            new CacheService(cachePath));

        await vm.InitializeAsync();

        Assert.Equal("", vm.Username);
        Assert.Equal("Configure username to start", vm.SyncStatusText);
        Assert.False(vm.HasSolvedStats);
    }

    [Fact]
    public async Task MainViewModel_SaveUsername_SanitizesAndPersistsNewConfiguration()
    {
        string configPath = Path.Combine(_tempDirectory, "save_user_config.json");
        string cachePath = Path.Combine(_tempDirectory, "save_user_cache.json");

        var configService = new ConfigurationService(configPath);
        var cacheService = new CacheService(cachePath);

        // Mock LeetCode returns empty success
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(@"{
                ""data"": {
                    ""matchedUser"": {
                        ""username"": ""new_champion"",
                        ""userCalendar"": { ""activeYears"": [2026], ""streak"": 0, ""totalActiveDays"": 0, ""submissionCalendar"": ""{}"" },
                        ""submitStatsGlobal"": { ""acSubmissionNum"": [{ ""difficulty"": ""All"", ""count"": 5, ""submissions"": 5 }] }
                    }
                }
            }", Encoding.UTF8, "application/json")
        });

        var vm = new MainViewModel(
            new LeetCodeService(new HttpClient(handler)),
            configService,
            cacheService)
        {
            Username = "   @new_champion   ",
            IsEditingUsername = true
        };

        // Save username via command
        vm.SaveUsernameCommand.Execute(null);

        // Username sanitized
        Assert.Equal("new_champion", vm.Username);
        Assert.False(vm.IsEditingUsername);

        // Persisted to config file
        var savedConfig = await configService.LoadConfigurationAsync();
        Assert.Equal("new_champion", savedConfig.Username);
    }

    #endregion
}
