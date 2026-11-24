using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FerdiumApp.Models;
using FerdiumApp.Services;

namespace FerdiumApp.ViewModels;

public sealed partial class ServiceItemViewModel : ObservableObject
{
    public ServiceConfig Config { get; }

    public string Id => Config.Id;
    public string Name => Config.Name;
    public string Url => Config.Url;
    public string Monogram => Config.Monogram;

    public string AccountLabel => Config.AccountLabel ?? string.Empty;
    public bool HasAccountLabel => !string.IsNullOrWhiteSpace(Config.AccountLabel);
    public string DisplayName => HasAccountLabel ? $"{Config.Name} ({Config.AccountLabel})" : Config.Name;
    public string AccountBadgeText => ComputeAccountBadge(Config.AccountBadge, Config.AccountLabel);
    public bool HasAccountBadge => !string.IsNullOrEmpty(AccountBadgeText);
    public Brush AccountBadgeBrush => ParseBrush(string.IsNullOrWhiteSpace(Config.AccountBadgeColor) ? "#0078D4" : Config.AccountBadgeColor!);

    public Brush AccentBrush { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private ImageSource? _iconImage;

    public bool HasIcon => IconImage is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(IsHibernated))]
    [NotifyPropertyChangedFor(nameof(HibernateMenuLabel))]
    private bool _isLoaded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private bool _isSuspended;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(MuteMenuLabel))]
    private bool _isMuted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    private int _unreadCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIndirectDot))]
    private bool _hasIndirectActivity;

    [ObservableProperty]
    private string _currentUrl;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DragOpacity))]
    private bool _isDragging;

    [ObservableProperty]
    private bool _isDropTargetBefore;

    [ObservableProperty]
    private bool _isDropTargetAfter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSharedSession))]
    private string _sessionBadgeLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(CanLockNow))]
    private bool _isLocked;

    public bool HasLockConfigured => Config.HasLockConfigured;
    public bool CanLockNow => HasLockConfigured && !IsLocked;
    public bool HasSharedSession => !string.IsNullOrEmpty(SessionBadgeLabel);

    public double DragOpacity => IsDragging ? 0.45 : (Config.IsEnabled ? 1.0 : 0.42);

    public bool IsEnabled
    {
        get => Config.IsEnabled;
        set
        {
            if (Config.IsEnabled == value) return;
            Config.IsEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DragOpacity));
            OnPropertyChanged(nameof(EnableMenuLabel));
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public string EnableMenuLabel => Config.IsEnabled ? "Disable Service" : "Enable Service";

    public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastHibernatedUtc { get; set; }
    public DateTime? WokenInBackgroundUtc { get; set; }

    public bool IsHibernated => !IsLoaded;
    public bool HasBadge => !Config.HideUnreadBadge && UnreadCount > 0;
    public bool HasIndirectDot => !Config.HideUnreadBadge && UnreadCount == 0 && HasIndirectActivity;
    public string BadgeText => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

    public string HibernateMenuLabel => IsLoaded ? "Hibernate (Free RAM)" : "Wake Up";
    public string MuteMenuLabel => IsMuted ? "Unmute" : "Mute";

    public string StatusLabel
    {
        get
        {
            if (!Config.IsEnabled)
            {
                return "Disabled";
            }

            if (IsLocked)
            {
                return "Locked";
            }

            var baseStatus = IsActive
                ? "Active"
                : !IsLoaded
                    ? "Hibernated (0 MB)"
                    : IsSuspended
                        ? "Suspended (low CPU)"
                        : "Loaded";
            return (IsMuted || Config.IsNotificationsEffectivelyMuted()) ? $"{baseStatus} • Muted" : baseStatus;
        }
    }

    public ServiceItemViewModel(ServiceConfig config)
    {
        Config = config;
        _isMuted = config.IsMuted;
        _isLocked = config.HasLockConfigured;
        _currentUrl = config.Url;
        AccentBrush = ParseBrush(config.AccentColor);
        _iconImage = ServiceIconService.TryGetBundledIcon(config.CatalogId, config.Url);
        if (_iconImage is null)
        {
            _ = LoadIconAsync();
        }
    }

    public void RefreshMetadataFromConfig()
    {
        IsMuted = Config.IsMuted;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Url));
        OnPropertyChanged(nameof(Monogram));
        OnPropertyChanged(nameof(AccountLabel));
        OnPropertyChanged(nameof(HasAccountLabel));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(AccountBadgeText));
        OnPropertyChanged(nameof(HasAccountBadge));
        OnPropertyChanged(nameof(AccountBadgeBrush));
        OnPropertyChanged(nameof(HasBadge));
        OnPropertyChanged(nameof(HasIndirectDot));
        OnPropertyChanged(nameof(HasLockConfigured));
        OnPropertyChanged(nameof(CanLockNow));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(MuteMenuLabel));
    }

    public static string ComputeAccountBadge(string? explicitBadge, string? accountLabel)
    {
        if (!string.IsNullOrWhiteSpace(explicitBadge))
        {
            var clean = explicitBadge.Trim();
            return (clean.Length > 3 ? clean[..3] : clean).ToUpperInvariant();
        }

        if (string.IsNullOrWhiteSpace(accountLabel))
        {
            return string.Empty;
        }

        var trimmed = accountLabel.Trim().TrimStart('@');
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        var words = trimmed.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
        {
            return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
        }

        return char.ToUpperInvariant(trimmed[0]).ToString();
    }

    private async Task LoadIconAsync()
    {
        var icon = await ServiceIconService.GetIconAsync(Config.CatalogId, Config.Url);
        if (icon is not null)
        {
            IconImage = icon;
        }
    }

    private static Brush ParseBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            var fallback = new SolidColorBrush(Color.FromRgb(115, 103, 240));
            fallback.Freeze();
            return fallback;
        }
    }
}
