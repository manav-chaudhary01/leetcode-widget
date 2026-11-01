using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

/// <summary>
/// Manages atomic persistence and retrieval of LeetCode submission activity and statistics.
/// </summary>
public class CacheService : ICacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _fileLock = new();
    public string CacheFilePath { get; }

    public CacheService(string? customPath = null)
    {
        CacheFilePath = customPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeetCodeDesktopWidget",
            "cache.json");
    }

    public Task<CachedActivityData?> LoadCacheAsync(string? expectedUsername = null)
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(CacheFilePath))
                {
                    return Task.FromResult<CachedActivityData?>(null);
                }

                var json = File.ReadAllText(CacheFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Task.FromResult<CachedActivityData?>(null);
                }

                var data = JsonSerializer.Deserialize<CachedActivityData>(json, JsonOptions);
                if (data == null)
                {
                    return Task.FromResult<CachedActivityData?>(null);
                }

                // Verify username if requested
                if (!string.IsNullOrWhiteSpace(expectedUsername))
                {
                    var cleanExpected = WidgetConfig.SanitizeUsername(expectedUsername);
                    var cleanCached = WidgetConfig.SanitizeUsername(data.Username);
                    if (!string.Equals(cleanExpected, cleanCached, StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult<CachedActivityData?>(null);
                    }
                }

                return Task.FromResult<CachedActivityData?>(data);
            }
            catch
            {
                // Gracefully handle corrupt or unreadable cache file
                return Task.FromResult<CachedActivityData?>(null);
            }
        }
    }

    public Task SaveCacheAsync(LeetCodeUserStats stats)
    {
        if (stats == null || string.IsNullOrWhiteSpace(stats.Username))
        {
            return Task.CompletedTask;
        }

        lock (_fileLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(CacheFilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var cachedData = CachedActivityData.FromUserStats(stats);
                var json = JsonSerializer.Serialize(cachedData, JsonOptions);
                var tempPath = CacheFilePath + ".tmp";

                File.WriteAllText(tempPath, json);
                File.Move(tempPath, CacheFilePath, overwrite: true);
            }
            catch
            {
                // Do not crash application on cache write failure
            }
        }

        return Task.CompletedTask;
    }
}
