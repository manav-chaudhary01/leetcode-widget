using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

public class StreakCalculationTests
{
    private static readonly DateOnly TestToday = new(2026, 11, 4);

    [Fact]
    public void CalculateStreaks_EmptyOrNullSubmissions_ReturnsZeroStreaks()
    {
        var nullResult = StreakCalculationService.CalculateStreaks(null, TestToday);
        Assert.Equal(0, nullResult.CurrentStreak);
        Assert.Equal(0, nullResult.LongestStreak);

        var emptyResult = StreakCalculationService.CalculateStreaks(new Dictionary<DateOnly, int>(), TestToday);
        Assert.Equal(0, emptyResult.CurrentStreak);
        Assert.Equal(0, emptyResult.LongestStreak);
    }

    [Fact]
    public void CalculateStreaks_WhenTodayHasActivity_IncludesTodayInCurrentStreak()
    {
        var data = new Dictionary<DateOnly, int>
        {
            [new DateOnly(2026, 11, 2)] = 3,
            [new DateOnly(2026, 11, 3)] = 5,
            [new DateOnly(2026, 11, 4)] = 2  // today
        };

        var result = StreakCalculationService.CalculateStreaks(data, TestToday);

        Assert.Equal(3, result.CurrentStreak);
        Assert.Equal(new DateOnly(2026, 11, 2), result.CurrentStreakStart);
        Assert.Equal(TestToday, result.CurrentStreakEnd);
        Assert.Equal(3, result.LongestStreak);
    }

    [Fact]
    public void CalculateStreaks_WhenOnlyYesterdayHasActivity_MaintainsStreakAlive()
    {
        // User has not coded yet today (Nov 4), but coded Nov 2 and Nov 3
        var data = new Dictionary<DateOnly, int>
        {
            [new DateOnly(2026, 11, 2)] = 2,
            [new DateOnly(2026, 11, 3)] = 4   // yesterday
        };

        var result = StreakCalculationService.CalculateStreaks(data, TestToday);

        Assert.Equal(2, result.CurrentStreak);
        Assert.Equal(new DateOnly(2026, 11, 2), result.CurrentStreakStart);
        Assert.Equal(new DateOnly(2026, 11, 3), result.CurrentStreakEnd);
    }

    [Fact]
    public void CalculateStreaks_WhenLatestActivityOlderThanYesterday_StreakIsZero()
    {
        // Latest activity was Nov 2 (2 days ago), yesterday and today have no activity
        var data = new Dictionary<DateOnly, int>
        {
            [new DateOnly(2026, 10, 31)] = 1,
            [new DateOnly(2026, 11, 1)] = 2,
            [new DateOnly(2026, 11, 2)] = 3
        };

        var result = StreakCalculationService.CalculateStreaks(data, TestToday);

        Assert.Equal(0, result.CurrentStreak);
        Assert.Null(result.CurrentStreakStart);
        Assert.Null(result.CurrentStreakEnd);
        Assert.Equal(3, result.LongestStreak);
    }

    [Fact]
    public void CalculateStreaks_FutureDates_AreStrictlyExcluded()
    {
        var data = new Dictionary<DateOnly, int>
        {
            [new DateOnly(2026, 11, 4)] = 1, // today
            [new DateOnly(2026, 11, 5)] = 5, // tomorrow (future)
            [new DateOnly(2026, 11, 6)] = 2  // day after tomorrow (future)
        };

        var result = StreakCalculationService.CalculateStreaks(data, TestToday);

        Assert.Equal(1, result.CurrentStreak);
        Assert.Equal(1, result.LongestStreak);
        Assert.Equal(TestToday, result.CurrentStreakEnd);
    }

    [Fact]
    public void CalculateStreaks_AcrossYearBoundaries_CalculatesConsecutiveDays()
    {
        var data = new Dictionary<DateOnly, int>
        {
            [new DateOnly(2023, 12, 30)] = 2,
            [new DateOnly(2023, 12, 31)] = 4,
            [new DateOnly(2024, 1, 1)] = 1,
            [new DateOnly(2024, 1, 2)] = 6
        };

        var refDate = new DateOnly(2024, 1, 10);
        var result = StreakCalculationService.CalculateStreaks(data, refDate);

        Assert.Equal(4, result.LongestStreak);
        Assert.Equal(new DateOnly(2023, 12, 30), result.LongestStreakStart);
        Assert.Equal(new DateOnly(2024, 1, 2), result.LongestStreakEnd);
    }

    [Fact]
    public void CalculateStreaks_LeapYear_HandlesFeb29Accurately()
    {
        // 2024 is a leap year: Feb 28 -> Feb 29 -> Mar 1
        var data = new Dictionary<DateOnly, int>
        {
            [new DateOnly(2024, 2, 28)] = 1,
            [new DateOnly(2024, 2, 29)] = 2,
            [new DateOnly(2024, 3, 1)] = 3
        };

        var result = StreakCalculationService.CalculateStreaks(data, new DateOnly(2024, 3, 1));

        Assert.Equal(3, result.CurrentStreak);
        Assert.Equal(3, result.LongestStreak);
    }

    [Fact]
    public void CalculateStreaks_SingleDayActivity_YieldsStreakOfOne()
    {
        var data = new Dictionary<DateOnly, int>
        {
            [TestToday] = 4
        };

        var result = StreakCalculationService.CalculateStreaks(data, TestToday);

        Assert.Equal(1, result.CurrentStreak);
        Assert.Equal(1, result.LongestStreak);
        Assert.Equal(TestToday, result.CurrentStreakStart);
        Assert.Equal(TestToday, result.CurrentStreakEnd);
    }

    [Fact]
    public void CalculateYearlyStatistics_EmptySubmissions_ReturnsZeroes()
    {
        var stats = StreakCalculationService.CalculateYearlyStatistics(2026, null, TestToday);

        Assert.Equal(2026, stats.Year);
        Assert.Equal(0, stats.TotalSubmissions);
        Assert.Equal(0, stats.ActiveDays);
        Assert.Equal(0.0, stats.AveragePerActiveDay);
        Assert.Null(stats.MostActiveDay);
        Assert.Equal(0, stats.LongestStreak);
    }

    [Fact]
    public void CalculateYearlyStatistics_FiltersStrictlyToSelectedYear()
    {
        var data = new Dictionary<DateOnly, int>
        {
            [new DateOnly(2023, 12, 31)] = 10, // adjacent year
            [new DateOnly(2024, 1, 1)] = 2,    // target year 2024
            [new DateOnly(2024, 1, 2)] = 4,    // target year 2024
            [new DateOnly(2024, 6, 15)] = 6,   // target year 2024
            [new DateOnly(2025, 1, 1)] = 8     // adjacent year
        };

        var stats = StreakCalculationService.CalculateYearlyStatistics(2024, data, new DateOnly(2025, 1, 1));

        Assert.Equal(2024, stats.Year);
        Assert.Equal(12, stats.TotalSubmissions); // 2 + 4 + 6
        Assert.Equal(3, stats.ActiveDays);
        Assert.Equal(4.0, stats.AveragePerActiveDay); // 12 / 3
        Assert.NotNull(stats.MostActiveDay);
        Assert.Equal(new DateOnly(2024, 6, 15), stats.MostActiveDay.Date);
        Assert.Equal(6, stats.MostActiveDay.Count);
        Assert.Equal(2, stats.LongestStreak); // Jan 1 & Jan 2
    }

    [Fact]
    public void CalculateYearlyStatistics_MostActiveDay_ResolvesTiesDeterministically()
    {
        var data = new Dictionary<DateOnly, int>
        {
            [new DateOnly(2026, 3, 10)] = 5,
            [new DateOnly(2026, 5, 20)] = 8, // tie 1
            [new DateOnly(2026, 8, 15)] = 8, // tie 2
            [new DateOnly(2026, 9, 1)] = 2
        };

        var stats = StreakCalculationService.CalculateYearlyStatistics(2026, data, TestToday);

        Assert.NotNull(stats.MostActiveDay);
        Assert.Equal(8, stats.MostActiveDay.Count);
        // Ties resolve to the earliest date
        Assert.Equal(new DateOnly(2026, 5, 20), stats.MostActiveDay.Date);
        Assert.Equal("8 on May 20", stats.MostActiveDay.ToString());
    }

    [Fact]
    public void MainViewModel_YearSelectionChanges_UpdatesYearStatisticsImmediately()
    {
        var data = new Dictionary<DateOnly, int>
        {
            [new DateOnly(2024, 5, 1)] = 10,
            [new DateOnly(2024, 5, 2)] = 5,
            [new DateOnly(2025, 3, 1)] = 20
        };

        var stats = new LeetCodeUserStats
        {
            Username = "coder",
            TotalSolved = 150,
            EasySolved = 60,
            MediumSolved = 70,
            HardSolved = 20,
            SubmissionCounts = data
        };

        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(@"{
              ""data"": {
                ""matchedUser"": {
                  ""username"": ""coder"",
                  ""userCalendar"": {
                    ""activeYears"": [2025, 2024],
                    ""streak"": 1,
                    ""totalActiveDays"": 3,
                    ""submissionCalendar"": ""{\""1714521600\"": 10, \""1714608000\"": 5, \""1740787200\"": 20}""
                  },
                  ""submitStatsGlobal"": {
                    ""acSubmissionNum"": [
                      { ""difficulty"": ""All"", ""count"": 150, ""submissions"": 400 },
                      { ""difficulty"": ""Easy"", ""count"": 60, ""submissions"": 150 },
                      { ""difficulty"": ""Medium"", ""count"": 70, ""submissions"": 200 },
                      { ""difficulty"": ""Hard"", ""count"": 20, ""submissions"": 50 }
                    ]
                  }
                }
              }
            }", Encoding.UTF8, "application/json")
        });

        var vm = new MainViewModel(new LeetCodeService(new HttpClient(handler)))
        {
            Username = "coder",
            ReferenceDate = new DateOnly(2025, 12, 31)
        };

        // Trigger sync
        vm.RefreshCommand.Execute(null);

        // All-time difficulty stats verified
        Assert.Equal(150, vm.TotalSolved);
        Assert.Equal(60, vm.EasySolved);
        Assert.Equal(70, vm.MediumSolved);
        Assert.Equal(20, vm.HardSolved);

        // Switch to 2024
        vm.SelectedYear = 2024;
        Assert.Equal(2024, vm.CurrentYearStats.Year);
        Assert.Equal(15, vm.YearSubmissions);
        Assert.Equal(2, vm.YearActiveDays);
        Assert.Equal("7.5", vm.YearAverageText);
        Assert.Equal(2, vm.YearLongestStreak);

        // Switch to 2025
        vm.SelectedYear = 2025;
        Assert.Equal(2025, vm.CurrentYearStats.Year);
        Assert.Equal(20, vm.YearSubmissions);
        Assert.Equal(1, vm.YearActiveDays);
        Assert.Equal("20.0", vm.YearAverageText);
        Assert.Equal(1, vm.YearLongestStreak);
    }

    [Fact]
    public async Task LiveApiVerification_WithRealLeetCodeUser_CalculatesRealStatistics()
    {
        var service = new LeetCodeService();
        var result = await service.GetUserActivityAsync("lee215", 2024);

        Assert.True(result.IsSuccess, $"API query failed: {result.ErrorMessage}");
        Assert.NotNull(result.Data);
        Assert.Equal("lee215", result.Data.Username);
        Assert.True(result.Data.TotalSolved > 500, $"Expected >500 solved problems for lee215, got {result.Data.TotalSolved}");

        var streaks = StreakCalculationService.CalculateStreaks(result.Data.SubmissionCounts);
        Assert.True(streaks.LongestStreak >= 1, "Expected positive longest streak");

        var yearStats = StreakCalculationService.CalculateYearlyStatistics(2024, result.Data.SubmissionCounts);
        Assert.Equal(2024, yearStats.Year);
        Assert.True(yearStats.TotalSubmissions > 0, $"Expected positive submissions in 2024 for lee215, got {yearStats.TotalSubmissions}");
        Assert.True(yearStats.ActiveDays > 0);
        Assert.NotNull(yearStats.MostActiveDay);
    }
}
