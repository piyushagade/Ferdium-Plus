namespace FerdiumApp.Models;

public enum AppThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2,
}

public sealed class AppSettings
{
    // ==================== GENERAL ====================
    public bool AutoLaunchOnStart { get; set; } = false;
    public bool RunInBackground { get; set; } = true;
    public bool ConfirmOnQuit { get; set; } = false;
    public bool EnableSystemTray { get; set; } = true;
    public bool ReloadAfterResume { get; set; } = true;
    public int ReloadAfterResumeTime { get; set; } = 10;
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToSystemTray { get; set; } = false;
    public bool CloseToSystemTray { get; set; } = true;
    public bool KeepAllWorkspacesLoaded { get; set; } = false;
    public bool EnableTodos { get; set; } = false;
    public string PredefinedTodoServer { get; set; } = "https://app.franztodos.com";
    public string CustomTodoServer { get; set; } = string.Empty;
    public bool ScheduledDndEnabled { get; set; } = false;
    public string ScheduledDndStart { get; set; } = "17:00";
    public string ScheduledDndEnd { get; set; } = "09:00";

    // ==================== SERVICES & HIBERNATION ====================
    public bool ShowDisabledServices { get; set; } = true;
    public bool ShowServiceNames { get; set; } = false;
    public bool UseGrayscaleServices { get; set; } = false;
    public int GrayscaleServicesDim { get; set; } = 50;
    public bool ShowMessageBadgeWhenMuted { get; set; } = true;
    public bool EnableLongPressServiceHint { get; set; } = false;
    public int IconSize { get; set; } = 20; // 0, 10, 20, 30, 40
    public string NavigationBarBehaviour { get; set; } = "always"; // custom, always, never
    public bool LazyLoadServices { get; set; } = true;
    public bool SuspendBackgroundWebViews { get; set; } = false;
    public bool TrimMemoryOnHibernate { get; set; } = true;
    public bool HibernateOnStartup { get; set; } = true;
    public int HibernationStrategySeconds { get; set; } = 900; // 15 min default (matches AutoHibernateMinutes)
    public int AutoHibernateMinutes { get; set; } = 15;
    public int WakeUpStrategySeconds { get; set; } = 300;
    public int WakeUpHibernationStrategySeconds { get; set; } = 0;
    public bool WakeUpHibernationSplay { get; set; } = true;
    public bool SandboxServices { get; set; } = true;

    // ==================== APPEARANCE ====================
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;
    public bool UseMica { get; set; } = true;
    public bool UniversalDarkMode { get; set; } = true;
    public bool SplitMode { get; set; } = false;
    public int SplitColumns { get; set; } = 3;
    public string AccentColor { get; set; } = "#7367F0";
    public string ProgressbarAccentColor { get; set; } = "#7367F0";
    public int ServiceRibbonWidth { get; set; } = 68; // 35, 45, 55, 68, 80, 90, 100
    public int WebviewPaddingSize { get; set; } = 0;
    public int ServiceWebviewBorderRadius { get; set; } = 8;
    public string SidebarServicesLocation { get; set; } = "topLeft"; // topLeft, center, bottomRight
    public bool UseHorizontalStyle { get; set; } = false;
    public bool HideCollapseButton { get; set; } = false;
    public bool HideRecipesButton { get; set; } = false;
    public bool HideSplitModeButton { get; set; } = true;
    public bool HideWorkspacesButton { get; set; } = false;
    public bool HideNotificationsButton { get; set; } = false;
    public bool HideSettingsButton { get; set; } = false;
    public bool HideDownloadButton { get; set; } = false;
    public bool AlwaysShowWorkspaces { get; set; } = false;
    public bool UseCompactWorkspaceDrawer { get; set; } = false;
    public bool HideAllServicesWorkspace { get; set; } = false;

    // ==================== PRIVACY ====================
    public bool PrivateNotifications { get; set; } = false;
    public bool ClipboardNotifications { get; set; } = true;
    public bool NotifyTaskBarOnMessage { get; set; } = false;
    public bool IsTwoFactorAutoCatcherEnabled { get; set; } = false;
    public string TwoFactorAutoCatcherMatcher { get; set; } = "token, code, sms, verify";
    public string WebRtcIpHandlingPolicy { get; set; } = "disable_non_proxied_udp";
    public bool Sentry { get; set; } = false;
    public string SearchEngine { get; set; } = "startPage"; // startPage, google, duckDuckGo, bing
    public bool IsLockingFeatureEnabled { get; set; } = false;
    public string LockedPassword { get; set; } = string.Empty;
    public int InactivityLockMinutes { get; set; } = 0;

    // ==================== LANGUAGE ====================
    public string Locale { get; set; } = "en-US";
    public bool EnableSpellchecking { get; set; } = true;
    public string SpellcheckerLanguage { get; set; } = "en-us";
    public bool EnableTranslator { get; set; } = false;
    public string TranslatorEngine { get; set; } = "LibreTranslate";
    public string TranslatorLanguage { get; set; } = "en";

    // ==================== ADVANCED ====================
    public bool EnableGpuAcceleration { get; set; } = true;
    public bool EnableGlobalHideShortcut { get; set; } = false;
    public bool UseSelfSignedCertificates { get; set; } = false;
    public string DownloadFolderPath { get; set; } = string.Empty;
    public string UserAgentPref { get; set; } = string.Empty;
    public string ShortcutActivateNextService { get; set; } = "Ctrl+Tab";
    public string ShortcutActivatePreviousService { get; set; } = "Ctrl+Shift+Tab";
    public bool ActivateServiceUsesAlt { get; set; } = false;

    // ==================== UPDATES ====================
    public bool AutomaticUpdates { get; set; } = true;
    public bool BetaUpdates { get; set; } = false;

    // ==================== LEGACY / SHELL COMPATIBILITY ====================
    public bool MinimizeToTray
    {
        get => CloseToSystemTray;
        set => CloseToSystemTray = value;
    }

    public bool IsMuted { get; set; } = false;

    public bool ShowWebControls
    {
        get => !string.Equals(NavigationBarBehaviour, "never", StringComparison.OrdinalIgnoreCase);
        set => NavigationBarBehaviour = value ? "always" : "never";
    }
}
