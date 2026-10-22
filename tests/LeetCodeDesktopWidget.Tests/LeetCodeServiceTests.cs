using System;
using System.Collections.Generic;
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

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(_handler(request));
    }
}

public class LeetCodeServiceTests
{
    private const string SampleSuccessResponse = @"
{
  ""data"": {
    ""matchedUser"": {
      ""username"": ""test_coder"",
      ""userCalendar"": {
        ""activeYears"": [2026, 2025, 2024],
        ""streak"": 5,
        ""totalActiveDays"": 42,
        ""submissionCalendar"": ""{\""1773014400\"": 3, \""1773100800\"": 7, \""1773187200\"": 1}""
      },
      ""submitStatsGlobal"": {
        ""acSubmissionNum"": [
          { ""difficulty"": ""All"", ""count"": 350, ""submissions"": 900 },
          { ""difficulty"": ""Easy"", ""count"": 150, ""submissions"": 300 },
          { ""difficulty"": ""Medium"", ""count"": 150, ""submissions"": 450 },
          { ""difficulty"": ""Hard"", ""count"": 50, ""submissions"": 150 }
        ]
      }
    }
  }
}";

    private const string SampleUserNotFoundResponse = @"
{
  ""errors"": [
    {
      ""message"": ""That user does not exist."",
      ""path"": [""matchedUser""]
    }
  ],
  ""data"": {
    ""matchedUser"": null
  }
}";

    [Fact]
    public void ParseSubmissionCalendar_ValidJson_MapsTimestampsToDates()
    {
        var service = new LeetCodeService();
        // 1719100800 = 2024-06-23 UTC
        // 1726963200 = 2024-09-22 UTC
        string json = "{\"1719100800\": 3, \"1726963200\": 17}";

        var result = service.ParseSubmissionCalendar(json);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[new DateOnly(2024, 6, 23)]);
        Assert.Equal(17, result[new DateOnly(2024, 9, 22)]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("invalid json content")]
    public void ParseSubmissionCalendar_EmptyOrInvalid_ReturnsEmptyDictionary(string? input)
    {
        var service = new LeetCodeService();
        var result = service.ParseSubmissionCalendar(input);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserActivityAsync_SuccessfulResponse_ParsesStatsAndCalendar()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleSuccessResponse, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var service = new LeetCodeService(client);

        var result = await service.GetUserActivityAsync("test_coder", 2026);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal("test_coder", result.Data.Username);
        Assert.Equal(350, result.Data.TotalSolved);
        Assert.Equal(150, result.Data.EasySolved);
        Assert.Equal(150, result.Data.MediumSolved);
        Assert.Equal(50, result.Data.HardSolved);
        Assert.Equal(5, result.Data.CurrentStreak);
        Assert.Equal(42, result.Data.TotalActiveDays);
        Assert.Equal(3, result.Data.ActiveYears.Count);
        Assert.Equal(3, result.Data.SubmissionCounts.Count);
    }

    [Fact]
    public async Task GetUserActivityAsync_UserNotFound_ReturnsAppropriateError()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleUserNotFoundResponse, Encoding.UTF8, "application/json")
        });

        var client = new HttpClient(handler);
        var service = new LeetCodeService(client);

        var result = await service.GetUserActivityAsync("non_existent_user");

        Assert.False(result.IsSuccess);
        Assert.Equal(LeetCodeErrorCode.UserNotFound, result.ErrorCode);
        Assert.Contains("does not exist", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUserActivityAsync_RateLimited_ReturnsRateLimitError()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var client = new HttpClient(handler);
        var service = new LeetCodeService(client);

        var result = await service.GetUserActivityAsync("any_user");

        Assert.False(result.IsSuccess);
        Assert.Equal(LeetCodeErrorCode.RateLimited, result.ErrorCode);
    }

    [Fact]
    public async Task GetUserActivityAsync_NetworkFailure_ReturnsNetworkError()
    {
        var handler = new MockHttpMessageHandler(req => throw new HttpRequestException("Connection refused"));

        var client = new HttpClient(handler);
        var service = new LeetCodeService(client);

        var result = await service.GetUserActivityAsync("any_user");

        Assert.False(result.IsSuccess);
        Assert.Equal(LeetCodeErrorCode.NetworkError, result.ErrorCode);
    }

    [Fact]
    public void HeatmapIntegration_LoadedSubmissionCountsRenderAccurateActivity()
    {
        var service = new LeetCodeService();
        string json = "{\"1719100800\": 3, \"1726963200\": 8}";
        var counts = service.ParseSubmissionCalendar(json);

        var activity = YearlyActivity.Create(2024, counts);

        Assert.Equal(366, activity.TotalDays); // 2024 leap year
        Assert.Equal(11, activity.TotalSubmissions); // 3 + 8
        Assert.Equal(2, activity.ActiveDays);

        var dayJune = activity.DayMap[new DateOnly(2024, 6, 23)];
        Assert.Equal(3, dayJune.Count);
        Assert.Equal(ActivityLevel.Medium, dayJune.Level);

        var daySept = activity.DayMap[new DateOnly(2024, 9, 22)];
        Assert.Equal(8, daySept.Count);
        Assert.Equal(ActivityLevel.High, daySept.Level);
    }

    [Fact]
    public async Task MainViewModel_SyncPreservesCachedDataOnSubsequentFailure()
    {
        bool returnSuccess = true;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (returnSuccess)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(SampleSuccessResponse, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        var client = new HttpClient(handler);
        var service = new LeetCodeService(client);
        var vm = new MainViewModel(service)
        {
            Username = "test_coder"
        };

        // 1. Initial Successful Sync
        await vm.RefreshAsync();
        Assert.Equal(350, vm.TotalSolved);
        Assert.Equal(5, vm.CurrentStreak);
        Assert.Contains("Synced", vm.SyncStatusText);
        Assert.True(vm.TotalSubmissions > 0);

        // 2. Second Sync Fails (e.g. offline/network failure)
        returnSuccess = false;
        await vm.RefreshAsync();

        // Cached stats and submissions MUST be preserved
        Assert.Equal(350, vm.TotalSolved);
        Assert.Equal(5, vm.CurrentStreak);
        Assert.True(vm.TotalSubmissions > 0);
        Assert.Contains("showing cached", vm.SyncStatusText, StringComparison.OrdinalIgnoreCase);
    }
}
