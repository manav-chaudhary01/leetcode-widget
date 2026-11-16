using System;

namespace LeetCodeDesktopWidget.Models;

/// <summary>
/// Represents a temporary challenge token bound to a normalized LeetCode username
/// for proving account ownership via the public LeetCode 'About Me' profile field.
/// </summary>
public record VerificationChallenge(
    string Username,
    string Token,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc)
{
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;

    public TimeSpan RemainingTime
    {
        get
        {
            var left = ExpiresAtUtc - DateTime.UtcNow;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }
}
