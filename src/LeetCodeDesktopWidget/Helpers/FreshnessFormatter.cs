using System;

namespace LeetCodeDesktopWidget.Helpers;

/// <summary>
/// Formats sync timestamps into concise, natural freshness descriptions.
/// </summary>
public static class FreshnessFormatter
{
    public static string GetRelativeTime(DateTime timestamp, DateTime? referenceTime = null)
    {
        var now = referenceTime ?? DateTime.Now;
        if (timestamp > now)
        {
            return "just now";
        }

        var span = now - timestamp;

        if (span.TotalMinutes < 1)
        {
            return "just now";
        }
        if (span.TotalMinutes < 60)
        {
            int mins = Math.Max(1, (int)span.TotalMinutes);
            return $"{mins}m ago";
        }
        if (span.TotalHours < 24)
        {
            int hours = Math.Max(1, (int)span.TotalHours);
            return $"{hours}h ago";
        }
        if (now.Date.AddDays(-1) == timestamp.Date)
        {
            return "yesterday";
        }
        if (span.TotalDays < 7)
        {
            return $"{(int)span.TotalDays}d ago";
        }

        return timestamp.ToString("MMM d");
    }

    public static string FormatSyncStatus(DateTime? lastSync, bool isOffline, string? failureReason = null, DateTime? referenceTime = null)
    {
        if (!lastSync.HasValue)
        {
            if (isOffline)
            {
                return string.IsNullOrWhiteSpace(failureReason)
                    ? "Offline · no data"
                    : $"Sync failed · {failureReason}";
            }
            return "Not synced";
        }

        var rel = GetRelativeTime(lastSync.Value, referenceTime);

        if (isOffline)
        {
            return $"Offline · cached {rel}";
        }

        return $"Synced {rel}";
    }
}
