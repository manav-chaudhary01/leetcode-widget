using System;

namespace LeetCodeDesktopWidget.Models;

/// <summary>
/// Contribution intensity level (0 to 4).
/// </summary>
public enum ActivityLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Maximum = 4
}

/// <summary>
/// Represents submission activity on a single calendar day.
/// </summary>
public record DailySubmission
{
    public DateOnly Date { get; init; }
    public int Count { get; init; }
    public ActivityLevel Level { get; init; }

    public DailySubmission(DateOnly date, int count = 0)
    {
        Date = date;
        Count = Math.Max(0, count);
        Level = DetermineActivityLevel(Count);
    }

    public DailySubmission(DateOnly date, int count, ActivityLevel level)
    {
        Date = date;
        Count = Math.Max(0, count);
        Level = level;
    }

    /// <summary>
    /// Computes activity level based on submission count thresholds.
    /// </summary>
    public static ActivityLevel DetermineActivityLevel(int count) => count switch
    {
        <= 0 => ActivityLevel.None,
        >= 1 and <= 2 => ActivityLevel.Low,
        >= 3 and <= 5 => ActivityLevel.Medium,
        >= 6 and <= 9 => ActivityLevel.High,
        _ => ActivityLevel.Maximum
    };

    /// <summary>
    /// Formats tooltip or display description.
    /// </summary>
    public string GetDescription()
    {
        var countStr = Count switch
        {
            0 => "No submissions",
            1 => "1 submission",
            _ => $"{Count} submissions"
        };
        return $"{countStr} on {Date:MMM d, yyyy}";
    }
}
