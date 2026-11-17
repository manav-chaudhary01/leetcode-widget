using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using LeetCodeDesktopWidget.Helpers;
using LeetCodeDesktopWidget.Models;

namespace LeetCodeDesktopWidget.Services;

/// <summary>
/// Manages persistent local JSON configuration for widget user settings.
/// Enforces test isolation and one-time verification-version migration.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly object _fileLock = new();

    public string ConfigFilePath { get; }

    public ConfigurationService(string? customPath = null)
    {
        ConfigFilePath = customPath ?? GetDefaultConfigPath();
    }

    /// <summary>
    /// Computes the default configuration path.
    /// Redirects to an isolated temporary directory if running under a test framework (xUnit/testhost).
    /// </summary>
    public static string GetDefaultConfigPath()
    {
        if (IsRunningUnderTests())
        {
            string testDir = Path.Combine(Path.GetTempPath(), "LeetCodeWidget_TestIsolation");
            Directory.CreateDirectory(testDir);
            return Path.Combine(testDir, "test_config.json");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LeetCodeDesktopWidget",
            "config.json");
    }

    /// <summary>
    /// Detects if execution is currently inside an xUnit or test runner process.
    /// </summary>
    public static bool IsRunningUnderTests()
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                string? name = asm.GetName().Name;
                if (name != null && (name.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                                     name.Contains("testhost", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Fallback safely
        }
        return false;
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

                var sanitizedUser = WidgetConfig.SanitizeUsername(config.Username);
                var sanitizedVerifiedUser = WidgetConfig.SanitizeUsername(config.VerifiedUsername);

                // ONE-TIME VERIFICATION-VERSION MIGRATION:
                // If a configuration record was created before Version 2 (missing VerificationVersion in JSON
                // or version < CurrentVerificationVersion), invalidate the unproven verification immediately
                // and persist the migrated state.
                bool isLegacy = !json.Contains("\"VerificationVersion\"", StringComparison.OrdinalIgnoreCase) ||
                                config.VerificationVersion < WidgetConfig.CurrentVerificationVersion;

                if (isLegacy && !string.IsNullOrWhiteSpace(sanitizedVerifiedUser))
                {
                    SyncLogger.Log(1, "Verification version migration",
                        $"Migrated config to version {WidgetConfig.CurrentVerificationVersion}. Invalidated unproven verification for '{sanitizedVerifiedUser}'.");

                    config = config with
                    {
                        VerificationVersion = WidgetConfig.CurrentVerificationVersion,
                        VerifiedUsername = "",
                        VerifiedAt = null,
                        VerificationChallengeCode = ""
                    };
                    sanitizedVerifiedUser = "";

                    try
                    {
                        var dir = Path.GetDirectoryName(ConfigFilePath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }
                        var updatedJson = JsonSerializer.Serialize(config, JsonOptions);
                        var tempPath = ConfigFilePath + ".tmp";
                        File.WriteAllText(tempPath, updatedJson);
                        File.Move(tempPath, ConfigFilePath, overwrite: true);
                    }
                    catch
                    {
                        // Ignore persistence failure during migration read
                    }
                }

                var preset = Enum.IsDefined(typeof(WidgetPreset), config.Preset) ? config.Preset : WidgetPreset.Standard;

                return Task.FromResult(config with
                {
                    Username = sanitizedUser,
                    VerifiedUsername = sanitizedVerifiedUser,
                    RefreshIntervalMinutes = config.RefreshIntervalMinutes <= 0 ? 0 : Math.Clamp(config.RefreshIntervalMinutes, 5, 1440),
                    Preset = preset
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

                var preset = Enum.IsDefined(typeof(WidgetPreset), config.Preset) ? config.Preset : WidgetPreset.Standard;

                var sanitized = config with
                {
                    VerificationVersion = WidgetConfig.CurrentVerificationVersion,
                    Username = WidgetConfig.SanitizeUsername(config.Username),
                    VerifiedUsername = WidgetConfig.SanitizeUsername(config.VerifiedUsername),
                    RefreshIntervalMinutes = config.RefreshIntervalMinutes <= 0 ? 0 : Math.Clamp(config.RefreshIntervalMinutes, 5, 1440),
                    Preset = preset
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
