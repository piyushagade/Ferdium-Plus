using System.Globalization;
using System.IO;
using System.Text.Json;
using FerdiumApp.Models;

namespace FerdiumApp.Services;

public sealed record FerdiumImportResult(bool Success, int ImportedCount, string Message);

/// <summary>
/// Imports settings from an Electron Ferdium installation's config/settings.json
/// (and optional shortcuts.json) into FerdiumPlus's AppSettings model.
/// </summary>
public static class FerdiumSettingsImporter
{
    public static string GetDefaultFerdiumSettingsPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Ferdium", "config", "settings.json");
    }

    public static FerdiumImportResult ImportFromFile(string settingsFilePath, AppSettings target)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath) || !File.Exists(settingsFilePath))
        {
            return new FerdiumImportResult(
                false,
                0,
                $"Ferdium settings file not found at: {settingsFilePath}");
        }

        try
        {
            var json = File.ReadAllText(settingsFilePath);
            var importedCount = ApplyFerdiumJson(json, target);

            // Also check for sibling shortcuts.json in the same config directory
            var dir = Path.GetDirectoryName(settingsFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                var shortcutsPath = Path.Combine(dir, "shortcuts.json");
                if (File.Exists(shortcutsPath))
                {
                    importedCount += ApplyFerdiumShortcutsJson(File.ReadAllText(shortcutsPath), target);
                }
            }

            return new FerdiumImportResult(
                true,
                importedCount,
                $"Imported {importedCount} settings from {Path.GetFileName(settingsFilePath)}.");
        }
        catch (Exception ex)
        {
            return new FerdiumImportResult(false, 0, $"Failed to import settings: {ex.Message}");
        }
    }

    public static int ApplyFerdiumJson(string json, AppSettings target)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var root = doc.RootElement;
        var count = 0;

        bool TryBool(string key, Action<bool> setter)
        {
            if (root.TryGetProperty(key, out var el) &&
                (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False))
            {
                setter(el.GetBoolean());
                count++;
                return true;
            }
            return false;
        }

        bool TryInt(string key, Action<int> setter)
        {
            if (root.TryGetProperty(key, out var el))
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
                {
                    setter(n);
                    count++;
                    return true;
                }
                if (el.ValueKind == JsonValueKind.String &&
                    int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    setter(parsed);
                    count++;
                    return true;
                }
            }
            return false;
        }

        bool TryString(string key, Action<string> setter)
        {
            if (root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
            {
                var val = el.GetString();
                if (val is not null)
                {
                    setter(val);
                    count++;
                    return true;
                }
            }
            return false;
        }

        // General
        TryBool("autoLaunchOnStart", v => target.AutoLaunchOnStart = v);
        TryBool("runInBackground", v => target.RunInBackground = v);
        TryBool("confirmOnQuit", v => target.ConfirmOnQuit = v);
        TryBool("enableSystemTray", v => target.EnableSystemTray = v);
        TryBool("reloadAfterResume", v => target.ReloadAfterResume = v);
        TryInt("reloadAfterResumeTime", v => target.ReloadAfterResumeTime = Math.Clamp(v, 1, 1440));
        TryBool("startMinimized", v => target.StartMinimized = v);
        TryBool("minimizeToSystemTray", v => target.MinimizeToSystemTray = v);
        TryBool("closeToSystemTray", v => target.CloseToSystemTray = v);
        TryBool("keepAllWorkspacesLoaded", v => target.KeepAllWorkspacesLoaded = v);
        TryBool("isTodosFeatureEnabled", v => target.EnableTodos = v);
        TryBool("enableTodos", v => target.EnableTodos = v);
        TryString("predefinedTodoServer", v => target.PredefinedTodoServer = v);
        TryString("customTodoServer", v => target.CustomTodoServer = v);
        TryBool("scheduledDNDEnabled", v => target.ScheduledDndEnabled = v);
        TryString("scheduledDNDStart", v => target.ScheduledDndStart = v);
        TryString("scheduledDNDEnd", v => target.ScheduledDndEnd = v);
        TryBool("isAppMuted", v => target.IsMuted = v);

        // Services & Hibernation
        TryBool("showDisabledServices", v => target.ShowDisabledServices = v);
        TryBool("showServiceName", v => target.ShowServiceNames = v);
        TryBool("useGrayscaleServices", v => target.UseGrayscaleServices = v);
        TryInt("grayscaleServicesDim", v => target.GrayscaleServicesDim = Math.Clamp(v, 0, 100));
        TryBool("showMessageBadgeWhenMuted", v => target.ShowMessageBadgeWhenMuted = v);
        TryBool("enableLongPressServiceHint", v => target.EnableLongPressServiceHint = v);
        TryInt("iconSize", v => target.IconSize = Math.Clamp(v, 0, 40));
        TryString("navigationBarBehaviour", v => target.NavigationBarBehaviour = v);
        TryBool("lazyLoadServices", v => target.LazyLoadServices = v);
        TryBool("throttleBackgroundWebviews", v => target.SuspendBackgroundWebViews = v);
        TryBool("hibernateOnStartup", v => target.HibernateOnStartup = v);
        if (TryInt("hibernationStrategy", v =>
        {
            target.HibernationStrategySeconds = Math.Clamp(v, 10, 86400);
            target.AutoHibernateMinutes = Math.Max(1, (int)Math.Round(target.HibernationStrategySeconds / 60.0));
        }))
        {
            // synced
        }
        TryInt("wakeUpStrategy", v => target.WakeUpStrategySeconds = Math.Clamp(v, 0, 86400));
        TryInt("wakeUpHibernationStrategy", v => target.WakeUpHibernationStrategySeconds = Math.Clamp(v, 0, 86400));
        TryBool("wakeUpHibernationSplay", v => target.WakeUpHibernationSplay = v);
        TryBool("sandboxServices", v => target.SandboxServices = v);

        // Appearance
        TryBool("useMicaMaterial", v => target.UseMica = v);
        bool? adaptableDark = null;
        bool? darkMode = null;
        if (root.TryGetProperty("adaptableDarkMode", out var adm) &&
            (adm.ValueKind == JsonValueKind.True || adm.ValueKind == JsonValueKind.False))
        {
            adaptableDark = adm.GetBoolean();
            count++;
        }
        if (root.TryGetProperty("darkMode", out var dm) &&
            (dm.ValueKind == JsonValueKind.True || dm.ValueKind == JsonValueKind.False))
        {
            darkMode = dm.GetBoolean();
            count++;
        }
        if (adaptableDark.HasValue || darkMode.HasValue)
        {
            if (adaptableDark == true)
            {
                target.ThemeMode = AppThemeMode.System;
            }
            else if (darkMode == true)
            {
                target.ThemeMode = AppThemeMode.Dark;
            }
            else if (darkMode == false)
            {
                target.ThemeMode = AppThemeMode.Light;
            }
        }
        TryBool("universalDarkMode", v => target.UniversalDarkMode = v);
        TryBool("splitMode", v => target.SplitMode = v);
        TryInt("splitColumns", v => target.SplitColumns = Math.Clamp(v, 1, 5));
        TryString("accentColor", v => { if (!string.IsNullOrWhiteSpace(v)) target.AccentColor = v; });
        TryString("progressbarAccentColor", v => { if (!string.IsNullOrWhiteSpace(v)) target.ProgressbarAccentColor = v; });
        TryInt("serviceRibbonWidth", v => target.ServiceRibbonWidth = Math.Clamp(v, 35, 120));
        TryInt("webviewPaddingSize", v => target.WebviewPaddingSize = Math.Clamp(v, 0, 32));
        TryInt("serviceWebviewBorderRadius", v => target.ServiceWebviewBorderRadius = Math.Clamp(v, 0, 24));
        TryString("sidebarServicesLocation", v => target.SidebarServicesLocation = v);
        TryBool("useHorizontalStyle", v => target.UseHorizontalStyle = v);
        TryBool("hideCollapseButton", v => target.HideCollapseButton = v);
        TryBool("hideRecipesButton", v => target.HideRecipesButton = v);
        TryBool("hideSplitModeButton", v => target.HideSplitModeButton = v);
        TryBool("hideWorkspacesButton", v => target.HideWorkspacesButton = v);
        TryBool("hideNotificationsButton", v => target.HideNotificationsButton = v);
        TryBool("hideSettingsButton", v => target.HideSettingsButton = v);
        TryBool("hideDownloadButton", v => target.HideDownloadButton = v);
        TryBool("alwaysShowWorkspaces", v => target.AlwaysShowWorkspaces = v);
        TryBool("useCompactWorkspaceDrawer", v => target.UseCompactWorkspaceDrawer = v);
        TryBool("hideAllServicesWorkspace", v => target.HideAllServicesWorkspace = v);

        // Privacy
        TryBool("privateNotifications", v => target.PrivateNotifications = v);
        TryBool("clipboardNotifications", v => target.ClipboardNotifications = v);
        TryBool("notifyTaskBarOnMessage", v => target.NotifyTaskBarOnMessage = v);
        TryBool("isTwoFactorAutoCatcherEnabled", v => target.IsTwoFactorAutoCatcherEnabled = v);
        TryString("twoFactorAutoCatcherMatcher", v => target.TwoFactorAutoCatcherMatcher = v);
        TryString("webRTCIPHandlingPolicy", v => target.WebRtcIpHandlingPolicy = v);
        TryBool("sentry", v => target.Sentry = v);
        TryString("searchEngine", v => target.SearchEngine = v);
        TryBool("isLockingFeatureEnabled", v => target.IsLockingFeatureEnabled = v);
        TryInt("inactivityLock", v => target.InactivityLockMinutes = Math.Clamp(v, 0, 1440));

        // Language
        TryString("locale", v => target.Locale = v);
        TryBool("enableSpellchecking", v => target.EnableSpellchecking = v);
        TryString("spellcheckerLanguage", v => target.SpellcheckerLanguage = v);
        TryBool("enableTranslator", v => target.EnableTranslator = v);
        TryString("translatorEngine", v => target.TranslatorEngine = v);
        TryString("translatorLanguage", v => target.TranslatorLanguage = v);

        // Advanced
        TryBool("enableGPUAcceleration", v => target.EnableGpuAcceleration = v);
        TryBool("enableGlobalHideShortcut", v => target.EnableGlobalHideShortcut = v);
        TryBool("useSelfSignedCertificates", v => target.UseSelfSignedCertificates = v);
        TryString("downloadFolderPath", v => target.DownloadFolderPath = v);
        TryString("userAgentPref", v => target.UserAgentPref = v);
        TryString("shortcutActivateNextService", v => target.ShortcutActivateNextService = v);
        TryString("shortcutActivatePreviousService", v => target.ShortcutActivatePreviousService = v);
        TryBool("activateServiceUsesAlt", v => target.ActivateServiceUsesAlt = v);

        // Updates
        TryBool("automaticUpdates", v => target.AutomaticUpdates = v);
        TryBool("beta", v => target.BetaUpdates = v);

        return count;
    }

    public static int ApplyFerdiumShortcutsJson(string json, AppSettings target)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var root = doc.RootElement;
        var count = 0;
        if (root.TryGetProperty("activateNextService", out var next) && next.ValueKind == JsonValueKind.String)
        {
            target.ShortcutActivateNextService = next.GetString() ?? target.ShortcutActivateNextService;
            count++;
        }
        if (root.TryGetProperty("activatePreviousService", out var prev) && prev.ValueKind == JsonValueKind.String)
        {
            target.ShortcutActivatePreviousService = prev.GetString() ?? target.ShortcutActivatePreviousService;
            count++;
        }
        if (root.TryGetProperty("activateServiceUsesAlt", out var alt) &&
            (alt.ValueKind == JsonValueKind.True || alt.ValueKind == JsonValueKind.False))
        {
            target.ActivateServiceUsesAlt = alt.GetBoolean();
            count++;
        }
        return count;
    }

    /// <summary>
    /// Evaluates whether the current time falls within the configured Scheduled Do-Not-Disturb window.
    /// Supports overnight windows where start > end (e.g. 17:00 to 09:00).
    /// </summary>
    public static bool IsWithinScheduledDnd(bool enabled, string startText, string endText, TimeSpan nowTimeOfDay)
    {
        if (!enabled)
        {
            return false;
        }

        if (!TimeSpan.TryParseExact(startText, @"hh\:mm", CultureInfo.InvariantCulture, out var start) &&
            !TimeSpan.TryParse(startText, CultureInfo.InvariantCulture, out start))
        {
            return false;
        }

        if (!TimeSpan.TryParseExact(endText, @"hh\:mm", CultureInfo.InvariantCulture, out var end) &&
            !TimeSpan.TryParse(endText, CultureInfo.InvariantCulture, out end))
        {
            return false;
        }

        if (start == end)
        {
            return false;
        }

        return start < end
            ? nowTimeOfDay >= start && nowTimeOfDay < end
            : nowTimeOfDay >= start || nowTimeOfDay < end;
    }
}
