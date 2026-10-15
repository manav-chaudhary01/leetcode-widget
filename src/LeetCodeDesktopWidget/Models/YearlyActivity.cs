using System;
using System.Collections.Generic;
using System.Linq;

namespace LeetCodeDesktopWidget.Models;

/// <summary>
/// Represents full calendar year submission activity and grid layout mappings.
/// </summary>
public class YearlyActivity
{
    public int Year { get; }
    public bool IsLeapYear => DateTime.IsLeapYear(Year);
    public int TotalDays => IsLeapYear ? 366 : 365;
    public IReadOnlyList<DailySubmission> Days { get; }
    public IReadOnlyDictionary<DateOnly, DailySubmission> DayMap { get; }
    public int TotalSubmissions { get; }
    public int ActiveDays { get; }
    public int MaxDailySubmissions { get; }

    public YearlyActivity(int year, IEnumerable<DailySubmission> days)
    {
        Year = year;
        var dayList = days.OrderBy(d => d.Date).ToList();
        Days = dayList.AsReadOnly();
        DayMap = dayList.ToDictionary(d => d.Date);

        int total = 0;
        int active = 0;
        int max = 0;

        foreach (var d in dayList)
        {
            total += d.Count;
            if (d.Count > 0) active++;
            if (d.Count > max) max = d.Count;
        }

        TotalSubmissions = total;
        ActiveDays = active;
        MaxDailySubmissions = max;
    }

    /// <summary>
    /// Creates a complete yearly activity object, filling all calendar days (365 or 366)
    /// from Jan 1 through Dec 31.
    /// </summary>
    public static YearlyActivity Create(int year, IReadOnlyDictionary<DateOnly, int>? submissionCounts = null)
    {
        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);
        var days = new List<DailySubmission>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            int count = 0;
            if (submissionCounts != null && submissionCounts.TryGetValue(date, out int c))
            {
                count = c;
            }
            days.Add(new DailySubmission(date, count));
        }

        return new YearlyActivity(year, days);
    }

    /// <summary>
    /// Maps a calendar date to horizontal week column (0..52) and weekday row (0..6, Sunday=0).
    /// </summary>
    public static (int Column, int Row) GetGridCoordinates(DateOnly date)
    {
        var jan1 = new DateOnly(date.Year, 1, 1);
        int jan1DayOfWeek = (int)jan1.DayOfWeek; // 0 = Sunday, 6 = Saturday
        int dayIndex = date.DayOfYear - 1 + jan1DayOfWeek;
        int col = dayIndex / 7;
        int row = dayIndex % 7;
        return (col, row);
    }

    /// <summary>
    /// Gets the starting column index for each month (1 to 12) in the year.
    /// </summary>
    public IReadOnlyDictionary<int, int> GetMonthStartColumns()
    {
        var map = new Dictionary<int, int>();
        for (int month = 1; month <= 12; month++)
        {
            var monthDate = new DateOnly(Year, month, 1);
            var (col, _) = GetGridCoordinates(monthDate);
            map[month] = col;
        }
        return map;
    }
}
