using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LeetCodeDesktopWidget.Models;

/// <summary>
/// Serializable schema for persisted LeetCode activity and solved problem telemetry.
/// </summary>
public record CachedActivityData
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Username { get; init; } = "";
    public DateTime LastSuccessfulSync { get; init; }
    public int TotalSolved { get; init; }
    public int EasySolved { get; init; }
    public int MediumSolved { get; init; }
    public int HardSolved { get; init; }
    public int CurrentStreak { get; init; }
    public int TotalActiveDays { get; init; }
    public List<int> ActiveYears { get; init; } = new();
    public Dictionary<string, int> Submissions { get; init; } = new();

    /// <summary>
    /// Converts a live LeetCodeUserStats model into a serializable CachedActivityData record.
    /// </summary>
    public static CachedActivityData FromUserStats(LeetCodeUserStats stats)
    {
        var submissionDict = new Dictionary<string, int>();
        foreach (var (date, count) in stats.SubmissionCounts)
        {
            submissionDict[date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)] = count;
        }

        return new CachedActivityData
        {
            SchemaVersion = CurrentSchemaVersion,
            Username = stats.Username,
            LastSuccessfulSync = stats.LastSyncedAt ?? DateTime.Now,
            TotalSolved = stats.TotalSolved,
            EasySolved = stats.EasySolved,
            MediumSolved = stats.MediumSolved,
            HardSolved = stats.HardSolved,
            CurrentStreak = stats.CurrentStreak,
            TotalActiveDays = stats.TotalActiveDays,
            ActiveYears = stats.ActiveYears.ToList(),
            Submissions = submissionDict
        };
    }

    /// <summary>
    /// Restores a LeetCodeUserStats model from cached activity data.
    /// </summary>
    public LeetCodeUserStats ToUserStats()
    {
        var parsedSubmissions = new Dictionary<DateOnly, int>();
        foreach (var (dateStr, count) in Submissions)
        {
            if (DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                parsedSubmissions[date] = count;
            }
        }

        return new LeetCodeUserStats
        {
            Username = Username,
            TotalSolved = TotalSolved,
            EasySolved = EasySolved,
            MediumSolved = MediumSolved,
            HardSolved = HardSolved,
            CurrentStreak = CurrentStreak,
            TotalActiveDays = TotalActiveDays,
            ActiveYears = ActiveYears.AsReadOnly(),
            SubmissionCounts = parsedSubmissions,
            LastSyncedAt = LastSuccessfulSync
        };
    }
}
