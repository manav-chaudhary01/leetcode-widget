using System;
using System.Text.RegularExpressions;

namespace LeetCodeDesktopWidget.Models;

/// <summary>
/// Persistent user configuration for the widget.
/// </summary>
public record WidgetConfig
{
    public string Username { get; init; } = "";
    public int SelectedYear { get; init; } = DateTime.Now.Year;
    public int RefreshIntervalMinutes { get; init; } = 60;

    public static WidgetConfig Default => new()
    {
        Username = "",
        SelectedYear = DateTime.Now.Year,
        RefreshIntervalMinutes = 60
    };

    /// <summary>
    /// Sanitizes a LeetCode username: strips leading '@', trims whitespace,
    /// and validates allowed characters (alphanumeric, hyphens, underscores).
    /// Returns empty string if invalid or null.
    /// </summary>
    public static string SanitizeUsername(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        var trimmed = input.Trim();
        if (trimmed.StartsWith('@'))
        {
            trimmed = trimmed.TrimStart('@').Trim();
        }

        // LeetCode usernames: alphanumeric, underscores, hyphens, max 40 chars
        if (trimmed.Length > 40)
        {
            trimmed = trimmed[..40];
        }

        return Regex.IsMatch(trimmed, @"^[a-zA-Z0-9_-]+$") ? trimmed : "";
    }
}
