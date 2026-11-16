using System;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

/// <summary>
/// Service responsible for managing cryptographic challenge tokens and verifying
/// LeetCode account ownership via public profile 'About Me' field checks.
/// </summary>
public interface IAccountVerificationService
{
    VerificationChallenge? CurrentChallenge { get; }

    /// <summary>
    /// Generates a cryptographically random challenge token bound to the normalized username.
    /// Challenges automatically expire after 15 minutes.
    /// </summary>
    VerificationChallenge GenerateChallenge(string username);

    /// <summary>
    /// Cancels and clears any active challenge.
    /// </summary>
    void CancelChallenge();

    /// <summary>
    /// Verifies ownership by fetching the user's public LeetCode 'About Me' profile
    /// and checking if it contains the active challenge token.
    /// </summary>
    Task<VerificationResult> VerifyOwnershipAsync(string username, CancellationToken cancellationToken = default);
}
