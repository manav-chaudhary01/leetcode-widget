using System;
using System.Collections.Generic;
using LeetCodeDesktopWidget.Models;
using LeetCodeDesktopWidget.Services;
using LeetCodeDesktopWidget.ViewModels;
using Xunit;

namespace LeetCodeDesktopWidget.Tests;

public class HeatmapTests
{
    [Fact]
    public void LeapYears_2024And2028_Contain366Days()
    {
        var activity2024 = YearlyActivity.Create(2024);
        var activity2028 = YearlyActivity.Create(2028);

        Assert.True(activity2024.IsLeapYear);
        Assert.Equal(366, activity2024.TotalDays);
        Assert.Equal(366, activity2024.Days.Count);
        Assert.Contains(new DateOnly(2024, 2, 29), activity2024.DayMap.Keys);

        Assert.True(activity2028.IsLeapYear);
        Assert.Equal(366, activity2028.TotalDays);
        Assert.Equal(366, activity2028.Days.Count);
        Assert.Contains(new DateOnly(2028, 2, 29), activity2028.DayMap.Keys);
    }

    [Fact]
    public void NonLeapYears_2025And2026_Contain365Days()
    {
        var activity2025 = YearlyActivity.Create(2025);
        var activity2026 = YearlyActivity.Create(2026);

        Assert.False(activity2025.IsLeapYear);
        Assert.Equal(365, activity2025.TotalDays);
        Assert.Equal(365, activity2025.Days.Count);
        Assert.DoesNotContain(activity2025.Days, d => d.Date.Month == 2 && d.Date.Day == 29);

        Assert.False(activity2026.IsLeapYear);
        Assert.Equal(365, activity2026.TotalDays);
        Assert.Equal(365, activity2026.Days.Count);
        Assert.DoesNotContain(activity2026.Days, d => d.Date.Month == 2 && d.Date.Day == 29);
    }

    [Theory]
    [InlineData(0, ActivityLevel.None)]
    [InlineData(1, ActivityLevel.Low)]
    [InlineData(2, ActivityLevel.Low)]
    [InlineData(3, ActivityLevel.Medium)]
    [InlineData(5, ActivityLevel.Medium)]
    [InlineData(6, ActivityLevel.High)]
    [InlineData(9, ActivityLevel.High)]
    [InlineData(10, ActivityLevel.Maximum)]
    [InlineData(25, ActivityLevel.Maximum)]
    public void ActivityLevel_ThresholdsMapCorrectly(int count, ActivityLevel expectedLevel)
    {
        var level = DailySubmission.DetermineActivityLevel(count);
        Assert.Equal(expectedLevel, level);
    }

    [Fact]
    public void DailySubmission_DescriptionFormatsAccurately()
    {
        var date = new DateOnly(2026, 10, 14);

        var zeroSub = new DailySubmission(date, 0);
        Assert.Equal("No submissions on Oct 14, 2026", zeroSub.GetDescription());

        var singleSub = new DailySubmission(date, 1);
        Assert.Equal("1 submission on Oct 14, 2026", singleSub.GetDescription());

        var multiSub = new DailySubmission(date, 7);
        Assert.Equal("7 submissions on Oct 14, 2026", multiSub.GetDescription());
    }

    [Fact]
    public void GridCoordinates_MapToValidColumnsAndRows()
    {
        // 2026: Jan 1 is Thursday (DayOfWeek.Thursday = 4)
        var jan1 = new DateOnly(2026, 1, 1);
        var (col1, row1) = YearlyActivity.GetGridCoordinates(jan1);
        Assert.Equal(0, col1);
        Assert.Equal(4, row1); // Thursday = row 4

        // 2026: Dec 31
        var dec31 = new DateOnly(2026, 12, 31);
        var (colDec, rowDec) = YearlyActivity.GetGridCoordinates(dec31);
        Assert.True(colDec >= 52 && colDec <= 53);
        Assert.True(rowDec >= 0 && rowDec <= 6);
    }

    [Fact]
    public void MonthStartColumns_MonotonicallyIncrease()
    {
        var activity = YearlyActivity.Create(2026);
        var monthCols = activity.GetMonthStartColumns();

        Assert.Equal(12, monthCols.Count);
        int prevCol = -1;
        for (int m = 1; m <= 12; m++)
        {
            Assert.True(monthCols.ContainsKey(m));
            int col = monthCols[m];
            Assert.True(col >= prevCol, $"Month {m} start column ({col}) must be >= previous month ({prevCol})");
            prevCol = col;
        }
    }

    [Fact]
    public void MainViewModel_YearSwitchingLoadsNewActivity()
    {
        var vm = new MainViewModel();
        Assert.Equal(2026, vm.SelectedYear);
        Assert.NotNull(vm.CurrentActivity);
        Assert.Equal(2026, vm.CurrentActivity.Year);

        // Switch to leap year 2024
        vm.SelectedYear = 2024;
        Assert.Equal(2024, vm.SelectedYear);
        Assert.Equal(2024, vm.CurrentActivity.Year);
        Assert.Equal(366, vm.CurrentActivity.TotalDays);
        Assert.NotEmpty(vm.SummaryStatsText);

        // Test hover info binding
        var sampleDay = new DailySubmission(new DateOnly(2024, 6, 15), 4);
        vm.HoveredDay = sampleDay;
        Assert.Equal(sampleDay.GetDescription(), vm.HoverInfoText);

        vm.HoveredDay = null;
        Assert.Equal("Hover over a square to view submissions", vm.HoverInfoText);
    }

    [Fact]
    public void MockDataService_ProducesConsistentDataForYear()
    {
        var activity1 = MockDataService.GenerateMockYearlyActivity(2026);
        var activity2 = MockDataService.GenerateMockYearlyActivity(2026);

        Assert.Equal(activity1.TotalSubmissions, activity2.TotalSubmissions);
        Assert.Equal(activity1.ActiveDays, activity2.ActiveDays);
        Assert.True(activity1.TotalSubmissions > 0);
        Assert.True(activity1.ActiveDays > 0);
    }
}
