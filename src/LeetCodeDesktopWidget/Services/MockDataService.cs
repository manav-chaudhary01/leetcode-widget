using System;
using System.Collections.Generic;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

/// <summary>
/// Development-only mock data provider.
/// Generates realistic submission activity distributions strictly for UI testing and layout verification.
/// This service is isolated from production LeetCode API clients.
/// </summary>
public static class MockDataService
{
    /// <summary>
    /// Generates a realistic mock activity profile for the given year.
    /// Uses a fixed seed based on year to ensure deterministic UI rendering during testing.
    /// </summary>
    public static YearlyActivity GenerateMockYearlyActivity(int year)
    {
        var rng = new Random(year * 31);
        var counts = new Dictionary<DateOnly, int>();

        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        // Simulate realistic coding streaks, weekends, and gaps
        int streakDaysLeft = 0;

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            if (streakDaysLeft > 0)
            {
                streakDaysLeft--;
                // In active streak: higher probability of submissions
                counts[date] = rng.Next(1, 12);
            }
            else
            {
                // Decide whether to start a new streak or have empty days
                double p = rng.NextDouble();
                if (p < 0.35)
                {
                    // Start streak of 3 to 14 days
                    streakDaysLeft = rng.Next(3, 14);
                    counts[date] = rng.Next(1, 8);
                }
                else if (p < 0.55)
                {
                    // Single day activity
                    counts[date] = rng.Next(1, 4);
                }
                else
                {
                    // Rest / no submissions day
                    counts[date] = 0;
                }
            }
        }

        return YearlyActivity.Create(year, counts);
    }
}
