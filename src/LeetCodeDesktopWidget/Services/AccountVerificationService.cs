using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

/// <summary>
/// Implements cryptographic challenge generation and public profile ownership verification.
/// Never requires or handles passwords, cookies, or authentication sessions.
/// </summary>
public class AccountVerificationService : IAccountVerificationService
{
    public static readonly TimeSpan ChallengeDuration = TimeSpan.FromMinutes(15);
    private readonly ILeetCodeService _leetCodeService;
    private readonly object _lock = new();
    private VerificationChallenge? _currentChallenge;

    public VerificationChallenge? CurrentChallenge
    {
        get
        {
            lock (_lock)
            {
                if (_currentChallenge != null && _currentChallenge.IsExpired)
                {
                    _currentChallenge = null;
                }
                return _currentChallenge;
            }
        }
    }

    public AccountVerificationService(ILeetCodeService? leetCodeService = null)
    {
        _leetCodeService = leetCodeService ?? new LeetCodeService();
    }

    public VerificationChallenge GenerateChallenge(string username)
    {
        string normalized = WidgetConfig.SanitizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Cannot generate verification challenge for empty or invalid username.", nameof(username));
        }

        // Generate 12 cryptographically random bytes -> 24 hex characters
        byte[] randomBytes = new byte[12];
        RandomNumberGenerator.Fill(randomBytes);
        string hexToken = Convert.ToHexString(randomBytes).ToLowerInvariant();
        string token = $"lc-verify-{hexToken}";

        var now = DateTime.UtcNow;
        var challenge = new VerificationChallenge(
            normalized,
            token,
            now,
            now.Add(ChallengeDuration));

        lock (_lock)
        {
            _currentChallenge = challenge;
        }

        return challenge;
    }

    public void CancelChallenge()
    {
        lock (_lock)
        {
            _currentChallenge = null;
        }
    }

    public async Task<VerificationResult> VerifyOwnershipAsync(string username, CancellationToken cancellationToken = default)
    {
        string normalized = WidgetConfig.SanitizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return VerificationResult.Failure("Please provide a valid username to verify.");
        }

        VerificationChallenge? activeChallenge;
        lock (_lock)
        {
            activeChallenge = _currentChallenge;
        }

        if (activeChallenge == null)
        {
            return VerificationResult.Failure("No active verification challenge found. Please start verification to generate a token.");
        }

        if (activeChallenge.IsExpired)
        {
            lock (_lock) { _currentChallenge = null; }
            return VerificationResult.Failure("The verification challenge has expired (15-minute limit). Please generate a new challenge.");
        }

        if (!string.Equals(activeChallenge.Username, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return VerificationResult.Failure($"Challenge was generated for @{activeChallenge.Username}, but verifying @{normalized}. Please generate a new challenge.");
        }

        // Fetch fresh, uncached profile from LeetCode
        var profileResult = await _leetCodeService.GetUserProfileAboutMeAsync(normalized, cancellationToken).ConfigureAwait(false);

        if (!profileResult.IsSuccess)
        {
            return VerificationResult.Failure(profileResult.ErrorMessage ?? "Failed to fetch LeetCode profile for verification.");
        }

        string aboutMe = profileResult.Data ?? "";

        // Check for exact substring match of the challenge token
        if (aboutMe.IndexOf(activeChallenge.Token, StringComparison.Ordinal) >= 0)
        {
            // Verification succeeded! Invalidate active challenge
            lock (_lock)
            {
                _currentChallenge = null;
            }

            return VerificationResult.Success(normalized, DateTime.UtcNow);
        }

        return VerificationResult.Failure($"Verification token '{activeChallenge.Token}' was not found in your LeetCode 'About Me' profile. Please paste the token into your LeetCode profile bio, save changes, and try again.");
    }
}
