using System;

namespace LeetCodeDesktopWidget.Models;

/// <summary>
/// Result of an account ownership verification attempt.
/// </summary>
public record VerificationResult(
    bool IsSuccess,
    string? ErrorMessage = null,
    string? VerifiedUsername = null,
    DateTime? VerifiedAtUtc = null)
{
    public static VerificationResult Success(string username, DateTime verifiedAtUtc) =>
        new(true, null, username, verifiedAtUtc);

    public static VerificationResult Failure(string errorMessage) =>
        new(false, errorMessage, null, null);
}
