using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        _httpClient = httpClient ?? new HttpClient();
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

        try
        {
            using var response = await _httpClient.PostAsync(
                LeetCodeGraphQLEndpoint,
                requestContent,
                cancellationToken).ConfigureAwait(false);

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

            if (result.Data?.MatchedUser == null)
            {
                string errMsg = (result.Errors != null && result.Errors.Count > 0)
                    ? result.Errors[0].Message
                    : $"LeetCode user '{username}' was not found.";

                return LeetCodeResult<LeetCodeUserStats>.Failure(errMsg, LeetCodeErrorCode.UserNotFound);
            }

            var matched = result.Data.MatchedUser;
            var calendar = matched.UserCalendar;
            var submitStats = matched.SubmitStatsGlobal;

            // Extract solved counts
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

            // Parse submission calendar
            var dailyCounts = ParseSubmissionCalendar(calendar?.SubmissionCalendar);

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
                LastSyncedAt = DateTime.Now
            };

            return LeetCodeResult<LeetCodeUserStats>.Success(stats);
        }
        catch (HttpRequestException ex)
        {
            return LeetCodeResult<LeetCodeUserStats>.Failure(
                $"Network connection error: {ex.Message}",
                LeetCodeErrorCode.NetworkError);
        }
        catch (TaskCanceledException)
        {
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

    /// <summary>
    /// Parses LeetCode's submissionCalendar JSON string ("{ \"1719100800\": 3, ... }")
    /// into a dictionary mapping calendar dates to submission counts.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, int> ParseSubmissionCalendar(string? calendarJson)
    {
        var result = new Dictionary<DateOnly, int>();

        if (string.IsNullOrWhiteSpace(calendarJson) || calendarJson.Trim() == "{}" || calendarJson.Trim() == "null")
        {
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(calendarJson);
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
                    }
                }
            }
        }
        catch
        {
            // Fallback gracefully on malformed JSON
        }

        return result;
    }
}
