using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Helpers;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

/// <summary>
/// Client service for communicating with LeetCode's public GraphQL interface.
/// Queries public user activity calendars and solved problem counts.
/// No authentication tokens, cookies, or user passwords required.
/// </summary>
public class LeetCodeService : ILeetCodeService
{
    private const string LeetCodeGraphQLEndpoint = "https://leetcode.com/graphql";
    private readonly HttpClient _httpClient;

    private const string UserAboutMeQuery = @"
query getUserAboutMe($username: String!) {
  matchedUser(username: $username) {
    username
    profile {
      aboutMe
    }
  }
}";

    private const string UserProfileQuery = @"
query getUserProfile($username: String!, $year: Int) {
  matchedUser(username: $username) {
    username
    userCalendar(year: $year) {
      activeYears
      streak
      totalActiveDays
      submissionCalendar
    }
    submitStatsGlobal {
      acSubmissionNum {
        difficulty
        count
        submissions
      }
    }
  }
}";

    public LeetCodeService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        ConfigureDefaultHeaders(_httpClient);
    }

    private static void ConfigureDefaultHeaders(HttpClient client)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }
        if (!client.DefaultRequestHeaders.Contains("Referer"))
        {
            client.DefaultRequestHeaders.Add("Referer", "https://leetcode.com");
        }
    }

    public async Task<LeetCodeResult<LeetCodeUserStats>> GetUserActivityAsync(
        string username,
        int? year = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return LeetCodeResult<LeetCodeUserStats>.Failure(
                "Please configure a valid LeetCode username.",
                LeetCodeErrorCode.UserNotFound);
        }

        username = username.Trim();

        var requestPayload = new GraphQLRequest
        {
            Query = UserProfileQuery,
            Variables = new Dictionary<string, object?>
            {
                ["username"] = username,
                ["year"] = year
            }
        };

        string jsonPayload = JsonSerializer.Serialize(requestPayload);
        using var requestContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        SyncLogger.Log(6, "HTTP request started", $"Endpoint: {LeetCodeGraphQLEndpoint}, User: {username}, Year: {year}");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.PostAsync(
                LeetCodeGraphQLEndpoint,
                requestContent,
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            SyncLogger.Log(7, "HTTP response received", $"StatusCode: {(int)response.StatusCode} ({response.ReasonPhrase}), Elapsed: {stopwatch.ElapsedMilliseconds}ms");

            if ((int)response.StatusCode == 429)
            {
                return LeetCodeResult<LeetCodeUserStats>.Failure(
                    "LeetCode rate limit reached. Please wait a few moments before trying again.",
                    LeetCodeErrorCode.RateLimited);
            }

            if (!response.IsSuccessStatusCode)
            {
                return LeetCodeResult<LeetCodeUserStats>.Failure(
                    $"LeetCode API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    LeetCodeErrorCode.NetworkError);
            }

            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GraphQLResponse<UserProfileData>>(
                responseStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result == null)
            {
                return LeetCodeResult<LeetCodeUserStats>.Failure(
                    "Received empty or invalid response from LeetCode.",
                    LeetCodeErrorCode.InvalidResponse);
            }

            // Stage 8: GraphQL errors, if any
            if (result.Errors != null && result.Errors.Count > 0)
            {
                string errs = string.Join("; ", result.Errors.Select(e => e.Message));
                SyncLogger.Log(8, "GraphQL errors, if any", errs);
            }
            else
            {
                SyncLogger.Log(8, "GraphQL errors, if any", "None");
            }

            if (result.Data?.MatchedUser == null)
            {
                string errMsg = (result.Errors != null && result.Errors.Count > 0)
                    ? string.Join("; ", result.Errors.Select(e => e.Message))
                    : $"LeetCode user '{username}' was not found.";

                return LeetCodeResult<LeetCodeUserStats>.Failure(errMsg, LeetCodeErrorCode.UserNotFound);
            }

            var matched = result.Data.MatchedUser;
            SyncLogger.Log(9, "matchedUser successfully parsed", $"Username: {matched.Username}");

            var calendar = matched.UserCalendar;
            var submitStats = matched.SubmitStatsGlobal;

            // Stage 10: All-time solved statistics parsed
            int total = 0, easy = 0, medium = 0, hard = 0;
            if (submitStats?.AcSubmissionNum != null)
            {
                foreach (var item in submitStats.AcSubmissionNum)
                {
                    switch (item.Difficulty)
                    {
                        case "All":
                            total = item.Count;
                            break;
                        case "Easy":
                            easy = item.Count;
                            break;
                        case "Medium":
                            medium = item.Count;
                            break;
                        case "Hard":
                            hard = item.Count;
                            break;
                    }
                }
            }
            SyncLogger.Log(10, "All-time solved statistics parsed", $"Total: {total}, Easy: {easy}, Medium: {medium}, Hard: {hard}");

            // Stage 11: Year-specific calendar parsed
            bool hasCalendar = true;
            string? calendarError = null;
            IReadOnlyDictionary<DateOnly, int> dailyCounts = new Dictionary<DateOnly, int>();

            if (calendar == null)
            {
                hasCalendar = false;
                calendarError = (result.Errors != null && result.Errors.Count > 0)
                    ? string.Join("; ", result.Errors.Select(e => e.Message))
                    : "No calendar data returned by LeetCode for the specified year.";
                SyncLogger.Log(11, "Year-specific calendar parsed", $"Calendar is null: {calendarError}");
            }
            else
            {
                SyncLogger.Log(11, "Year-specific calendar parsed",
                    $"ActiveYears: [{string.Join(", ", calendar.ActiveYears ?? new List<int>())}], Streak: {calendar.Streak}, ActiveDays: {calendar.TotalActiveDays}, CalendarLength: {calendar.SubmissionCalendar?.Length ?? 0}");

                var (counts, parseSuccess, error) = ParseSubmissionCalendarSafe(calendar.SubmissionCalendar);
                if (!parseSuccess)
                {
                    hasCalendar = false;
                    calendarError = error;
                }
                else
                {
                    dailyCounts = counts;
                }
            }

            var stats = new LeetCodeUserStats
            {
                Username = matched.Username,
                TotalSolved = total,
                EasySolved = easy,
                MediumSolved = medium,
                HardSolved = hard,
                CurrentStreak = calendar?.Streak ?? 0,
                TotalActiveDays = calendar?.TotalActiveDays ?? 0,
                ActiveYears = calendar?.ActiveYears?.AsReadOnly() ?? (IReadOnlyList<int>)Array.Empty<int>(),
                SubmissionCounts = dailyCounts,
                LastSyncedAt = DateTime.Now,
                HasCalendarData = hasCalendar,
                CalendarErrorMessage = calendarError
            };

            return LeetCodeResult<LeetCodeUserStats>.Success(stats);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            SyncLogger.Log(7, "HTTP request failed", $"Exception: {ex.Message}, Elapsed: {stopwatch.ElapsedMilliseconds}ms");
            return LeetCodeResult<LeetCodeUserStats>.Failure(
                $"Network connection error: {ex.Message}",
                LeetCodeErrorCode.NetworkError);
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            SyncLogger.Log(7, "HTTP request timed out/cancelled", $"Elapsed: {stopwatch.ElapsedMilliseconds}ms");
            return LeetCodeResult<LeetCodeUserStats>.Failure(
                "Request timed out while connecting to LeetCode.",
                LeetCodeErrorCode.NetworkError);
        }
        catch (JsonException ex)
        {
            return LeetCodeResult<LeetCodeUserStats>.Failure(
                $"Failed to parse LeetCode data structure: {ex.Message}",
                LeetCodeErrorCode.InvalidResponse);
        }
        catch (Exception ex)
        {
            return LeetCodeResult<LeetCodeUserStats>.Failure(
                $"Unexpected error: {ex.Message}",
                LeetCodeErrorCode.Unknown);
        }
    }

    public async Task<LeetCodeResult<string>> GetUserProfileAboutMeAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return LeetCodeResult<string>.Failure(
                "Please configure a valid LeetCode username.",
                LeetCodeErrorCode.UserNotFound);
        }

        username = username.Trim();

        var requestPayload = new GraphQLRequest
        {
            Query = UserAboutMeQuery,
            Variables = new Dictionary<string, object?>
            {
                ["username"] = username
            }
        };

        string jsonPayload = JsonSerializer.Serialize(requestPayload);
        using var requestContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.PostAsync(
                LeetCodeGraphQLEndpoint,
                requestContent,
                cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode == 429)
            {
                return LeetCodeResult<string>.Failure(
                    "LeetCode rate limit reached. Please wait a few moments before trying again.",
                    LeetCodeErrorCode.RateLimited);
            }

            if (!response.IsSuccessStatusCode)
            {
                return LeetCodeResult<string>.Failure(
                    $"LeetCode API returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).",
                    LeetCodeErrorCode.NetworkError);
            }

            var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<GraphQLResponse<UserProfileData>>(
                responseStream,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (result == null)
            {
                return LeetCodeResult<string>.Failure(
                    "Received empty or invalid response from LeetCode.",
                    LeetCodeErrorCode.InvalidResponse);
            }

            if (result.Data?.MatchedUser == null)
            {
                string errMsg = (result.Errors != null && result.Errors.Count > 0)
                    ? result.Errors[0].Message
                    : $"LeetCode user '{username}' was not found.";

                return LeetCodeResult<string>.Failure(errMsg, LeetCodeErrorCode.UserNotFound);
            }

            string aboutMe = result.Data.MatchedUser.Profile?.AboutMe ?? "";
            return LeetCodeResult<string>.Success(aboutMe);
        }
        catch (HttpRequestException ex)
        {
            return LeetCodeResult<string>.Failure(
                $"Network connection error: {ex.Message}",
                LeetCodeErrorCode.NetworkError);
        }
        catch (TaskCanceledException)
        {
            return LeetCodeResult<string>.Failure(
                "Request timed out while connecting to LeetCode.",
                LeetCodeErrorCode.NetworkError);
        }
        catch (JsonException ex)
        {
            return LeetCodeResult<string>.Failure(
                $"Failed to parse LeetCode profile response: {ex.Message}",
                LeetCodeErrorCode.InvalidResponse);
        }
        catch (Exception ex)
        {
            return LeetCodeResult<string>.Failure(
                $"Unexpected error: {ex.Message}",
                LeetCodeErrorCode.Unknown);
        }
    }

    /// <summary>
    /// Safely parses LeetCode's submissionCalendar JSON string ("{ \"1719100800\": 3, ... }")
    /// into a dictionary mapping calendar dates to submission counts, logging stages 12 and 13.
    /// </summary>
    public (IReadOnlyDictionary<DateOnly, int> Counts, bool Success, string? Error) ParseSubmissionCalendarSafe(string? calendarJson)
    {
        var result = new Dictionary<DateOnly, int>();

        if (string.IsNullOrWhiteSpace(calendarJson) || calendarJson.Trim() == "{}" || calendarJson.Trim() == "null")
        {
            SyncLogger.Log(12, "Submission calendar JSON decoded", "Empty calendar ({})");
            SyncLogger.Log(13, "Daily submissions converted to dates and aggregated", "0 dates, 0 submissions");
            return (result, true, null);
        }

        try
        {
            SyncLogger.Log(12, "Submission calendar JSON decoded", $"Payload length: {calendarJson.Length}");
            using var doc = JsonDocument.Parse(calendarJson);
            int totalSubmissions = 0;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (long.TryParse(prop.Name, out long epochSeconds))
                {
                    int count = 0;
                    if (prop.Value.ValueKind == JsonValueKind.Number)
                    {
                        count = prop.Value.GetInt32();
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out int c))
                    {
                        count = c;
                    }

                    if (count > 0)
                    {
                        var utcDate = DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime.Date;
                        var dateOnly = DateOnly.FromDateTime(utcDate);
                        result[dateOnly] = result.GetValueOrDefault(dateOnly) + count;
                        totalSubmissions += count;
                    }
                }
            }

            SyncLogger.Log(13, "Daily submissions converted to dates and aggregated",
                $"Dates: {result.Count}, TotalSubmissions: {totalSubmissions}");

            return (result, true, null);
        }
        catch (JsonException ex)
        {
            SyncLogger.Log(12, "Submission calendar JSON decoded", $"Failed: {ex.Message}");
            return (result, false, $"Malformed submission calendar JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            SyncLogger.Log(12, "Submission calendar JSON decoded", $"Failed: {ex.Message}");
            return (result, false, $"Unexpected calendar parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses LeetCode's submissionCalendar JSON string ("{ \"1719100800\": 3, ... }")
    /// into a dictionary mapping calendar dates to submission counts.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, int> ParseSubmissionCalendar(string? calendarJson)
    {
        return ParseSubmissionCalendarSafe(calendarJson).Counts;
    }
}
