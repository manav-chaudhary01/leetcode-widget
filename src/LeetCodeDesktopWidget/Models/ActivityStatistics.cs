using System;

namespace LeetCodeDesktopWidget.Models;

/// <summary>
/// Streak metrics across active submission history.
/// </summary>
public record StreakInfo
{
    public int CurrentStreak { get; init; }
    public int LongestStreak { get; init; }
    public DateOnly? CurrentStreakStart { get; init; }
    public DateOnly? CurrentStreakEnd { get; init; }
    public DateOnly? LongestStreakStart { get; init; }
    public DateOnly? LongestStreakEnd { get; init; }

    public static StreakInfo Empty => new()
    {
        CurrentStreak = 0,
        LongestStreak = 0
    };
}

/// <summary>
/// Detailed submission statistics for a single calendar year.
/// </summary>
public record YearlyStatistics
{
    public int Year { get; init; }
    public int TotalSubmissions { get; init; }
    public int ActiveDays { get; init; }
    public double AveragePerActiveDay { get; init; }
    public MostActiveDayInfo? MostActiveDay { get; init; }
    public int LongestStreak { get; init; }

    public static YearlyStatistics Empty(int year) => new()
    {
        Year = year,
        TotalSubmissions = 0,
        ActiveDays = 0,
        AveragePerActiveDay = 0.0,
        MostActiveDay = null,
        LongestStreak = 0
    };
}

/// <summary>
/// Information about the day with the highest submission count in a year.
/// </summary>
public record MostActiveDayInfo(DateOnly Date, int Count)
{
    public override string ToString() => $"{Count} on {Date:MMM d}";
}
