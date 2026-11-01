using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

/// <summary>
/// Manages persistent local JSON configuration for widget user settings.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly object _fileLock = new();

    public string ConfigFilePath { get; }

    public ConfigurationService(string? customPath = null)
    {
        ConfigFilePath = customPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeetCodeDesktopWidget",
            "config.json");
    }

    public Task<WidgetConfig> LoadConfigurationAsync()
    {
        lock (_fileLock)
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    return Task.FromResult(WidgetConfig.Default);
                }

                var json = File.ReadAllText(ConfigFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return Task.FromResult(WidgetConfig.Default);
                }

                var config = JsonSerializer.Deserialize<WidgetConfig>(json, JsonOptions);
                if (config == null)
                {
                    return Task.FromResult(WidgetConfig.Default);
                }

                // Sanitize username
                var sanitizedUser = WidgetConfig.SanitizeUsername(config.Username);
                return Task.FromResult(config with
                {
                    Username = sanitizedUser,
                    RefreshIntervalMinutes = Math.Clamp(config.RefreshIntervalMinutes, 5, 1440)
                });
            }
            catch
            {
                // Fail gracefully on corrupt/unreadable file
                return Task.FromResult(WidgetConfig.Default);
            }
        }
    }

    public Task SaveConfigurationAsync(WidgetConfig config)
    {
        if (config == null) return Task.CompletedTask;

        lock (_fileLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigFilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var sanitized = config with
                {
                    Username = WidgetConfig.SanitizeUsername(config.Username),
                    RefreshIntervalMinutes = Math.Clamp(config.RefreshIntervalMinutes, 5, 1440)
                };

                var json = JsonSerializer.Serialize(sanitized, JsonOptions);
                var tempPath = ConfigFilePath + ".tmp";

                File.WriteAllText(tempPath, json);
                File.Move(tempPath, ConfigFilePath, overwrite: true);
            }
            catch
            {
                // Gracefully fail without crashing
            }
        }

        return Task.CompletedTask;
    }
}
