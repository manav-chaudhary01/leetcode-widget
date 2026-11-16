using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

public class AccountIsolationAndVerificationGateTests : IDisposable
{
    private readonly string _tempDir;

    public AccountIsolationAndVerificationGateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lc_widget_m85_tests_" + Guid.NewGuid().ToString("N"));
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

    private class MockVerificationLeetCodeService : ILeetCodeService
    {
        public string AboutMe { get; set; } = "";
        public bool ReturnFailure { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, LeetCodeUserStats> UserStatsMap { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<LeetCodeResult<string>> GetUserProfileAboutMeAsync(string username, CancellationToken cancellationToken = default)
        {
            if (ReturnFailure)
            {
                return Task.FromResult(LeetCodeResult<string>.Failure(ErrorMessage ?? "Profile fetch failed", LeetCodeErrorCode.NetworkError));
            }
            return Task.FromResult(LeetCodeResult<string>.Success(AboutMe));
        }

        public Task<LeetCodeResult<LeetCodeUserStats>> GetUserActivityAsync(string username, int? year = null, CancellationToken cancellationToken = default)
        {
            if (ReturnFailure)
            {
                return Task.FromResult(LeetCodeResult<LeetCodeUserStats>.Failure(ErrorMessage ?? "Sync failed", LeetCodeErrorCode.NetworkError));
            }

            if (UserStatsMap.TryGetValue(username, out var stats))
            {
                return Task.FromResult(LeetCodeResult<LeetCodeUserStats>.Success(stats));
            }

            return Task.FromResult(LeetCodeResult<LeetCodeUserStats>.Failure("User not found", LeetCodeErrorCode.UserNotFound));
        }

        public IReadOnlyDictionary<DateOnly, int> ParseSubmissionCalendar(string? calendarJson)
        {
            return new Dictionary<DateOnly, int>();
        }
    }

    #region 1. Verification Gate Tests (1 - 7)

    [Fact]
    public async Task Gate_1_FreshConfiguration_StartsInUnconfiguredOrVerificationRequired()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_gate1.json"));
        var vm = new MainViewModel(null, configService, null, null, null);

        await vm.InitializeAsync();

        Assert.Equal(WidgetState.Unconfigured, vm.State);
        Assert.True(vm.IsUnconfigured);
        Assert.False(vm.IsAccountVerified);
        Assert.False(vm.CanDisplayActivity);
    }

    [Fact]
    public async Task Gate_2_UnverifiedUsername_CannotExposeHeatmap()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_gate2.json"));
        await configService.SaveConfigurationAsync(new WidgetConfig { Username = "unverified_dev" });

        var vm = new MainViewModel(null, configService, null, null, null);
        await vm.InitializeAsync();

        Assert.Equal(WidgetState.VerificationRequired, vm.State);
        Assert.False(vm.IsAccountVerified);
        Assert.Null(vm.CurrentActivity);
        Assert.False(vm.CanDisplayActivity);
    }

    [Fact]
    public async Task Gate_3_UnverifiedUsername_CannotExposeStatistics()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_gate3.json"));
        await configService.SaveConfigurationAsync(new WidgetConfig { Username = "unverified_dev" });

        var vm = new MainViewModel(null, configService, null, null, null);
        await vm.InitializeAsync();

        Assert.Equal(0, vm.TotalSolved);
        Assert.Equal(0, vm.EasySolved);
        Assert.Equal(0, vm.MediumSolved);
        Assert.Equal(0, vm.HardSolved);
        Assert.Equal(0, vm.CurrentStreak);
        Assert.Equal(0, vm.LongestStreak);
        Assert.Equal(0, vm.YearSubmissions);
        Assert.Equal(0, vm.YearActiveDays);
        Assert.Equal("0", vm.YearAverageText);
        Assert.Equal("None", vm.MostActiveDayText);
        Assert.False(vm.HasSolvedStats);
    }

    [Fact]
    public async Task Gate_4_UnverifiedUsername_CannotExposeCachedActivity()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_gate4.json"));
        var cacheService = new CacheService(Path.Combine(_tempDir, "cache_gate4.json"));

        // Pre-save cached stats for unverified_dev
        await cacheService.SaveCacheAsync(new LeetCodeUserStats
        {
            Username = "unverified_dev",
            TotalSolved = 150,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { new DateOnly(2026, 1, 1), 5 } },
            LastSyncedAt = DateTime.UtcNow
        });

        // Config has unverified_dev WITHOUT verification
        await configService.SaveConfigurationAsync(new WidgetConfig { Username = "unverified_dev" });

        var vm = new MainViewModel(null, configService, cacheService, null, null);
        await vm.InitializeAsync();

        // Must reject loading or exposing cache
        Assert.False(vm.IsAccountVerified);
        Assert.Null(vm.CurrentActivity);
        Assert.Equal(0, vm.TotalSolved);
        Assert.Equal(0, vm.YearSubmissions);
        Assert.Empty(vm.AvailableYears);
    }

    [Fact]
    public async Task Gate_5_VerificationFailure_KeepsActivityHidden_AndShowsError()
    {
        var leetService = new MockVerificationLeetCodeService
        {
            AboutMe = "Profile without token"
        };
        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_gate5.json"));
        var cacheService = new CacheService(Path.Combine(_tempDir, "cache_gate5.json"));
        var verifService = new AccountVerificationService(leetService);

        var vm = new MainViewModel(leetService, configService, cacheService, null, verifService)
        {
            Username = "fail_user"
        };
        await vm.InitializeAsync();

        vm.StartVerificationCommand.Execute(null);
        Assert.True(vm.IsVerificationPromptOpen);

        // User clicks check verification, but token is missing
        vm.CheckVerificationCommand.Execute(null);

        // Wait brief moment for async command
        for (int i = 0; i < 20 && vm.IsVerifying; i++)
        {
            await Task.Delay(50);
        }

        Assert.False(vm.IsAccountVerified);
        Assert.Equal(WidgetState.VerificationRequired, vm.State);
        Assert.Contains("not found", vm.VerificationStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Null(vm.CurrentActivity);
        Assert.Equal(0, vm.TotalSolved);
    }

    [Fact]
    public async Task Gate_6_SuccessfulVerification_TransitionsToReady_AndExposesWidgetData()
    {
        var leetService = new MockVerificationLeetCodeService();
        var date = new DateOnly(2026, 5, 20);
        leetService.UserStatsMap["alice"] = new LeetCodeUserStats
        {
            Username = "alice",
            TotalSolved = 85,
            EasySolved = 50,
            MediumSolved = 30,
            HardSolved = 5,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { date, 4 } },
            LastSyncedAt = DateTime.UtcNow,
            HasCalendarData = true
        };

        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_gate6.json"));
        var cacheService = new CacheService(Path.Combine(_tempDir, "cache_gate6.json"));
        var verifService = new AccountVerificationService(leetService);

        var vm = new MainViewModel(leetService, configService, cacheService, null, verifService)
        {
            Username = "alice",
            SelectedYear = 2026,
            ReferenceDate = date
        };
        await vm.InitializeAsync();

        vm.StartVerificationCommand.Execute(null);
        leetService.AboutMe = $"About me with token {vm.VerificationToken}";

        vm.CheckVerificationCommand.Execute(null);

        for (int i = 0; i < 30 && (!vm.IsAccountVerified || vm.CurrentActivity == null); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(vm.IsAccountVerified);
        Assert.Equal("alice", vm.VerifiedUsername);
        Assert.Equal(WidgetState.Ready, vm.State);
        Assert.NotNull(vm.CurrentActivity);
        Assert.Equal(85, vm.TotalSolved);
        Assert.Equal(4, vm.YearSubmissions);
        Assert.Equal(1, vm.YearActiveDays);
    }

    [Fact]
    public async Task Gate_7_ExistingValidVerification_SkipsOnboarding()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_gate7.json"));
        var cacheService = new CacheService(Path.Combine(_tempDir, "cache_gate7.json"));

        var stats = new LeetCodeUserStats
        {
            Username = "existing_user",
            TotalSolved = 120,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { new DateOnly(2026, 3, 1), 2 } },
            LastSyncedAt = DateTime.UtcNow
        };
        await cacheService.SaveCacheAsync(stats);

        await configService.SaveConfigurationAsync(new WidgetConfig
        {
            Username = "existing_user",
            VerifiedUsername = "existing_user",
            VerifiedAt = DateTime.UtcNow,
            SelectedYear = 2026
        });

        var vm = new MainViewModel(null, configService, cacheService, null, null);
        await vm.InitializeAsync();

        Assert.True(vm.IsAccountVerified);
        Assert.True(vm.IsReady || vm.IsVerifiedSyncing);
        Assert.False(vm.IsUnconfigured);
        Assert.False(vm.IsVerificationRequired);
        Assert.NotNull(vm.CurrentActivity);
        Assert.Equal(120, vm.TotalSolved);
    }

    #endregion

    #region 2. Hard Account Isolation Tests (8 - 14)

    [Fact]
    public async Task Isolation_8_SwitchingAccount_ImmediatelyClearsOldAccountActivity()
    {
        var vm = new MainViewModel();
        vm.SetVerifiedForTesting("account_a", null, new LeetCodeUserStats
        {
            Username = "account_a",
            TotalSolved = 100,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { new DateOnly(2026, 4, 1), 5 } },
            LastSyncedAt = DateTime.UtcNow
        });

        Assert.True(vm.IsAccountVerified);
        Assert.NotNull(vm.CurrentActivity);
        Assert.Equal(100, vm.TotalSolved);

        // User starts editing username and enters Account B
        vm.ToggleEditUsernameCommand.Execute(null);
        vm.Username = "account_b";
        await vm.SaveUsernameAndRefreshAsync();

        // Invariant: Account A's activity, stats, and verification must be immediately wiped out
        Assert.False(vm.IsAccountVerified);
        Assert.Equal("", vm.VerifiedUsername);
        Assert.Null(vm.CurrentActivity);
        Assert.Equal(0, vm.TotalSolved);
        Assert.Equal(0, vm.YearSubmissions);
        Assert.Empty(vm.AvailableYears);
        Assert.True(vm.State == WidgetState.VerificationInProgress || vm.State == WidgetState.VerificationRequired);
        Assert.True(vm.IsVerificationPromptOpen);

        // Cancelling prompt returns to clean VerificationRequired onboarding state
        vm.CancelVerificationCommand.Execute(null);
        Assert.Equal(WidgetState.VerificationRequired, vm.State);
        Assert.False(vm.IsVerificationPromptOpen);
    }

    [Fact]
    public async Task Isolation_9_AccountB_CannotDisplayAccountA_Statistics()
    {
        var vm = new MainViewModel();
        vm.SetVerifiedForTesting("account_a", null, new LeetCodeUserStats
        {
            Username = "account_a",
            TotalSolved = 250,
            EasySolved = 100,
            MediumSolved = 100,
            HardSolved = 50,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { new DateOnly(2026, 1, 1), 2 } },
            LastSyncedAt = DateTime.UtcNow
        });

        vm.ToggleEditUsernameCommand.Execute(null);
        vm.Username = "account_b";
        await vm.SaveUsernameAndRefreshAsync();

        Assert.Equal(0, vm.TotalSolved);
        Assert.Equal(0, vm.EasySolved);
        Assert.Equal(0, vm.MediumSolved);
        Assert.Equal(0, vm.HardSolved);
        Assert.False(vm.HasSolvedStats);
    }

    [Fact]
    public async Task Isolation_10_AccountB_CannotDisplayAccountA_Heatmap()
    {
        var vm = new MainViewModel();
        vm.SetVerifiedForTesting("account_a", null, new LeetCodeUserStats
        {
            Username = "account_a",
            TotalSolved = 50,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { new DateOnly(2026, 2, 10), 10 } },
            LastSyncedAt = DateTime.UtcNow
        });

        vm.ToggleEditUsernameCommand.Execute(null);
        vm.Username = "account_b";
        await vm.SaveUsernameAndRefreshAsync();

        Assert.Null(vm.CurrentActivity);
        Assert.False(vm.CanDisplayActivity);
    }

    [Fact]
    public async Task Isolation_11_AccountB_CannotDisplayAccountA_Streak()
    {
        var refDate = new DateOnly(2026, 3, 10);
        var vm = new MainViewModel { ReferenceDate = refDate };
        vm.SetVerifiedForTesting("account_a", null, new LeetCodeUserStats
        {
            Username = "account_a",
            TotalSolved = 50,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int>
            {
                { refDate, 1 },
                { refDate.AddDays(-1), 1 },
                { refDate.AddDays(-2), 1 }
            },
            LastSyncedAt = DateTime.UtcNow
        });

        Assert.Equal(3, vm.CurrentStreak);
        Assert.Equal(3, vm.LongestStreak);

        vm.ToggleEditUsernameCommand.Execute(null);
        vm.Username = "account_b";
        await vm.SaveUsernameAndRefreshAsync();

        Assert.Equal(0, vm.CurrentStreak);
        Assert.Equal(0, vm.LongestStreak);
    }

    [Fact]
    public async Task Isolation_12_ChangingUsername_InvalidatesVerification()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_iso12.json"));
        await configService.SaveConfigurationAsync(new WidgetConfig
        {
            Username = "verified_a",
            VerifiedUsername = "verified_a",
            VerifiedAt = DateTime.UtcNow
        });

        var vm = new MainViewModel(null, configService, null, null, null);
        await vm.InitializeAsync();
        Assert.True(vm.IsAccountVerified);

        vm.ToggleEditUsernameCommand.Execute(null);
        vm.Username = "unverified_b";
        await vm.SaveUsernameAndRefreshAsync();

        Assert.False(vm.IsAccountVerified);
        Assert.Equal("", vm.VerifiedUsername);
        Assert.Null(vm.VerifiedAt);

        var reloaded = await configService.LoadConfigurationAsync();
        Assert.False(reloaded.IsAccountVerified);
        Assert.Equal("", reloaded.VerifiedUsername);
    }

    [Fact]
    public async Task Isolation_13_FailedSyncForAccountB_CannotRestoreAccountA_Data()
    {
        var leetService = new MockVerificationLeetCodeService
        {
            ReturnFailure = true,
            ErrorMessage = "Network disconnected"
        };
        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_iso13.json"));
        var cacheService = new CacheService(Path.Combine(_tempDir, "cache_iso13.json"));

        var vm = new MainViewModel(leetService, configService, cacheService, null, null);
        vm.SetVerifiedForTesting("account_a", null, new LeetCodeUserStats
        {
            Username = "account_a",
            TotalSolved = 150,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { new DateOnly(2026, 5, 1), 3 } },
            LastSyncedAt = DateTime.UtcNow
        });

        // Switch to account B and mark verified
        vm.SetVerifiedForTesting("account_b");

        // Attempt sync for account_b which fails
        await vm.PerformSyncAsync(CancellationToken.None);

        // Account B must NOT fall back to Account A's stats or activity!
        Assert.Equal(0, vm.TotalSolved);
        Assert.Equal(0, vm.YearSubmissions);
        Assert.Null(vm.CurrentActivity);
        Assert.Empty(vm.AvailableYears);
    }

    [Fact]
    public void Isolation_14_LastSuccessfulStats_RejectedWhenUsernameDiffersFromVerified()
    {
        var vm = new MainViewModel();
        // Setup Account A as verified
        vm.SetVerifiedForTesting("account_a", null, new LeetCodeUserStats
        {
            Username = "account_a",
            TotalSolved = 200,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { new DateOnly(2026, 1, 1), 1 } },
            LastSyncedAt = DateTime.UtcNow
        });

        Assert.True(vm.HasValidActivityForCurrentAccount);
        Assert.Equal(200, vm.TotalSolved);

        // Change username without verifying
        vm.Username = "account_b";

        // Invariant: DisplayedActivityUsername == VerifiedUsername == CurrentUsername
        Assert.False(vm.HasValidActivityForCurrentAccount);
        Assert.False(vm.CanDisplayActivity);
        Assert.Equal(0, vm.TotalSolved);
        Assert.Null(vm.CurrentActivity);
    }

    #endregion

    #region 3. Cache Isolation Tests (15 - 17)

    [Fact]
    public async Task Cache_15_SameAccountCache_RestoresSuccessfully()
    {
        var cachePath = Path.Combine(_tempDir, "cache_same.json");
        var cacheService = new CacheService(cachePath);

        await cacheService.SaveCacheAsync(new LeetCodeUserStats
        {
            Username = "charlie",
            TotalSolved = 99,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { new DateOnly(2026, 2, 2), 4 } },
            LastSyncedAt = DateTime.UtcNow
        });

        var loaded = await cacheService.LoadCacheAsync("charlie");
        Assert.NotNull(loaded);
        Assert.Equal("charlie", loaded.Username);
        Assert.Equal(99, loaded.TotalSolved);
    }

    [Fact]
    public async Task Cache_16_DifferentAccountCache_CannotRestore()
    {
        var cachePath = Path.Combine(_tempDir, "cache_diff.json");
        var cacheService = new CacheService(cachePath);

        await cacheService.SaveCacheAsync(new LeetCodeUserStats
        {
            Username = "user_a",
            TotalSolved = 99,
            ActiveYears = new List<int> { 2026 },
            LastSyncedAt = DateTime.UtcNow
        });

        // Requesting cache for user_b MUST return null
        var loaded = await cacheService.LoadCacheAsync("user_b");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task Cache_17_CachedActivityIdentity_ValidatedBeforeDisplay()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_gate17.json"));
        var cacheService = new CacheService(Path.Combine(_tempDir, "cache_gate17.json"));

        // Save cache for user_a
        await cacheService.SaveCacheAsync(new LeetCodeUserStats
        {
            Username = "user_a",
            TotalSolved = 77,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { new DateOnly(2026, 3, 3), 3 } },
            LastSyncedAt = DateTime.UtcNow
        });

        // Config is verified for user_b
        await configService.SaveConfigurationAsync(new WidgetConfig
        {
            Username = "user_b",
            VerifiedUsername = "user_b",
            VerifiedAt = DateTime.UtcNow,
            SelectedYear = 2026
        });

        var vm = new MainViewModel(null, configService, cacheService, null, null);
        await vm.InitializeAsync();

        // Cache must not be displayed for user_b!
        Assert.Equal(0, vm.TotalSolved);
        Assert.Null(vm.CurrentActivity);
        Assert.Empty(vm.AvailableYears);
    }

    #endregion

    #region 4. Dynamic Years Tests (18 - 24)

    [Fact]
    public void Years_18_AvailableYears_DerivedStrictlyFromActiveYears()
    {
        var vm = new MainViewModel();
        vm.PopulateActiveYears(new List<int> { 2026 });

        Assert.Single(vm.AvailableYears);
        Assert.Equal(2026, vm.AvailableYears[0]);
    }

    [Fact]
    public void Years_19_NoHardcodedDefaultRangeExists()
    {
        var vm = new MainViewModel();
        // Fresh MainViewModel should have empty AvailableYears, not 2013-now
        Assert.Empty(vm.AvailableYears);
        Assert.False(vm.HasAvailableYears);
    }

    [Fact]
    public void Years_20_ActiveYears_SortedDescending_NewestFirst()
    {
        var vm = new MainViewModel();
        vm.PopulateActiveYears(new List<int> { 2023, 2026, 2024, 2025 });

        Assert.Equal(new[] { 2026, 2025, 2024, 2023 }, vm.AvailableYears);
    }

    [Fact]
    public void Years_21_MissingYears_NotInvented()
    {
        var vm = new MainViewModel();
        // Gaps between 2023 and 2026
        vm.PopulateActiveYears(new List<int> { 2023, 2026 });

        Assert.Equal(2, vm.AvailableYears.Count);
        Assert.Equal(2026, vm.AvailableYears[0]);
        Assert.Equal(2023, vm.AvailableYears[1]);
        Assert.DoesNotContain(2024, vm.AvailableYears);
        Assert.DoesNotContain(2025, vm.AvailableYears);
    }

    [Fact]
    public void Years_22_EmptyActiveYears_HandledSafely()
    {
        var vm = new MainViewModel();
        vm.PopulateActiveYears(new List<int>());

        Assert.Empty(vm.AvailableYears);
        Assert.False(vm.HasAvailableYears);

        vm.PopulateActiveYears(null);
        Assert.Empty(vm.AvailableYears);
        Assert.False(vm.HasAvailableYears);
    }

    [Fact]
    public void Years_23_SelectedYear_ClampedToAvailableYears()
    {
        var vm = new MainViewModel();
        vm.SelectedYear = 2024;

        // Account only has 2026 active
        vm.PopulateActiveYears(new List<int> { 2026 });

        // SelectedYear must automatically default to newest active year (2026)
        Assert.Equal(2026, vm.SelectedYear);
    }

    [Fact]
    public async Task Years_24_HistoricalYearFetching_PreservesAllYearsInCumulativeCache()
    {
        var leetService = new MockVerificationLeetCodeService();
        var date2026 = new DateOnly(2026, 6, 1);
        var date2024 = new DateOnly(2024, 8, 1);

        leetService.UserStatsMap["multi_year_user"] = new LeetCodeUserStats
        {
            Username = "multi_year_user",
            TotalSolved = 150,
            ActiveYears = new List<int> { 2026, 2024 },
            SubmissionCounts = new Dictionary<DateOnly, int>
            {
                { date2026, 3 },
                { date2024, 5 }
            },
            LastSyncedAt = DateTime.UtcNow,
            HasCalendarData = true
        };

        var configService = new ConfigurationService(Path.Combine(_tempDir, "config_y24.json"));
        var cacheService = new CacheService(Path.Combine(_tempDir, "cache_y24.json"));

        var vm = new MainViewModel(leetService, configService, cacheService, null, null);
        vm.SetVerifiedForTesting("multi_year_user");
        vm.SelectedYear = 2026;

        await vm.PerformSyncAsync(CancellationToken.None);

        Assert.Equal(3, vm.YearSubmissions);
        Assert.Equal(new[] { 2026, 2024 }, vm.AvailableYears);

        // Switch to historical 2024
        vm.SelectedYear = 2024;
        vm.UpdateActivityForSelectedYear();

        Assert.Equal(5, vm.YearSubmissions);
        Assert.NotNull(vm.CurrentActivity);
        Assert.Equal(2024, vm.CurrentActivity.Year);
    }

    #endregion

    #region 5. UI Visibility Mapping Tests

    [Fact]
    public void Converter_BooleanToVisibility_InvertTrueAndFalse()
    {
        var direct = new Helpers.BooleanToVisibilityConverter { Invert = false };
        var inverse = new Helpers.BooleanToVisibilityConverter { Invert = true };

        Assert.Equal(System.Windows.Visibility.Visible, direct.Convert(true, typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(System.Windows.Visibility.Collapsed, direct.Convert(false, typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(System.Windows.Visibility.Collapsed, inverse.Convert(true, typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(System.Windows.Visibility.Visible, inverse.Convert(false, typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Converter_StringNotEmptyToVisibility()
    {
        var converter = new Helpers.StringNotEmptyToVisibilityConverter();
        Assert.Equal(System.Windows.Visibility.Visible, converter.Convert("Error message", typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(System.Windows.Visibility.Collapsed, converter.Convert("", typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(System.Windows.Visibility.Collapsed, converter.Convert(null!, typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(System.Windows.Visibility.Collapsed, converter.Convert("   ", typeof(System.Windows.Visibility), null!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void MainWindow_InstantiatesWithMainViewModel_WithoutThrowing()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel();
                var window = new MainWindow(vm);
                Assert.Same(vm, window.DataContext);
                window.Close();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(5000);

        if (threadEx != null) throw threadEx;
    }

    #endregion
}
