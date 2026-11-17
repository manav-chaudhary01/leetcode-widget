using System;
using System.Text.RegularExpressions;

namespace LeetCodeDesktopWidget.Models;

/// <summary>
/// Persistent user configuration for the widget.
/// Enforces cryptographic verification versioning and challenge token binding.
/// </summary>
public record WidgetConfig
{
    /// <summary>
    /// Current cryptographic verification format version.
    /// Version 1: Legacy unproven username matching.
    /// Version 2: Cryptographic challenge token verification binding.
    /// </summary>
    public const int CurrentVerificationVersion = 2;

    public int VerificationVersion { get; init; } = CurrentVerificationVersion;
    public string Username { get; init; } = "";
    public int SelectedYear { get; init; } = DateTime.Now.Year;
    public int RefreshIntervalMinutes { get; init; } = 60;
    public string VerifiedUsername { get; init; } = "";
    public DateTime? VerifiedAt { get; init; }
    public string VerificationChallengeCode { get; init; } = "";
    public double? WindowX { get; init; }
    public double? WindowY { get; init; }

    /// <summary>
    /// Verification invariant:
    /// An account is verified IF AND ONLY IF:
    /// 1. Its verification version is >= CurrentVerificationVersion (2).
    /// 2. Username and VerifiedUsername are non-empty and match case-insensitively.
    /// 3. VerifiedAt timestamp is present.
    /// </summary>
    public bool IsAccountVerified =>
        VerificationVersion >= CurrentVerificationVersion &&
        !string.IsNullOrWhiteSpace(VerifiedUsername) &&
        !string.IsNullOrWhiteSpace(Username) &&
        string.Equals(Username, VerifiedUsername, StringComparison.OrdinalIgnoreCase) &&
        VerifiedAt.HasValue;

    public static WidgetConfig Default => new()
    {
        VerificationVersion = CurrentVerificationVersion,
        Username = "",
        SelectedYear = DateTime.Now.Year,
        RefreshIntervalMinutes = 60,
        VerifiedUsername = "",
        VerifiedAt = null,
        VerificationChallengeCode = "",
        WindowX = null,
        WindowY = null
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
