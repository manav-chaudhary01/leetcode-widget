using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

public class VerificationIntegrityAndYearSelectionTests : IDisposable
{
    private readonly string _tempDir;

    public VerificationIntegrityAndYearSelectionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LC_VerifyIntegrity_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore best-effort cleanup errors
        }
    }

    private class MockVerificationLeetCodeService : ILeetCodeService
    {
        public string AboutMeResponse { get; set; } = "";
        public bool FailAboutMe { get; set; }
        public LeetCodeUserStats? StatsToReturn { get; set; }

        public Task<LeetCodeResult<string>> GetUserProfileAboutMeAsync(string username, CancellationToken cancellationToken = default)
        {
            if (FailAboutMe)
            {
                return Task.FromResult(LeetCodeResult<string>.Failure("GraphQL query error", LeetCodeErrorCode.NetworkError));
            }
            return Task.FromResult(LeetCodeResult<string>.Success(AboutMeResponse));
        }

        public Task<LeetCodeResult<LeetCodeUserStats>> GetUserActivityAsync(string username, int? year = null, CancellationToken cancellationToken = default)
        {
            var stats = StatsToReturn ?? new LeetCodeUserStats
            {
                Username = username,
                TotalSolved = 150,
                EasySolved = 60,
                MediumSolved = 70,
                HardSolved = 20,
                ActiveYears = new List<int> { 2026, 2025 },
                LastSyncedAt = DateTime.UtcNow
            };
            return Task.FromResult(LeetCodeResult<LeetCodeUserStats>.Success(stats));
        }

        public IReadOnlyDictionary<DateOnly, int> ParseSubmissionCalendar(string? calendarJson) =>
            new Dictionary<DateOnly, int>();
    }

    #region Verification Invariant Tests (1 - 9)

    [Fact]
    public async Task Invariant_01_AccountCoder_IsNotAutomaticallyVerified()
    {
        var configPath = Path.Combine(_tempDir, "config_coder.json");
        var configService = new ConfigurationService(configPath);
        var cacheService = new CacheService(Path.Combine(_tempDir, "cache_coder.json"));
        var leetService = new MockVerificationLeetCodeService();

        var vm = new MainViewModel(leetService, configService, cacheService);
        await vm.InitializeAsync();

        // Initially unconfigured
        Assert.False(vm.IsAccountVerified);
        Assert.Equal("", vm.VerifiedUsername);
        Assert.Null(vm.VerifiedAt);

        // User enters famous public username "coder"
        vm.Username = "coder";
        await vm.SaveUsernameAndRefreshAsync();

        // Must NOT be verified simply by existing or having a public profile
        Assert.False(vm.IsAccountVerified);
        Assert.Equal("", vm.VerifiedUsername);
        Assert.Null(vm.VerifiedAt);
        Assert.True(vm.State == WidgetState.VerificationRequired || vm.State == WidgetState.VerificationInProgress);
        Assert.False(vm.CanDisplayActivity);
        Assert.Equal(0, vm.TotalSolved);
    }

    [Fact]
    public async Task Invariant_02_PublicProfile_WithNoChallengeTokenInAboutMe_RemainsUnverified()
    {
        var leetService = new MockVerificationLeetCodeService
        {
            AboutMeResponse = "Hi, I am a senior software engineer at tech company! Check my github."
        };
        var verificationService = new AccountVerificationService(leetService);
        verificationService.GenerateChallenge("coder");

        var result = await verificationService.VerifyOwnershipAsync("coder");

        Assert.False(result.IsSuccess);
        Assert.Contains("was not found in your LeetCode 'About Me'", result.ErrorMessage);
    }

    [Fact]
    public async Task Invariant_03_IncorrectOrPartialChallengeToken_RemainsUnverified()
    {
        var leetService = new MockVerificationLeetCodeService
        {
            AboutMeResponse = "Trying to verify: lc-verify-wrong-token-12345"
        };
        var verificationService = new AccountVerificationService(leetService);
        verificationService.GenerateChallenge("alice");

        var result = await verificationService.VerifyOwnershipAsync("alice");

        Assert.False(result.IsSuccess);
        Assert.Contains("was not found in your LeetCode 'About Me'", result.ErrorMessage);
    }

    [Fact]
    public async Task Invariant_04_StaleOrExpiredChallenge_CannotVerifyAccount()
    {
        var leetService = new MockVerificationLeetCodeService
        {
            AboutMeResponse = "lc-verify-expired-token-1234567890"
        };
        var verificationService = new AccountVerificationService(leetService);
        verificationService.GenerateChallenge("alice");

        // Simulate challenge expired 5 minutes ago via reflection on _currentChallenge
        var field = typeof(AccountVerificationService).GetField("_currentChallenge", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var expiredChallenge = new VerificationChallenge(
            "alice",
            "lc-verify-expired-token-1234567890",
            DateTime.UtcNow.AddMinutes(-20),
            DateTime.UtcNow.AddMinutes(-5));
        field?.SetValue(verificationService, expiredChallenge);

        var result = await verificationService.VerifyOwnershipAsync("alice");

        Assert.False(result.IsSuccess);
        Assert.Contains("expired", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invariant_05_ChallengeGeneratedForAlice_CannotVerifyBob()
    {
        var leetService = new MockVerificationLeetCodeService
        {
            AboutMeResponse = "lc-verify-alice-token-1234567890"
        };
        var verificationService = new AccountVerificationService(leetService);
        verificationService.GenerateChallenge("alice");

        // Attempting to verify bob using alice's active challenge must fail
        var result = await verificationService.VerifyOwnershipAsync("bob");

        Assert.False(result.IsSuccess);
        Assert.Contains("Challenge was generated for @alice, but verifying @bob", result.ErrorMessage);
    }

    [Fact]
    public async Task Invariant_06_SuccessfulExactMatch_VerifiesOnlyTargetUsername_WithTokenAndTimestamp()
    {
        var leetService = new MockVerificationLeetCodeService();
        var verificationService = new AccountVerificationService(leetService);
        var challenge = verificationService.GenerateChallenge("target_coder");

        // Simulate user placing the challenge token into their About Me
        leetService.AboutMeResponse = $"Software developer. {challenge.Token} thanks!";

        var result = await verificationService.VerifyOwnershipAsync("target_coder");

        Assert.True(result.IsSuccess);
        Assert.Equal("target_coder", result.VerifiedUsername);
        Assert.NotNull(result.VerifiedAtUtc);
        Assert.Equal(challenge.Token, result.VerificationChallengeToken);
        Assert.Null(verificationService.CurrentChallenge); // Consumed on success
    }

    [Fact]
    public async Task Invariant_07_SwitchingAccounts_ClearsVerification_Stats_Streaks_And_SelectedYear_Immediately()
    {
        var configPath = Path.Combine(_tempDir, "config_switch.json");
        var configService = new ConfigurationService(configPath);
        var cacheService = new CacheService(Path.Combine(_tempDir, "cache_switch.json"));
        var leetService = new MockVerificationLeetCodeService();

        var vm = new MainViewModel(leetService, configService, cacheService);
        vm.SetVerifiedForTesting("account_a", DateTime.UtcNow, new LeetCodeUserStats
        {
            Username = "account_a",
            TotalSolved = 500,
            EasySolved = 200,
            MediumSolved = 200,
            HardSolved = 100,
            ActiveYears = new List<int> { 2024, 2023 },
            SubmissionCounts = new Dictionary<DateOnly, int>
            {
                { new DateOnly(2024, 5, 1), 5 }
            },
            LastSyncedAt = DateTime.UtcNow
        });
        vm.SelectedYear = 2024;

        Assert.True(vm.IsAccountVerified);
        Assert.Equal("account_a", vm.VerifiedUsername);
        Assert.Equal(500, vm.TotalSolved);
        Assert.Equal(2024, vm.SelectedYear);

        // User switches to account_b
        vm.ToggleEditUsernameCommand.Execute(null);
        vm.Username = "account_b";
        await vm.SaveUsernameAndRefreshAsync();

        // All data from account_a must be immediately wiped
        Assert.False(vm.IsAccountVerified);
        Assert.Equal("", vm.VerifiedUsername);
        Assert.Null(vm.VerifiedAt);
        Assert.Equal(0, vm.TotalSolved);
        Assert.Equal(0, vm.EasySolved);
        Assert.Equal(0, vm.MediumSolved);
        Assert.Equal(0, vm.HardSolved);
        Assert.Null(vm.CurrentActivity);
        Assert.Empty(vm.AvailableYears);
        Assert.Equal(DateTime.Now.Year, vm.SelectedYear); // Reset to current year, never retaining 2024
        Assert.True(vm.State == WidgetState.VerificationRequired || vm.State == WidgetState.VerificationInProgress);

        // Configuration file on disk must persist account_b as unverified
        var loaded = await configService.LoadConfigurationAsync();
        Assert.Equal("account_b", loaded.Username);
        Assert.False(loaded.IsAccountVerified);
        Assert.Equal("", loaded.VerifiedUsername);
    }

    [Fact]
    public async Task Invariant_08_RestartingWithLegacyOrUntrustedConfig_MigratesToUnverified()
    {
        var configPath = Path.Combine(_tempDir, "legacy_config.json");

        // Write a raw legacy configuration file without VerificationVersion
        var legacyJson = JsonSerializer.Serialize(new
        {
            Username = "untrusted_coder",
            SelectedYear = 2024,
            RefreshIntervalMinutes = 60,
            VerifiedUsername = "untrusted_coder",
            VerifiedAt = DateTime.UtcNow
        });
        await File.WriteAllTextAsync(configPath, legacyJson);

        var configService = new ConfigurationService(configPath);
        var loaded = await configService.LoadConfigurationAsync();

        // Must be migrated to unverified immediately
        Assert.Equal(WidgetConfig.CurrentVerificationVersion, loaded.VerificationVersion);
        Assert.Equal("untrusted_coder", loaded.Username);
        Assert.Equal("", loaded.VerifiedUsername);
        Assert.Null(loaded.VerifiedAt);
        Assert.False(loaded.IsAccountVerified);

        // File on disk must now also be cleanly migrated
        var migratedJson = await File.ReadAllTextAsync(configPath);
        Assert.Contains("\"VerificationVersion\":2", migratedJson.Replace(" ", ""));
        Assert.Contains("\"VerifiedUsername\":\"\"", migratedJson.Replace(" ", ""));
    }

    [Fact]
    public async Task Invariant_09_UnverifiedAccounts_NeverDisplayCachedStatisticsOrHeatmap()
    {
        var configPath = Path.Combine(_tempDir, "config_unverified.json");
        var cachePath = Path.Combine(_tempDir, "cache_unverified.json");
        var configService = new ConfigurationService(configPath);
        var cacheService = new CacheService(cachePath);

        // Cache contains data for "target_user"
        await cacheService.SaveCacheAsync(new LeetCodeUserStats
        {
            Username = "target_user",
            TotalSolved = 800,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int>
            {
                { new DateOnly(2026, 1, 1), 10 }
            },
            LastSyncedAt = DateTime.UtcNow
        });

        // Config has target_user as UNVERIFIED
        await configService.SaveConfigurationAsync(new WidgetConfig
        {
            Username = "target_user",
            VerifiedUsername = "",
            VerifiedAt = null
        });

        var vm = new MainViewModel(new MockVerificationLeetCodeService(), configService, cacheService);
        await vm.InitializeAsync();

        Assert.False(vm.IsAccountVerified);
        Assert.False(vm.CanDisplayActivity);
        Assert.Null(vm.CurrentActivity);
        Assert.Equal(0, vm.TotalSolved);
        Assert.Empty(vm.AvailableYears);
        Assert.Equal(WidgetState.VerificationRequired, vm.State);
    }

    #endregion

    #region Year Selection Invariant Tests (10 - 15)

    [Fact]
    public void YearSelection_10_PrefersCurrentCalendarYear_WhenPresentInActiveYears()
    {
        var vm = new MainViewModel();
        int currentYear = DateTime.Now.Year;
        var activeYears = new List<int> { currentYear, currentYear - 1, currentYear - 2 };

        vm.PopulateActiveYears(activeYears, isAccountSwitchOrVerification: true);

        Assert.Equal(currentYear, vm.SelectedYear);
        Assert.True(vm.HasAvailableYears);
        Assert.Equal(3, vm.AvailableYears.Count);
        Assert.Equal(currentYear, vm.AvailableYears[0]);
    }

    [Fact]
    public void YearSelection_11_SelectsLatestAvailableYear_WhenCurrentYearAbsent()
    {
        var vm = new MainViewModel();
        int currentYear = DateTime.Now.Year;
        // Current year is absent, account was active in 2023, 2022, 2021
        var activeYears = new List<int> { 2021, 2023, 2022 };

        vm.PopulateActiveYears(activeYears, isAccountSwitchOrVerification: true);

        Assert.Equal(2023, vm.SelectedYear);
        Assert.True(vm.HasAvailableYears);
        Assert.Equal(2023, vm.AvailableYears[0]);
        Assert.Equal(2022, vm.AvailableYears[1]);
        Assert.Equal(2021, vm.AvailableYears[2]);
    }

    [Fact]
    public void YearSelection_12_EmptyActiveYears_ProducesHasAvailableYearsFalse_AndEmptyState()
    {
        var vm = new MainViewModel();

        vm.PopulateActiveYears(new List<int>());

        Assert.Empty(vm.AvailableYears);
        Assert.False(vm.HasAvailableYears);

        // Null active years also safely sets HasAvailableYears to false
        vm.PopulateActiveYears(null);
        Assert.Empty(vm.AvailableYears);
        Assert.False(vm.HasAvailableYears);
    }

    [Fact]
    public async Task YearSelection_13_SwitchingAccounts_RecomputesDefaultYear_DoesNotRetainOldYear()
    {
        var configPath = Path.Combine(_tempDir, "config_year_switch.json");
        var configService = new ConfigurationService(configPath);
        var vm = new MainViewModel(null, configService, null);

        // Account A is active in [2022, 2021] and user selected 2022
        vm.SetVerifiedForTesting("account_a", DateTime.UtcNow, new LeetCodeUserStats
        {
            Username = "account_a",
            ActiveYears = new List<int> { 2022, 2021 }
        });
        Assert.Equal(2022, vm.SelectedYear);

        // User switches to Account B
        vm.ToggleEditUsernameCommand.Execute(null);
        vm.Username = "account_b";
        await vm.SaveUsernameAndRefreshAsync();

        // Old year 2022 must NOT be retained
        Assert.Equal(DateTime.Now.Year, vm.SelectedYear);

        // Account B verifies and has active years [2026, 2025]
        vm.SetVerifiedForTesting("account_b", DateTime.UtcNow, new LeetCodeUserStats
        {
            Username = "account_b",
            ActiveYears = new List<int> { 2026, 2025 }
        });
        int expectedForB = DateTime.Now.Year == 2026 ? 2026 : 2025;
        Assert.Equal(expectedForB, vm.SelectedYear);

        // Switch to Account C with active years [2024, 2023]
        vm.ToggleEditUsernameCommand.Execute(null);
        vm.Username = "account_c";
        await vm.SaveUsernameAndRefreshAsync();

        vm.SetVerifiedForTesting("account_c", DateTime.UtcNow, new LeetCodeUserStats
        {
            Username = "account_c",
            ActiveYears = new List<int> { 2024, 2023 }
        });

        // Must select latest available year 2024, NOT Account B's 2026 or Account A's 2022
        Assert.Equal(2024, vm.SelectedYear);
    }

    [Fact]
    public void YearSelection_14_ViewModel_Heatmap_And_Footer_Synchronized()
    {
        var vm = new MainViewModel();
        var stats = new LeetCodeUserStats
        {
            Username = "sync_user",
            ActiveYears = new List<int> { 2026, 2025 },
            SubmissionCounts = new Dictionary<DateOnly, int>
            {
                { new DateOnly(2026, 1, 10), 4 },
                { new DateOnly(2025, 8, 15), 7 }
            }
        };

        vm.SetVerifiedForTesting("sync_user", DateTime.UtcNow, stats);

        // Initially on 2026
        Assert.Equal(2026, vm.SelectedYear);
        Assert.NotNull(vm.CurrentActivity);
        Assert.Equal(2026, vm.CurrentActivity.Year);
        Assert.Equal(2026, vm.CurrentYearStats.Year);
        Assert.Equal(4, vm.YearSubmissions);

        // User switches to 2025 via dropdown selection
        vm.SelectedYear = 2025;

        // ViewModel, CurrentActivity (Heatmap), and CurrentYearStats (Footer) are perfectly synchronized
        Assert.Equal(2025, vm.SelectedYear);
        Assert.NotNull(vm.CurrentActivity);
        Assert.Equal(2025, vm.CurrentActivity.Year);
        Assert.Equal(2025, vm.CurrentYearStats.Year);
        Assert.Equal(7, vm.YearSubmissions);
    }

    [Fact]
    public async Task YearSelection_15_SelectionUpdatesCorrectlyAfterAsyncSynchronization()
    {
        var configPath = Path.Combine(_tempDir, "config_sync_year.json");
        var configService = new ConfigurationService(configPath);
        var leetService = new MockVerificationLeetCodeService
        {
            StatsToReturn = new LeetCodeUserStats
            {
                Username = "live_user",
                TotalSolved = 250,
                ActiveYears = new List<int> { 2026, 2025, 2024 },
                LastSyncedAt = DateTime.UtcNow
            }
        };

        var vm = new MainViewModel(leetService, configService, null);
        vm.SetVerifiedForTesting("live_user");

        await vm.PerformSyncAsync(CancellationToken.None);

        Assert.True(vm.HasAvailableYears);
        Assert.Equal(3, vm.AvailableYears.Count);
        Assert.Equal(2026, vm.SelectedYear);
        Assert.Equal(new List<int> { 2026, 2025, 2024 }, vm.AvailableYears.ToList());
    }

    #endregion
}
