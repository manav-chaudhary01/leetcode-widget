using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

public interface ICacheService
{
    string CacheFilePath { get; }
    Task<CachedActivityData?> LoadCacheAsync(string? expectedUsername = null);
    Task SaveCacheAsync(LeetCodeUserStats stats);
}
