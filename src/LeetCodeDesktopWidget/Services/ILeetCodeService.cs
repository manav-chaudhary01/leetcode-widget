using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

public interface ILeetCodeService
{
    Task<LeetCodeResult<LeetCodeUserStats>> GetUserActivityAsync(string username, int? year = null, CancellationToken cancellationToken = default);
    IReadOnlyDictionary<DateOnly, int> ParseSubmissionCalendar(string? calendarJson);
}
