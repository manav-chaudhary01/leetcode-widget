using System;

namespace LeetCodeDesktopWidget.Models;

/// <summary>
/// Result of an account ownership verification attempt.
/// Preserves the cryptographic challenge token on success.
/// </summary>
public record VerificationResult(
    bool IsSuccess,
    string? ErrorMessage = null,
    string? VerifiedUsername = null,
    DateTime? VerifiedAtUtc = null,
    string? VerificationChallengeToken = null)
{
    public static VerificationResult Success(string username, DateTime verifiedAtUtc, string challengeToken) =>
        new(true, null, username, verifiedAtUtc, challengeToken);

    public static VerificationResult Failure(string errorMessage) =>
        new(false, errorMessage, null, null, null);
}
