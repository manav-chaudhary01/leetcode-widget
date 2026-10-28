using System;
using System.Collections.Generic;
using System.Linq;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

/// <summary>
/// Pure, deterministic calculation engine for streaks, active days, and yearly submission telemetry.
/// </summary>
public static class StreakCalculationService
{
    /// <summary>
    /// Calculates current and longest streaks across the provided submission calendar.
    /// Current Streak Semantics:
    /// - Counts consecutive days with at least 1 submission ending either today (if active today),
    ///   or yesterday (if today has no activity yet, giving the user until midnight to extend it).
    /// - If neither today nor yesterday has activity, the current streak is 0.
    /// - Future dates (> referenceDate) are strictly excluded.
    /// </summary>
    public static StreakInfo CalculateStreaks(
        IReadOnlyDictionary<DateOnly, int>? submissions,
        DateOnly? referenceDate = null)
    {
        if (submissions == null || submissions.Count == 0)
        {
            return StreakInfo.Empty;
        }

        var today = referenceDate ?? DateOnly.FromDateTime(DateTime.Today);

        // Filter active dates (count > 0) strictly up to today
        var activeDates = submissions
            .Where(kvp => kvp.Value > 0 && kvp.Key <= today)
            .Select(kvp => kvp.Key)
            .ToHashSet();

        if (activeDates.Count == 0)
        {
            return StreakInfo.Empty;
        }

        // 1. Current Streak Calculation
        int currentStreak = 0;
        DateOnly? currentStreakStart = null;
        DateOnly? currentStreakEnd = null;

        DateOnly? streakCursor = null;

        if (activeDates.Contains(today))
        {
            streakCursor = today;
            currentStreakEnd = today;
        }
        else
        {
            var yesterday = today.AddDays(-1);
            if (activeDates.Contains(yesterday))
            {
                streakCursor = yesterday;
                currentStreakEnd = yesterday;
            }
        }

        if (streakCursor.HasValue)
        {
            var curr = streakCursor.Value;
            while (activeDates.Contains(curr))
            {
                currentStreak++;
                currentStreakStart = curr;
                curr = curr.AddDays(-1);
            }
        }

        // 2. Longest Streak Calculation across all history
        var sortedDates = activeDates.OrderBy(d => d).ToList();
        int longestStreak = 0;
        DateOnly? longestStart = null;
        DateOnly? longestEnd = null;

        int currentRun = 0;
        DateOnly? runStart = null;
        DateOnly? prevDate = null;

        foreach (var date in sortedDates)
        {
            if (prevDate.HasValue && date == prevDate.Value.AddDays(1))
            {
                currentRun++;
            }
            else
            {
                currentRun = 1;
                runStart = date;
            }

            if (currentRun > longestStreak)
            {
                longestStreak = currentRun;
                longestStart = runStart;
                longestEnd = date;
            }

            prevDate = date;
        }

        return new StreakInfo
        {
            CurrentStreak = currentStreak,
            LongestStreak = longestStreak,
            CurrentStreakStart = currentStreakStart,
            CurrentStreakEnd = currentStreakEnd,
            LongestStreakStart = longestStart,
            LongestStreakEnd = longestEnd
        };
    }

    /// <summary>
    /// Calculates activity telemetry strictly for the specified calendar year.
    /// Does not include submissions from adjacent years.
    /// </summary>
    public static YearlyStatistics CalculateYearlyStatistics(
        int year,
        IReadOnlyDictionary<DateOnly, int>? submissions,
        DateOnly? referenceDate = null)
    {
        if (submissions == null || submissions.Count == 0)
        {
            return YearlyStatistics.Empty(year);
        }

        var maxDate = referenceDate ?? DateOnly.FromDateTime(DateTime.Today);

        // Filter activity belonging strictly to 'year' and not in the future
        var yearActivity = submissions
            .Where(kvp => kvp.Key.Year == year && kvp.Key <= maxDate && kvp.Value > 0)
            .OrderBy(kvp => kvp.Key)
            .ToList();

        if (yearActivity.Count == 0)
        {
            return YearlyStatistics.Empty(year);
        }

        int totalSubmissions = yearActivity.Sum(kvp => kvp.Value);
        int activeDays = yearActivity.Count;
        double averagePerActiveDay = activeDays > 0 ? (double)totalSubmissions / activeDays : 0.0;

        // Most active day (highest submission count, earliest date on ties)
        var maxEntry = yearActivity
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key)
            .First();

        var mostActiveDay = new MostActiveDayInfo(maxEntry.Key, maxEntry.Value);

        // Longest streak bounded strictly within this year
        int longestStreak = 0;
        int currentRun = 0;
        DateOnly? prevDate = null;

        foreach (var (date, _) in yearActivity)
        {
            if (prevDate.HasValue && date == prevDate.Value.AddDays(1))
            {
                currentRun++;
            }
            else
            {
                currentRun = 1;
            }

            if (currentRun > longestStreak)
            {
                longestStreak = currentRun;
            }

            prevDate = date;
        }

        return new YearlyStatistics
        {
            Year = year,
            TotalSubmissions = totalSubmissions,
            ActiveDays = activeDays,
            AveragePerActiveDay = averagePerActiveDay,
            MostActiveDay = mostActiveDay,
            LongestStreak = longestStreak
        };
    }
}
