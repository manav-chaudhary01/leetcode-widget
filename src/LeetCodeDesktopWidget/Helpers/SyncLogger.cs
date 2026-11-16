using System;
using System.IO;

namespace LeetCodeDesktopWidget.Helpers;

/// <summary>
/// Structured diagnostic logging for tracing all 18 stages of LeetCode synchronization.
/// Never logs sensitive tokens, credentials, or secrets.
/// </summary>
public static class SyncLogger
{
    private static readonly object _lock = new();

    public static void Log(int stage, string description, string? details = null)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LeetCodeDesktopWidget");
            Directory.CreateDirectory(dir);
            string logPath = Path.Combine(dir, "sync_diagnostic.log");

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Stage {stage:D2}] {description}" +
                          (string.IsNullOrEmpty(details) ? "" : $" | {details}");

            lock (_lock)
            {
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Do not fail if logging fails
        }
    }
}
