using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

public class AccountVerificationTests
{
    private class FakeLeetCodeService : ILeetCodeService
    {
        public string AboutMeResponse { get; set; } = "";
        public bool ShouldFail { get; set; }
        public string? ErrorMessage { get; set; }

        public Task<LeetCodeResult<string>> GetUserProfileAboutMeAsync(string username, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                return Task.FromResult(LeetCodeResult<string>.Failure(
                    ErrorMessage ?? "Profile fetch failed",
                    LeetCodeErrorCode.NetworkError));
            }

            return Task.FromResult(LeetCodeResult<string>.Success(AboutMeResponse));
        }

        public Task<LeetCodeResult<LeetCodeUserStats>> GetUserActivityAsync(string username, int? year = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LeetCodeResult<LeetCodeUserStats>.Success(new LeetCodeUserStats
            {
                Username = username,
                TotalSolved = 100,
                LastSyncedAt = DateTime.Now
            }));
        }

        public IReadOnlyDictionary<DateOnly, int> ParseSubmissionCalendar(string? calendarJson)
        {
            return new Dictionary<DateOnly, int>();
        }
    }

    [Fact]
    public void GenerateChallenge_CreatesCryptographicallyRandomTokenBoundToUsername()
    {
        var service = new AccountVerificationService(new FakeLeetCodeService());

        var challenge1 = service.GenerateChallenge("alice_coder");
        Assert.NotNull(challenge1);
        Assert.Equal("alice_coder", challenge1.Username);
        Assert.StartsWith("lc-verify-", challenge1.Token);
        Assert.True(challenge1.Token.Length >= 20);
        Assert.False(challenge1.IsExpired);

        // Generate second challenge: tokens must be unique/random
        var challenge2 = service.GenerateChallenge("alice_coder");
        Assert.NotEqual(challenge1.Token, challenge2.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@@@")]
    public void GenerateChallenge_InvalidUsername_ThrowsArgumentException(string invalidUsername)
    {
        var service = new AccountVerificationService(new FakeLeetCodeService());
        Assert.Throws<ArgumentException>(() => service.GenerateChallenge(invalidUsername));
    }

    [Fact]
    public void Challenge_Expiration_CalculatesAccurately()
    {
        var challenge = new VerificationChallenge(
            "test_user",
            "lc-verify-12345",
            DateTime.UtcNow.AddMinutes(-16),
            DateTime.UtcNow.AddMinutes(-1));

        Assert.True(challenge.IsExpired);
        Assert.Equal(TimeSpan.Zero, challenge.RemainingTime);

        var activeChallenge = new VerificationChallenge(
            "test_user",
            "lc-verify-12345",
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(15));

        Assert.False(activeChallenge.IsExpired);
        Assert.True(activeChallenge.RemainingTime > TimeSpan.FromMinutes(14));
    }

    [Fact]
    public async Task VerifyOwnershipAsync_WithoutActiveChallenge_ReturnsFailure()
    {
        var fakeLeetCode = new FakeLeetCodeService();
        var service = new AccountVerificationService(fakeLeetCode);

        var result = await service.VerifyOwnershipAsync("alice");
        Assert.False(result.IsSuccess);
        Assert.Contains("No active verification challenge", result.ErrorMessage);
    }

    [Fact]
    public async Task VerifyOwnershipAsync_WhenTokenNotInAboutMe_ReturnsFailureWithGuidance()
    {
        var fakeLeetCode = new FakeLeetCodeService
        {
            AboutMeResponse = "Just a software developer who loves algorithms."
        };
        var service = new AccountVerificationService(fakeLeetCode);

        var challenge = service.GenerateChallenge("alice");
        Assert.NotNull(service.CurrentChallenge);

        var result = await service.VerifyOwnershipAsync("alice");

        Assert.False(result.IsSuccess);
        Assert.Contains(challenge.Token, result.ErrorMessage);
        Assert.Contains("was not found", result.ErrorMessage);
        // Challenge remains active so user can paste it and retry
        Assert.NotNull(service.CurrentChallenge);
    }

    [Fact]
    public async Task VerifyOwnershipAsync_WhenTokenPresentInAboutMe_ReturnsSuccessAndInvalidatesChallenge()
    {
        var fakeLeetCode = new FakeLeetCodeService();
        var service = new AccountVerificationService(fakeLeetCode);

        var challenge = service.GenerateChallenge("alice");

        // User pastes token into About Me
        fakeLeetCode.AboutMeResponse = $"Hi! My verification token is {challenge.Token} for desktop widget.";

        var result = await service.VerifyOwnershipAsync("alice");

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", result.VerifiedUsername);
        Assert.NotNull(result.VerifiedAtUtc);

        // Challenge must be invalidated upon successful verification
        Assert.Null(service.CurrentChallenge);
    }

    [Fact]
    public async Task VerifyOwnershipAsync_WhenChallengeIsForDifferentUsername_ReturnsFailure()
    {
        var fakeLeetCode = new FakeLeetCodeService();
        var service = new AccountVerificationService(fakeLeetCode);

        service.GenerateChallenge("alice");

        var result = await service.VerifyOwnershipAsync("bob");

        Assert.False(result.IsSuccess);
        Assert.Contains("Challenge was generated for @alice", result.ErrorMessage);
    }

    [Fact]
    public void CancelChallenge_ClearsActiveChallenge()
    {
        var service = new AccountVerificationService(new FakeLeetCodeService());
        service.GenerateChallenge("alice");
        Assert.NotNull(service.CurrentChallenge);

        service.CancelChallenge();
        Assert.Null(service.CurrentChallenge);
    }

    [Fact]
    public async Task MainViewModel_VerificationFlow_SuccessUpdatesVerifiedStateAndPersistsConfig()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            string configPath = Path.Combine(tempDir, "config.json");
            var configService = new ConfigurationService(configPath);
            var fakeLeetCode = new FakeLeetCodeService();
            var verificationService = new AccountVerificationService(fakeLeetCode);

            var vm = new MainViewModel(
                fakeLeetCode,
                configService,
                new CacheService(Path.Combine(tempDir, "cache.json")),
                new RefreshCoordinator(),
                verificationService)
            {
                Username = "alice"
            };

            await vm.InitializeAsync();
            Assert.False(vm.IsAccountVerified);

            // 1. User starts verification
            vm.StartVerificationCommand.Execute(null);
            Assert.True(vm.IsVerificationPromptOpen);
            Assert.StartsWith("lc-verify-", vm.VerificationToken);

            // 2. Put token in About Me
            fakeLeetCode.AboutMeResponse = $"Profile bio containing {vm.VerificationToken}";

            // 3. User clicks Verify Ownership
            vm.CheckVerificationCommand.Execute(null);

            // Wait brief moment for async command
            for (int i = 0; i < 20 && !vm.IsAccountVerified; i++)
            {
                await Task.Delay(50);
            }

            Assert.True(vm.IsAccountVerified);
            Assert.Equal("alice", vm.VerifiedUsername);
            Assert.False(vm.IsVerificationPromptOpen);
            Assert.Contains("verified", vm.SyncStatusText, StringComparison.OrdinalIgnoreCase);

            // 4. Verify persistence in config.json
            var reloadedConfig = await configService.LoadConfigurationAsync();
            Assert.Equal("alice", reloadedConfig.VerifiedUsername);
            Assert.NotNull(reloadedConfig.VerifiedAt);
            Assert.True(reloadedConfig.IsAccountVerified);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MainViewModel_ChangingUsername_InvalidatesVerificationState()
    {
        var vm = new MainViewModel(new FakeLeetCodeService())
        {
            Username = "verified_user"
        };

        // Manually simulate loaded verified state
        var prop = typeof(MainViewModel).GetProperty(nameof(MainViewModel.VerifiedUsername));
        prop?.SetValue(vm, "verified_user");

        Assert.True(vm.IsAccountVerified);

        // User changes username to different account
        vm.Username = "different_user";
        Assert.False(vm.IsAccountVerified, "Changing username must immediately invalidate verification state");

        // Switching back restores verified status
        vm.Username = "verified_user";
        Assert.True(vm.IsAccountVerified);
    }

    [Fact]
    public async Task ConfigurationService_PersistsAndLoads_VerifiedUsernameAndTimestamp()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            string configPath = Path.Combine(tempDir, "verify_config.json");
            var configService = new ConfigurationService(configPath);

            var now = DateTime.UtcNow;
            var config = new WidgetConfig
            {
                Username = "test_user",
                VerifiedUsername = "test_user",
                VerifiedAt = now
            };

            await configService.SaveConfigurationAsync(config);

            var loaded = await configService.LoadConfigurationAsync();
            Assert.Equal("test_user", loaded.Username);
            Assert.Equal("test_user", loaded.VerifiedUsername);
            Assert.True(loaded.IsAccountVerified);
            Assert.NotNull(loaded.VerifiedAt);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void PrivacyAudit_VerificationAndConfig_ContainsNoPasswordsOrCookies()
    {
        var configType = typeof(WidgetConfig);
        var properties = configType.GetProperties();

        foreach (var prop in properties)
        {
            string name = prop.Name.ToLowerInvariant();
            Assert.DoesNotContain("password", name);
            Assert.DoesNotContain("cookie", name);
            Assert.DoesNotContain("session", name);
            Assert.DoesNotContain("auth", name);
            Assert.DoesNotContain("secret", name);
            Assert.DoesNotContain("credential", name);
        }

        var challengeType = typeof(VerificationChallenge);
        foreach (var prop in challengeType.GetProperties())
        {
            string name = prop.Name.ToLowerInvariant();
            Assert.DoesNotContain("password", name);
            Assert.DoesNotContain("cookie", name);
            Assert.DoesNotContain("session", name);
        }
    }

    [Fact]
    public void OpenLeetCodeProfileCommand_IsDefinedAndExecutable()
    {
        using var vm = new MainViewModel(
            new FakeLeetCodeService(),
            new ConfigurationService(),
            new CacheService(),
            new RefreshCoordinator(),
            new AccountVerificationService(new FakeLeetCodeService()));

        Assert.NotNull(vm.OpenLeetCodeProfileCommand);
        Assert.True(vm.OpenLeetCodeProfileCommand.CanExecute(null));
    }
}
