namespace FerdiumApp.Models;

public enum ServiceLockMode
{
    None = 0,
    Passcode = 1,
    Password = 2,
    WindowsHello = 3,
}

public sealed class ServiceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string CatalogId { get; set; } = "custom";
    public string AccentColor { get; set; } = "#7367F0";
    public string Monogram { get; set; } = "W";
    public bool IsEnabled { get; set; } = true;
    public bool IsMuted { get; set; } = false;
    public bool IsHibernationEnabled { get; set; } = true;
    public string? CustomUserAgent { get; set; }

    // Startup & Hibernation lifecycle
    public bool LoadOnStartup { get; set; } = false;
    public bool HibernateOnStartup { get; set; } = false;

    // Granular Mute & Notification options
    public bool MuteNotifications { get; set; } = false;
    public bool MuteNotificationSound { get; set; } = false;
    public bool MuteAudio { get; set; } = false;
    public bool HideUnreadBadge { get; set; } = false;
    public bool HideNotificationPreview { get; set; } = false;
    public DateTime? MuteUntilUtc { get; set; }

    // Per-service Lock (Passcode / Password / Windows Hello)
    public ServiceLockMode LockMode { get; set; } = ServiceLockMode.None;
    public string? LockSecretHash { get; set; }
    public string? LockSecretSalt { get; set; }
    public bool RelockOnSwitchAway { get; set; } = false;
    public bool AutoLockEnabled { get; set; } = true;
    public int AutoLockMinutes { get; set; } = 5;
    public bool RelockOnMinimize { get; set; } = false;
    public bool RelockOnSystemSuspend { get; set; } = true;

    /// <summary>
    /// Optional shared WebView2 profile key. Null/empty = shared default profile;
    /// "isolated" = isolated per-service profile; another service's Id = reuse that service's cookies/login.
    /// </summary>
    public string? SessionProfileKey { get; set; }

    /// <summary>
    /// Optional account label to distinguish multiple accounts of the same service (e.g. "Personal", "Work").
    /// </summary>
    public string? AccountLabel { get; set; }

    /// <summary>
    /// Optional short 1-3 character corner badge shown on the sidebar icon (auto-derived from AccountLabel if empty).
    /// </summary>
    public string? AccountBadge { get; set; }

    /// <summary>
    /// Optional hex color for the account corner badge pill.
    /// </summary>
    public string? AccountBadgeColor { get; set; }

    public bool IsNotificationsEffectivelyMuted(DateTime? utcNow = null)
    {
        if (IsMuted || MuteNotifications)
        {
            return true;
        }

        if (MuteUntilUtc.HasValue)
        {
            return MuteUntilUtc.Value > (utcNow ?? DateTime.UtcNow);
        }

        return false;
    }

    public bool IsAudioEffectivelyMuted => IsMuted || MuteAudio;

    public bool HasLockConfigured => LockMode != ServiceLockMode.None;
}
