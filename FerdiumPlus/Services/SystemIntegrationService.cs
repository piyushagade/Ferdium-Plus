using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace FerdiumApp.Services;

/// <summary>
/// Handles Windows OS integration for FerdiumPlus settings:
/// auto-launch on Windows startup, profile folder access, and cache size calculation.
/// </summary>
public static class SystemIntegrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRunValueName = "FerdiumWpf";

    public static string GetProfileFolderPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "FerdiumPlus");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static string GetWebViewProfilesRootPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(localAppData, "FerdiumPlus", "Profiles");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static void SetAutoLaunchOnStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key is null) return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    key.SetValue(AppRunValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(AppRunValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Registry access may be restricted in sandboxed environments
        }
    }

    public static void OpenFolderInExplorer(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Ignore shell launch errors
        }
    }

    public static long GetDirectorySizeBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0;
        }

        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // Skip locked files
                }
            }
        }
        catch
        {
            // Ignore enumeration errors
        }

        return total;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F1} MB";
        return $"{mb / 1024.0:F2} GB";
    }
}
