using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

public class SynchronizationRegressionTests : IDisposable
{
    private readonly string _tempDirectory;

    public SynchronizationRegressionTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "LeetCodeSyncTests_" + Guid.NewGuid().ToString("N"));
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

    private const string ValidCalendarAndStatsResponse = @"
{
  ""data"": {
    ""matchedUser"": {
      ""username"": ""manav_chaudhary01"",
      ""userCalendar"": {
        ""activeYears"": [2026],
        ""streak"": 55,
        ""totalActiveDays"": 59,
        ""submissionCalendar"": ""{\""1780444800\"": 2, \""1780531200\"": 1}""
      },
      ""submitStatsGlobal"": {
        ""acSubmissionNum"": [
          { ""difficulty"": ""All"", ""count"": 74, ""submissions"": 120 },
          { ""difficulty"": ""Easy"", ""count"": 46, ""submissions"": 60 },
          { ""difficulty"": ""Medium"", ""count"": 26, ""submissions"": 50 },
          { ""difficulty"": ""Hard"", ""count"": 2, ""submissions"": 10 }
        ]
      }
    }
  }
}";

    private const string SolvedStatsSuccessWithNullCalendarResponse = @"
{
  ""data"": {
    ""matchedUser"": {
      ""username"": ""manav_chaudhary01"",
      ""userCalendar"": null,
      ""submitStatsGlobal"": {
        ""acSubmissionNum"": [
          { ""difficulty"": ""All"", ""count"": 74, ""submissions"": 120 },
          { ""difficulty"": ""Easy"", ""count"": 46, ""submissions"": 60 },
          { ""difficulty"": ""Medium"", ""count"": 26, ""submissions"": 50 },
          { ""difficulty"": ""Hard"", ""count"": 2, ""submissions"": 10 }
        ]
      }
    }
  },
  ""errors"": [
    { ""message"": ""userCalendar service temporarily unavailable"" }
  ]
}";

    private const string MalformedCalendarJsonInPayloadResponse = @"
{
  ""data"": {
    ""matchedUser"": {
      ""username"": ""manav_chaudhary01"",
      ""userCalendar"": {
        ""activeYears"": [2026],
        ""streak"": 10,
        ""totalActiveDays"": 15,
        ""submissionCalendar"": ""{ INVALID_JSON 12345 }""
      },
      ""submitStatsGlobal"": {
        ""acSubmissionNum"": [
          { ""difficulty"": ""All"", ""count"": 74, ""submissions"": 120 },
          { ""difficulty"": ""Easy"", ""count"": 46, ""submissions"": 60 },
          { ""difficulty"": ""Medium"", ""count"": 26, ""submissions"": 50 },
          { ""difficulty"": ""Hard"", ""count"": 2, ""submissions"": 10 }
        ]
      }
    }
  }
}";

    private const string EmptyCalendarResponse = @"
{
  ""data"": {
    ""matchedUser"": {
      ""username"": ""manav_chaudhary01"",
      ""userCalendar"": {
        ""activeYears"": [2026],
        ""streak"": 0,
        ""totalActiveDays"": 0,
        ""submissionCalendar"": ""{}""
      },
      ""submitStatsGlobal"": {
        ""acSubmissionNum"": [
          { ""difficulty"": ""All"", ""count"": 74, ""submissions"": 120 },
          { ""difficulty"": ""Easy"", ""count"": 46, ""submissions"": 60 },
          { ""difficulty"": ""Medium"", ""count"": 26, ""submissions"": 50 },
          { ""difficulty"": ""Hard"", ""count"": 2, ""submissions"": 10 }
        ]
      }
    }
  }
}";

    private const string GraphQLErrorResponse = @"
{
  ""data"": null,
  ""errors"": [
    { ""message"": ""Internal server error in LeetCode GraphQL service"" }
  ]
}";

    #region 1. LeetCodeService Tests

    [Fact]
    public async Task GetUserActivityAsync_SuccessfulSolvedAndCalendar_ParsesBothIndependently()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidCalendarAndStatsResponse, Encoding.UTF8, "application/json")
        });
        var service = new LeetCodeService(new HttpClient(handler));

        var result = await service.GetUserActivityAsync("manav_chaudhary01", 2026);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("manav_chaudhary01", result.Data.Username);
        Assert.Equal(74, result.Data.TotalSolved);
        Assert.Equal(46, result.Data.EasySolved);
        Assert.Equal(26, result.Data.MediumSolved);
        Assert.Equal(2, result.Data.HardSolved);
        Assert.True(result.Data.HasCalendarData);
        Assert.Null(result.Data.CalendarErrorMessage);
        Assert.Equal(2, result.Data.SubmissionCounts.Count);
        Assert.Equal(55, result.Data.CurrentStreak);
        Assert.Equal(59, result.Data.TotalActiveDays);
    }

    [Fact]
    public async Task GetUserActivityAsync_SolvedStatsSucceed_WhenCalendarFails_PreservesSolvedStats()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SolvedStatsSuccessWithNullCalendarResponse, Encoding.UTF8, "application/json")
        });
        var service = new LeetCodeService(new HttpClient(handler));

        var result = await service.GetUserActivityAsync("manav_chaudhary01", 2026);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(74, result.Data.TotalSolved);
        Assert.Equal(46, result.Data.EasySolved);
        Assert.Equal(26, result.Data.MediumSolved);
        Assert.Equal(2, result.Data.HardSolved);
        Assert.False(result.Data.HasCalendarData);
        Assert.Contains("userCalendar service temporarily unavailable", result.Data.CalendarErrorMessage);
    }

    [Fact]
    public async Task GetUserActivityAsync_MalformedCalendarPayload_PreservesSolvedStatsAndReportsCalendarError()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MalformedCalendarJsonInPayloadResponse, Encoding.UTF8, "application/json")
        });
        var service = new LeetCodeService(new HttpClient(handler));

        var result = await service.GetUserActivityAsync("manav_chaudhary01", 2026);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(74, result.Data.TotalSolved);
        Assert.False(result.Data.HasCalendarData);
        Assert.Contains("Malformed submission calendar JSON", result.Data.CalendarErrorMessage);
    }

    [Fact]
    public async Task GetUserActivityAsync_EmptyCalendar_ReturnsHasCalendarTrueWithEmptyCounts()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(EmptyCalendarResponse, Encoding.UTF8, "application/json")
        });
        var service = new LeetCodeService(new HttpClient(handler));

        var result = await service.GetUserActivityAsync("manav_chaudhary01", 2025);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(74, result.Data.TotalSolved);
        Assert.True(result.Data.HasCalendarData);
        Assert.Empty(result.Data.SubmissionCounts);
        Assert.Equal(0, result.Data.TotalActiveDays);
    }

    [Fact]
    public async Task GetUserActivityAsync_HttpFailure_ReturnsNetworkError()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            ReasonPhrase = "Server Error"
        });
        var service = new LeetCodeService(new HttpClient(handler));

        var result = await service.GetUserActivityAsync("manav_chaudhary01", 2026);

        Assert.False(result.IsSuccess);
        Assert.Equal(LeetCodeErrorCode.NetworkError, result.ErrorCode);
        Assert.Contains("HTTP 500", result.ErrorMessage);
    }

    [Fact]
    public async Task GetUserActivityAsync_GraphQLErrors_ReturnsUserNotFoundOrErrorMessage()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(GraphQLErrorResponse, Encoding.UTF8, "application/json")
        });
        var service = new LeetCodeService(new HttpClient(handler));

        var result = await service.GetUserActivityAsync("manav_chaudhary01", 2026);

        Assert.False(result.IsSuccess);
        Assert.Contains("Internal server error", result.ErrorMessage);
    }

    [Fact]
    public async Task GetUserActivityAsync_RequestTimeout_ReturnsNetworkError()
    {
        var handler = new MockHttpMessageHandler(_ => throw new TaskCanceledException("Timeout"));
        var service = new LeetCodeService(new HttpClient(handler));

        var result = await service.GetUserActivityAsync("manav_chaudhary01", 2026);

        Assert.False(result.IsSuccess);
        Assert.Equal(LeetCodeErrorCode.NetworkError, result.ErrorCode);
        Assert.Contains("timed out", result.ErrorMessage);
    }

    #endregion

    #region 2. MainViewModel & State Resilience Tests

    [Fact]
    public async Task MainViewModel_UiLoadingStateReset_GuaranteedEvenOnException()
    {
        var handler = new MockHttpMessageHandler(_ => throw new InvalidOperationException("Fatal internal error"));
        var leetService = new LeetCodeService(new HttpClient(handler));
        var configService = new ConfigurationService(Path.Combine(_tempDirectory, "config_test.json"));
        var cacheService = new CacheService(Path.Combine(_tempDirectory, "cache_test.json"));

        await configService.SaveConfigurationAsync(new WidgetConfig { Username = "test_user" });

        var vm = new MainViewModel(leetService, configService, cacheService);
        await vm.InitializeAsync();

        // PerformSyncAsync must handle the exception, reset IsSyncing to false, and not leave status stuck
        await vm.PerformSyncAsync(CancellationToken.None);

        Assert.False(vm.IsSyncing);
        Assert.NotEqual("Syncing with LeetCode...", vm.SyncStatusText);
        Assert.Contains("Fatal internal error", vm.SyncStatusText);
    }

    [Fact]
    public async Task MainViewModel_Cancellation_ResetsLoadingStateAndUpdatesStatus()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Thread.Sleep(50);
            throw new OperationCanceledException();
        });
        var leetService = new LeetCodeService(new HttpClient(handler));
        var configService = new ConfigurationService(Path.Combine(_tempDirectory, "config_cancel.json"));
        var cacheService = new CacheService(Path.Combine(_tempDirectory, "cache_cancel.json"));

        await configService.SaveConfigurationAsync(new WidgetConfig { Username = "test_user" });

        var vm = new MainViewModel(leetService, configService, cacheService);
        await vm.InitializeAsync();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await vm.PerformSyncAsync(cts.Token);

        Assert.False(vm.IsSyncing);
        Assert.NotEqual("Syncing with LeetCode...", vm.SyncStatusText);
    }

    [Fact]
    public async Task MainViewModel_CachedCalendarPreservation_WhenNetworkRefreshFails()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDirectory, "config_pres.json"));
        var cacheService = new CacheService(Path.Combine(_tempDirectory, "cache_pres.json"));

        var initialDate = new DateOnly(2026, 6, 15);
        await cacheService.SaveCacheAsync(new LeetCodeUserStats
        {
            Username = "manav_chaudhary01",
            TotalSolved = 74,
            EasySolved = 46,
            MediumSolved = 26,
            HardSolved = 2,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { initialDate, 5 } },
            LastSyncedAt = DateTime.Now.AddHours(-1)
        });

        // Network call fails with 503 Service Unavailable
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var leetService = new LeetCodeService(new HttpClient(handler));

        var vm = new MainViewModel(leetService, configService, cacheService);
        vm.Username = "manav_chaudhary01";
        vm.SelectedYear = 2026;

        await vm.InitializeAsync();

        // Trigger sync which will fail
        await vm.PerformSyncAsync(CancellationToken.None);

        // Solved statistics and cached submissions must be strictly preserved
        Assert.Equal(74, vm.TotalSolved);
        Assert.Equal(46, vm.EasySolved);
        Assert.Equal(26, vm.MediumSolved);
        Assert.Equal(2, vm.HardSolved);
        Assert.Equal(5, vm.YearSubmissions);
        Assert.Equal(1, vm.YearActiveDays);
        Assert.False(vm.IsSyncing);
        Assert.Contains("Offline", vm.SyncStatusText);
    }

    [Fact]
    public async Task MainViewModel_IndependentHandling_WhenCalendarQueryFails_PreservesSolvedStatsAndCache()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDirectory, "config_indep.json"));
        var cacheService = new CacheService(Path.Combine(_tempDirectory, "cache_indep.json"));

        var initialDate = new DateOnly(2026, 6, 15);
        await cacheService.SaveCacheAsync(new LeetCodeUserStats
        {
            Username = "manav_chaudhary01",
            TotalSolved = 70,
            EasySolved = 44,
            MediumSolved = 24,
            HardSolved = 2,
            ActiveYears = new List<int> { 2026 },
            SubmissionCounts = new Dictionary<DateOnly, int> { { initialDate, 3 } },
            LastSyncedAt = DateTime.Now.AddHours(-2)
        });

        // Response has fresh solved counts (74) but null calendar
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SolvedStatsSuccessWithNullCalendarResponse, Encoding.UTF8, "application/json")
        });
        var leetService = new LeetCodeService(new HttpClient(handler));

        var vm = new MainViewModel(leetService, configService, cacheService);
        vm.Username = "manav_chaudhary01";
        vm.SelectedYear = 2026;
        await vm.InitializeAsync();

        await vm.PerformSyncAsync(CancellationToken.None);

        // All-time solved stats update to 74
        Assert.Equal(74, vm.TotalSolved);
        Assert.Equal(46, vm.EasySolved);
        // Previous calendar data preserved
        Assert.Equal(3, vm.YearSubmissions);
        Assert.Equal(1, vm.YearActiveDays);
        Assert.False(vm.IsSyncing);
        Assert.Contains("Calendar", vm.SyncStatusText);
    }

    [Fact]
    public async Task MainViewModel_MultiYearSubmissions_MergedCumulativeCache()
    {
        var configService = new ConfigurationService(Path.Combine(_tempDirectory, "config_multi.json"));
        var cacheService = new CacheService(Path.Combine(_tempDirectory, "cache_multi.json"));

        // First response: year 2026 has activity
        var response2026 = ValidCalendarAndStatsResponse;
        // Second response: year 2025 has 0 activity
        var response2025 = EmptyCalendarResponse;

        int requestCount = 0;
        var handler = new MockHttpMessageHandler(req =>
        {
            Interlocked.Increment(ref requestCount);
            string content = (requestCount == 1) ? response2026 : response2025;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        });
        var leetService = new LeetCodeService(new HttpClient(handler));

        var vm = new MainViewModel(leetService, configService, cacheService);
        vm.Username = "manav_chaudhary01";
        vm.SelectedYear = 2026;

        // Fetch 2026
        await vm.PerformSyncAsync(CancellationToken.None);
        Assert.Equal(3, vm.YearSubmissions);
        Assert.Equal(2, vm.YearActiveDays);

        // Switch to 2025
        vm.SelectedYear = 2025;
        await vm.PerformSyncAsync(CancellationToken.None);
        Assert.Equal(0, vm.YearSubmissions);
        Assert.Equal(0, vm.YearActiveDays);

        // Switch back to 2026: submissions for 2026 were preserved in cumulative cache!
        vm.SelectedYear = 2026;
        vm.UpdateActivityForSelectedYear();
        Assert.Equal(3, vm.YearSubmissions);
        Assert.Equal(2, vm.YearActiveDays);
    }

    [Fact]
    public async Task MainViewModel_HeatmapControl_ReceivesActivityAndInvalidates()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ValidCalendarAndStatsResponse, Encoding.UTF8, "application/json")
        });
        var leetService = new LeetCodeService(new HttpClient(handler));
        var configService = new ConfigurationService(Path.Combine(_tempDirectory, "config_hm.json"));
        var cacheService = new CacheService(Path.Combine(_tempDirectory, "cache_hm.json"));

        var vm = new MainViewModel(leetService, configService, cacheService);
        vm.Username = "manav_chaudhary01";
        vm.SelectedYear = 2026;

        await vm.PerformSyncAsync(CancellationToken.None);

        Assert.NotNull(vm.CurrentActivity);
        Assert.Equal(2026, vm.CurrentActivity.Year);
        Assert.Equal(3, vm.CurrentActivity.TotalSubmissions);
        Assert.Equal(2, vm.CurrentActivity.ActiveDays);
    }

    [Fact]
    public async Task RefreshCoordinator_RecoversFromException_AndAllowsSubsequentRefresh()
    {
        using var coordinator = new RefreshCoordinator();
        int attempt = 0;

        coordinator.Start(async ct =>
        {
            attempt++;
            if (attempt == 1)
            {
                throw new InvalidOperationException("Transient crash in refresh");
            }
            await Task.Delay(10, ct);
        });

        // First attempt throws inside callback
        bool res1 = await coordinator.TriggerRefreshAsync(isManual: true);
        Assert.False(res1);
        Assert.False(coordinator.IsRefreshing);

        // Second attempt must succeed without being locked or poisoned
        bool res2 = await coordinator.TriggerRefreshAsync(isManual: true);
        Assert.True(res2);
        Assert.False(coordinator.IsRefreshing);
        Assert.Equal(2, attempt);
    }

    #endregion
}
