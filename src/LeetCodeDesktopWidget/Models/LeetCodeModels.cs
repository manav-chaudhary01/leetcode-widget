using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LeetCodeDesktopWidget.Models;

public enum LeetCodeErrorCode
{
    None = 0,
    UserNotFound,
    NetworkError,
    RateLimited,
    InvalidResponse,
    Unknown
}

/// <summary>
/// Result wrapper providing success/error metadata for LeetCode operations.
/// </summary>
public record LeetCodeResult<T>
{
    public bool IsSuccess { get; init; }
    public T? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public LeetCodeErrorCode ErrorCode { get; init; }

    public static LeetCodeResult<T> Success(T data) => new()
    {
        IsSuccess = true,
        Data = data,
        ErrorCode = LeetCodeErrorCode.None
    };

    public static LeetCodeResult<T> Failure(string errorMessage, LeetCodeErrorCode code = LeetCodeErrorCode.Unknown) => new()
    {
        IsSuccess = false,
        ErrorMessage = errorMessage,
        ErrorCode = code
    };
}

/// <summary>
/// Domain model containing user activity and problem solving statistics.
/// </summary>
public class LeetCodeUserStats
{
    public string Username { get; init; } = "";
    public int TotalSolved { get; init; }
    public int EasySolved { get; init; }
    public int MediumSolved { get; init; }
    public int HardSolved { get; init; }
    public int CurrentStreak { get; init; }
    public int TotalActiveDays { get; init; }
    public IReadOnlyList<int> ActiveYears { get; init; } = Array.Empty<int>();
    public IReadOnlyDictionary<DateOnly, int> SubmissionCounts { get; init; } = new Dictionary<DateOnly, int>();
    public DateTime? LastSyncedAt { get; init; }
}

#region GraphQL DTOs

public class GraphQLRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("variables")]
    public Dictionary<string, object?> Variables { get; set; } = new();
}

public class GraphQLResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    [JsonPropertyName("errors")]
    public List<GraphQLError>? Errors { get; set; }
}

public class GraphQLError
{
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public class UserProfileData
{
    [JsonPropertyName("matchedUser")]
    public MatchedUser? MatchedUser { get; set; }
}

public class MatchedUser
{
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("userCalendar")]
    public UserCalendar? UserCalendar { get; set; }

    [JsonPropertyName("submitStatsGlobal")]
    public SubmitStatsGlobal? SubmitStatsGlobal { get; set; }
}

public class UserCalendar
{
    [JsonPropertyName("activeYears")]
    public List<int>? ActiveYears { get; set; }

    [JsonPropertyName("streak")]
    public int Streak { get; set; }

    [JsonPropertyName("totalActiveDays")]
    public int TotalActiveDays { get; set; }

    [JsonPropertyName("submissionCalendar")]
    public string? SubmissionCalendar { get; set; }
}

public class SubmitStatsGlobal
{
    [JsonPropertyName("acSubmissionNum")]
    public List<AcSubmissionNum>? AcSubmissionNum { get; set; }
}

public class AcSubmissionNum
{
    [JsonPropertyName("difficulty")]
    public string Difficulty { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("submissions")]
    public int Submissions { get; set; }
}

#endregion
