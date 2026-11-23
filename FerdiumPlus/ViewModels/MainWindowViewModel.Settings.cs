using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FerdiumApp.Models;
using FerdiumApp.Services;

namespace FerdiumApp.ViewModels;

public sealed record SettingOption<T>(T Value, string Label);

public sealed partial class MainWindowViewModel
{
    public event Action<AppSettings>? GlobalSettingsChanged;
    public event Action<bool>? TrayVisibilityChanged;
    public event Func<Task>? ClearAllCacheRequested;
    public event Action? BrowseDownloadFolderRequested;
    public event Action? ImportFerdiumFileRequested;

    [ObservableProperty]
    private string _activeSettingsTab = "General";

    [ObservableProperty]
    private string _settingsImportStatusText = string.Empty;

    [ObservableProperty]
    private string _cacheSizeText = "Calculating...";

    [ObservableProperty]
    private string _updateStatusText = "You are using the latest version of Ferdium.";

    public string AppVersionText =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public AppSettings Settings => _state.Settings;

    // ==================== OPTION LISTS ====================
    public IReadOnlyList<SettingOption<string>> PredefinedTodoServerOptions { get; } = new[]
    {
        new SettingOption<string>("https://app.franztodos.com", "Ferdium Todos (Default)"),
        new SettingOption<string>("https://todoist.com/app", "Todoist"),
        new SettingOption<string>("https://tasks.google.com/embed/?origin=https://calendar.google.com&fullWidth=1", "Google Tasks"),
        new SettingOption<string>("https://to-do.live.com/tasks", "Microsoft To Do"),
        new SettingOption<string>("https://ticktick.com/webapp", "TickTick"),
        new SettingOption<string>("isUsingCustomTodoService", "Custom Todo Server"),
    };

    public IReadOnlyList<SettingOption<int>> IconSizeOptions { get; } = new[]
    {
        new SettingOption<int>(0, "Very small icons"),
        new SettingOption<int>(10, "Small icons"),
        new SettingOption<int>(20, "Normal icons"),
        new SettingOption<int>(30, "Large icons"),
        new SettingOption<int>(40, "Very large icons"),
    };

    public IReadOnlyList<SettingOption<string>> NavigationBarBehaviourOptions { get; } = new[]
    {
        new SettingOption<string>("always", "Show navigation bar on all services"),
        new SettingOption<string>("custom", "Show navigation bar on custom websites only"),
        new SettingOption<string>("never", "Never show navigation bar"),
    };

    public IReadOnlyList<SettingOption<int>> HibernationStrategyOptions { get; } = new[]
    {
        new SettingOption<int>(10, "Extremely Fast Hibernation (10sec)"),
        new SettingOption<int>(30, "Very Fast Hibernation (30sec)"),
        new SettingOption<int>(60, "Fast Hibernation (1min)"),
        new SettingOption<int>(300, "Normal Hibernation (5min)"),
        new SettingOption<int>(600, "Slow Hibernation (10min)"),
        new SettingOption<int>(900, "Balanced Hibernation (15min)"),
        new SettingOption<int>(1800, "Very Slow Hibernation (30min)"),
        new SettingOption<int>(3600, "Extremely Slow Hibernation (1hour)"),
    };

    public IReadOnlyList<SettingOption<int>> WakeUpStrategyOptions { get; } = new[]
    {
        new SettingOption<int>(0, "Never wake up"),
        new SettingOption<int>(10, "Wake up after 10sec"),
        new SettingOption<int>(30, "Wake up after 30sec"),
        new SettingOption<int>(60, "Wake up after 1min"),
        new SettingOption<int>(300, "Wake up after 5min"),
        new SettingOption<int>(600, "Wake up after 10min"),
        new SettingOption<int>(1800, "Wake up after 30min"),
        new SettingOption<int>(3600, "Wake up after 1hour"),
    };

    public IReadOnlyList<SettingOption<int>> WakeUpHibernationStrategyOptions { get; } = new[]
    {
        new SettingOption<int>(0, "Use main hibernation strategy"),
        new SettingOption<int>(10, "Extremely Fast Hibernation (10sec)"),
        new SettingOption<int>(30, "Very Fast Hibernation (30sec)"),
        new SettingOption<int>(60, "Fast Hibernation (1min)"),
        new SettingOption<int>(300, "Normal Hibernation (5min)"),
        new SettingOption<int>(600, "Slow Hibernation (10min)"),
        new SettingOption<int>(1800, "Very Slow Hibernation (30min)"),
        new SettingOption<int>(3600, "Extremely Slow Hibernation (1hour)"),
    };

    public IReadOnlyList<SettingOption<int>> SidebarWidthOptions { get; } = new[]
    {
        new SettingOption<int>(35, "Extremely slim sidebar (35px)"),
        new SettingOption<int>(45, "Very slim sidebar (45px)"),
        new SettingOption<int>(55, "Slim sidebar (55px)"),
        new SettingOption<int>(68, "Normal sidebar (68px)"),
        new SettingOption<int>(80, "Wide sidebar (80px)"),
        new SettingOption<int>(90, "Very wide sidebar (90px)"),
        new SettingOption<int>(100, "Extremely wide sidebar (100px)"),
    };

    public IReadOnlyList<SettingOption<int>> WebviewPaddingOptions { get; } = new[]
    {
        new SettingOption<int>(0, "None (0px)"),
        new SettingOption<int>(4, "Small (4px)"),
        new SettingOption<int>(8, "Medium (8px)"),
        new SettingOption<int>(12, "Large (12px)"),
        new SettingOption<int>(16, "Extra Large (16px)"),
    };

    public IReadOnlyList<SettingOption<int>> WebviewBorderRadiusOptions { get; } = new[]
    {
        new SettingOption<int>(0, "Sharp (0px)"),
        new SettingOption<int>(4, "Subtle (4px)"),
        new SettingOption<int>(8, "Standard (8px)"),
        new SettingOption<int>(12, "Rounded (12px)"),
        new SettingOption<int>(16, "Extra Rounded (16px)"),
    };

    public IReadOnlyList<SettingOption<string>> SidebarServicesLocationOptions { get; } = new[]
    {
        new SettingOption<string>("topLeft", "Top / Left"),
        new SettingOption<string>("center", "Center"),
        new SettingOption<string>("bottomRight", "Bottom / Right"),
    };

    public IReadOnlyList<SettingOption<string>> WebRtcPolicyOptions { get; } = new[]
    {
        new SettingOption<string>("default", "Expose user public and local IPs"),
        new SettingOption<string>("default_public_interface_only", "Expose user public IP only"),
        new SettingOption<string>("default_public_and_private_interfaces", "Expose public & local IPs (default HTTP route)"),
        new SettingOption<string>("disable_non_proxied_udp", "Do not expose public or local IPs"),
    };

    public IReadOnlyList<SettingOption<string>> SearchEngineOptions { get; } = new[]
    {
        new SettingOption<string>("startPage", "Startpage"),
        new SettingOption<string>("google", "Google"),
        new SettingOption<string>("duckDuckGo", "DuckDuckGo"),
        new SettingOption<string>("bing", "Bing"),
    };

    public IReadOnlyList<SettingOption<string>> LocaleOptions { get; } = new[]
    {
        new SettingOption<string>("en-US", "English (United States)"),
        new SettingOption<string>("en-GB", "English (United Kingdom)"),
        new SettingOption<string>("de-DE", "Deutsch (German)"),
        new SettingOption<string>("es-ES", "Español (Spanish)"),
        new SettingOption<string>("fr-FR", "Français (French)"),
        new SettingOption<string>("it-IT", "Italiano (Italian)"),
        new SettingOption<string>("pt-BR", "Português (Brasil)"),
        new SettingOption<string>("nl-NL", "Nederlands (Dutch)"),
        new SettingOption<string>("pl-PL", "Polski (Polish)"),
        new SettingOption<string>("ru-RU", "Русский (Russian)"),
        new SettingOption<string>("ja-JP", "日本語 (Japanese)"),
        new SettingOption<string>("zh-CN", "中文 (Simplified Chinese)"),
        new SettingOption<string>("hi-IN", "हिन्दी (Hindi)"),
    };

    public IReadOnlyList<SettingOption<string>> SpellcheckerLanguageOptions { get; } = new[]
    {
        new SettingOption<string>("en-us", "English (US)"),
        new SettingOption<string>("en-gb", "English (UK)"),
        new SettingOption<string>("de-de", "German"),
        new SettingOption<string>("es-es", "Spanish"),
        new SettingOption<string>("fr-fr", "French"),
        new SettingOption<string>("it-it", "Italian"),
        new SettingOption<string>("pt-br", "Portuguese (Brazil)"),
        new SettingOption<string>("nl-nl", "Dutch"),
    };

    public IReadOnlyList<SettingOption<string>> TranslatorEngineOptions { get; } = new[]
    {
        new SettingOption<string>("LibreTranslate", "Ferdium Translator (LibreTranslate)"),
        new SettingOption<string>("Google", "Google Translate"),
    };

    public IReadOnlyList<SettingOption<string>> TranslatorLanguageOptions { get; } = new[]
    {
        new SettingOption<string>("en", "English"),
        new SettingOption<string>("ar", "Arabic"),
        new SettingOption<string>("zh", "Chinese"),
        new SettingOption<string>("fr", "French"),
        new SettingOption<string>("de", "German"),
        new SettingOption<string>("hi", "Hindi"),
        new SettingOption<string>("it", "Italian"),
        new SettingOption<string>("ja", "Japanese"),
        new SettingOption<string>("pt", "Portuguese"),
        new SettingOption<string>("ru", "Russian"),
        new SettingOption<string>("es", "Spanish"),
    };

    // ==================== GENERAL SETTINGS ====================
    public bool AutoLaunchOnStart
    {
        get => _state.Settings.AutoLaunchOnStart;
        set
        {
            if (_state.Settings.AutoLaunchOnStart == value) return;
            _state.Settings.AutoLaunchOnStart = value;
            SystemIntegrationService.SetAutoLaunchOnStart(value);
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool RunInBackground
    {
        get => _state.Settings.RunInBackground;
        set
        {
            if (_state.Settings.RunInBackground == value) return;
            _state.Settings.RunInBackground = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool ConfirmOnQuit
    {
        get => _state.Settings.ConfirmOnQuit;
        set
        {
            if (_state.Settings.ConfirmOnQuit == value) return;
            _state.Settings.ConfirmOnQuit = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool EnableSystemTray
    {
        get => _state.Settings.EnableSystemTray;
        set
        {
            if (_state.Settings.EnableSystemTray == value) return;
            _state.Settings.EnableSystemTray = value;
            if (!value)
            {
                _state.Settings.RunInBackground = false;
                _state.Settings.StartMinimized = false;
                _state.Settings.MinimizeToSystemTray = false;
                _state.Settings.CloseToSystemTray = false;
                OnPropertyChanged(nameof(RunInBackground));
                OnPropertyChanged(nameof(StartMinimized));
                OnPropertyChanged(nameof(MinimizeToSystemTray));
                OnPropertyChanged(nameof(CloseToSystemTray));
                OnPropertyChanged(nameof(MinimizeToTray));
            }
            OnPropertyChanged();
            SaveState();
            TrayVisibilityChanged?.Invoke(value);
        }
    }

    public bool ReloadAfterResume
    {
        get => _state.Settings.ReloadAfterResume;
        set
        {
            if (_state.Settings.ReloadAfterResume == value) return;
            _state.Settings.ReloadAfterResume = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public int ReloadAfterResumeTime
    {
        get => _state.Settings.ReloadAfterResumeTime;
        set
        {
            var clamped = Math.Clamp(value, 1, 1440);
            if (_state.Settings.ReloadAfterResumeTime == clamped) return;
            _state.Settings.ReloadAfterResumeTime = clamped;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool StartMinimized
    {
        get => _state.Settings.StartMinimized;
        set
        {
            if (_state.Settings.StartMinimized == value) return;
            _state.Settings.StartMinimized = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool MinimizeToSystemTray
    {
        get => _state.Settings.MinimizeToSystemTray;
        set
        {
            if (_state.Settings.MinimizeToSystemTray == value) return;
            _state.Settings.MinimizeToSystemTray = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool CloseToSystemTray
    {
        get => _state.Settings.CloseToSystemTray;
        set
        {
            if (_state.Settings.CloseToSystemTray == value) return;
            _state.Settings.CloseToSystemTray = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MinimizeToTray));
            SaveState();
        }
    }

    public bool KeepAllWorkspacesLoaded
    {
        get => _state.Settings.KeepAllWorkspacesLoaded;
        set
        {
            if (_state.Settings.KeepAllWorkspacesLoaded == value) return;
            _state.Settings.KeepAllWorkspacesLoaded = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool EnableTodos
    {
        get => _state.Settings.EnableTodos;
        set
        {
            if (_state.Settings.EnableTodos == value) return;
            _state.Settings.EnableTodos = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public string PredefinedTodoServer
    {
        get => _state.Settings.PredefinedTodoServer;
        set
        {
            if (_state.Settings.PredefinedTodoServer == value) return;
            _state.Settings.PredefinedTodoServer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomTodoServer));
            SaveState();
        }
    }

    public bool IsCustomTodoServer =>
        string.Equals(PredefinedTodoServer, "isUsingCustomTodoService", StringComparison.OrdinalIgnoreCase);

    public string CustomTodoServer
    {
        get => _state.Settings.CustomTodoServer;
        set
        {
            if (_state.Settings.CustomTodoServer == value) return;
            _state.Settings.CustomTodoServer = value ?? string.Empty;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool ScheduledDndEnabled
    {
        get => _state.Settings.ScheduledDndEnabled;
        set
        {
            if (_state.Settings.ScheduledDndEnabled == value) return;
            _state.Settings.ScheduledDndEnabled = value;
            OnPropertyChanged();
            SaveState();
            NotifyTrayStatus();
        }
    }

    public string ScheduledDndStart
    {
        get => _state.Settings.ScheduledDndStart;
        set
        {
            if (_state.Settings.ScheduledDndStart == value) return;
            _state.Settings.ScheduledDndStart = value ?? "17:00";
            OnPropertyChanged();
            SaveState();
            NotifyTrayStatus();
        }
    }

    public string ScheduledDndEnd
    {
        get => _state.Settings.ScheduledDndEnd;
        set
        {
            if (_state.Settings.ScheduledDndEnd == value) return;
            _state.Settings.ScheduledDndEnd = value ?? "09:00";
            OnPropertyChanged();
            SaveState();
            NotifyTrayStatus();
        }
    }

    public bool IsEffectiveMuted =>
        IsGlobalMuted || FerdiumSettingsImporter.IsWithinScheduledDnd(
            ScheduledDndEnabled,
            ScheduledDndStart,
            ScheduledDndEnd,
            DateTime.Now.TimeOfDay);

    // ==================== SERVICES SETTINGS ====================
    public bool ShowDisabledServices
    {
        get => _state.Settings.ShowDisabledServices;
        set
        {
            if (_state.Settings.ShowDisabledServices == value) return;
            _state.Settings.ShowDisabledServices = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool UseGrayscaleServices
    {
        get => _state.Settings.UseGrayscaleServices;
        set
        {
            if (_state.Settings.UseGrayscaleServices == value) return;
            _state.Settings.UseGrayscaleServices = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public int GrayscaleServicesDim
    {
        get => _state.Settings.GrayscaleServicesDim;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (_state.Settings.GrayscaleServicesDim == clamped) return;
            _state.Settings.GrayscaleServicesDim = clamped;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool ShowMessageBadgeWhenMuted
    {
        get => _state.Settings.ShowMessageBadgeWhenMuted;
        set
        {
            if (_state.Settings.ShowMessageBadgeWhenMuted == value) return;
            _state.Settings.ShowMessageBadgeWhenMuted = value;
            OnPropertyChanged();
            SaveState();
            NotifyTrayStatus();
        }
    }

    public bool EnableLongPressServiceHint
    {
        get => _state.Settings.EnableLongPressServiceHint;
        set
        {
            if (_state.Settings.EnableLongPressServiceHint == value) return;
            _state.Settings.EnableLongPressServiceHint = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public int IconSize
    {
        get => _state.Settings.IconSize;
        set
        {
            var clamped = Math.Clamp(value, 0, 40);
            if (_state.Settings.IconSize == clamped) return;
            _state.Settings.IconSize = clamped;
            OnPropertyChanged();
            NotifySidebarLayoutChanged();
            SaveState();
        }
    }

    private double BaseIconPixels => IconSize switch
    {
        <= 5 => 20,
        <= 15 => 24,
        <= 25 => 28,
        <= 35 => 32,
        _ => 36,
    };

    public double EffectiveSidebarWidth => ShowServiceNames ? double.NaN : ServiceRibbonWidth;

    public double EffectiveIconBoxSize
    {
        get
        {
            var desired = Math.Max(32, BaseIconPixels + 6);
            if (ShowServiceNames) return desired;
            var maxFit = Math.Max(22, ServiceRibbonWidth - 11);
            return Math.Min(desired, maxFit);
        }
    }

    public double EffectiveIconPixels =>
        Math.Max(16, Math.Min(BaseIconPixels, EffectiveIconBoxSize - 4));

    public System.Windows.Thickness EffectiveSidebarItemPadding => ServiceRibbonWidth switch
    {
        <= 38 => new System.Windows.Thickness(1, 4, 1, 4),
        <= 48 => new System.Windows.Thickness(3, 5, 3, 5),
        <= 58 => new System.Windows.Thickness(5, 6, 5, 6),
        _ => new System.Windows.Thickness(8, 6, 8, 6),
    };

    public void NotifySidebarLayoutChanged()
    {
        OnPropertyChanged(nameof(EffectiveSidebarWidth));
        OnPropertyChanged(nameof(EffectiveIconBoxSize));
        OnPropertyChanged(nameof(EffectiveIconPixels));
        OnPropertyChanged(nameof(EffectiveSidebarItemPadding));
    }

    public string NavigationBarBehaviour
    {
        get => _state.Settings.NavigationBarBehaviour;
        set
        {
            if (_state.Settings.NavigationBarBehaviour == value) return;
            _state.Settings.NavigationBarBehaviour = value ?? "always";
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowWebControls));
            SaveState();
        }
    }

    public bool HibernateOnStartup
    {
        get => _state.Settings.HibernateOnStartup;
        set
        {
            if (_state.Settings.HibernateOnStartup == value) return;
            _state.Settings.HibernateOnStartup = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public int HibernationStrategySeconds
    {
        get => _state.Settings.HibernationStrategySeconds;
        set
        {
            var clamped = Math.Clamp(value, 10, 86400);
            if (_state.Settings.HibernationStrategySeconds == clamped) return;
            _state.Settings.HibernationStrategySeconds = clamped;
            _state.Settings.AutoHibernateMinutes = Math.Max(1, (int)Math.Round(clamped / 60.0));
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoHibernateMinutes));
            OnPropertyChanged(nameof(AutoHibernateSummary));
            SaveState();
        }
    }

    public int WakeUpStrategySeconds
    {
        get => _state.Settings.WakeUpStrategySeconds;
        set
        {
            var clamped = Math.Clamp(value, 0, 86400);
            if (_state.Settings.WakeUpStrategySeconds == clamped) return;
            _state.Settings.WakeUpStrategySeconds = clamped;
            OnPropertyChanged();
            SaveState();
        }
    }

    public int WakeUpHibernationStrategySeconds
    {
        get => _state.Settings.WakeUpHibernationStrategySeconds;
        set
        {
            var clamped = Math.Clamp(value, 0, 86400);
            if (_state.Settings.WakeUpHibernationStrategySeconds == clamped) return;
            _state.Settings.WakeUpHibernationStrategySeconds = clamped;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool WakeUpHibernationSplay
    {
        get => _state.Settings.WakeUpHibernationSplay;
        set
        {
            if (_state.Settings.WakeUpHibernationSplay == value) return;
            _state.Settings.WakeUpHibernationSplay = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool SandboxServices
    {
        get => _state.Settings.SandboxServices;
        set
        {
            if (_state.Settings.SandboxServices == value) return;
            _state.Settings.SandboxServices = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    // ==================== APPEARANCE SETTINGS ====================
    public bool UniversalDarkMode
    {
        get => _state.Settings.UniversalDarkMode;
        set
        {
            if (_state.Settings.UniversalDarkMode == value) return;
            _state.Settings.UniversalDarkMode = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool SplitMode
    {
        get => _state.Settings.SplitMode;
        set
        {
            if (_state.Settings.SplitMode == value) return;
            _state.Settings.SplitMode = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public int SplitColumns
    {
        get => _state.Settings.SplitColumns;
        set
        {
            var clamped = Math.Clamp(value, 1, 5);
            if (_state.Settings.SplitColumns == clamped) return;
            _state.Settings.SplitColumns = clamped;
            OnPropertyChanged();
            SaveState();
        }
    }

    public string ProgressbarAccentColor
    {
        get => _state.Settings.ProgressbarAccentColor;
        set
        {
            if (_state.Settings.ProgressbarAccentColor == value) return;
            _state.Settings.ProgressbarAccentColor = string.IsNullOrWhiteSpace(value) ? "#7367F0" : value.Trim();
            OnPropertyChanged();
            SaveState();
        }
    }

    public int ServiceRibbonWidth
    {
        get => _state.Settings.ServiceRibbonWidth;
        set
        {
            var clamped = Math.Clamp(value, 35, 120);
            if (_state.Settings.ServiceRibbonWidth == clamped) return;
            _state.Settings.ServiceRibbonWidth = clamped;
            OnPropertyChanged();
            NotifySidebarLayoutChanged();
            SaveState();
        }
    }

    public int WebviewPaddingSize
    {
        get => _state.Settings.WebviewPaddingSize;
        set
        {
            var clamped = Math.Clamp(value, 0, 32);
            if (_state.Settings.WebviewPaddingSize == clamped) return;
            _state.Settings.WebviewPaddingSize = clamped;
            OnPropertyChanged();
            SaveState();
        }
    }

    public int ServiceWebviewBorderRadius
    {
        get => _state.Settings.ServiceWebviewBorderRadius;
        set
        {
            var clamped = Math.Clamp(value, 0, 24);
            if (_state.Settings.ServiceWebviewBorderRadius == clamped) return;
            _state.Settings.ServiceWebviewBorderRadius = clamped;
            OnPropertyChanged();
            SaveState();
        }
    }

    public string SidebarServicesLocation
    {
        get => _state.Settings.SidebarServicesLocation;
        set
        {
            if (_state.Settings.SidebarServicesLocation == value) return;
            _state.Settings.SidebarServicesLocation = value ?? "topLeft";
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool UseHorizontalStyle
    {
        get => _state.Settings.UseHorizontalStyle;
        set
        {
            if (_state.Settings.UseHorizontalStyle == value) return;
            _state.Settings.UseHorizontalStyle = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool HideCollapseButton
    {
        get => _state.Settings.HideCollapseButton;
        set
        {
            if (_state.Settings.HideCollapseButton == value) return;
            _state.Settings.HideCollapseButton = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool HideRecipesButton
    {
        get => _state.Settings.HideRecipesButton;
        set
        {
            if (_state.Settings.HideRecipesButton == value) return;
            _state.Settings.HideRecipesButton = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool HideSplitModeButton
    {
        get => _state.Settings.HideSplitModeButton;
        set
        {
            if (_state.Settings.HideSplitModeButton == value) return;
            _state.Settings.HideSplitModeButton = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool HideWorkspacesButton
    {
        get => _state.Settings.HideWorkspacesButton;
        set
        {
            if (_state.Settings.HideWorkspacesButton == value) return;
            _state.Settings.HideWorkspacesButton = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool HideNotificationsButton
    {
        get => _state.Settings.HideNotificationsButton;
        set
        {
            if (_state.Settings.HideNotificationsButton == value) return;
            _state.Settings.HideNotificationsButton = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool HideSettingsButton
    {
        get => _state.Settings.HideSettingsButton;
        set
        {
            if (_state.Settings.HideSettingsButton == value) return;
            _state.Settings.HideSettingsButton = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool HideDownloadButton
    {
        get => _state.Settings.HideDownloadButton;
        set
        {
            if (_state.Settings.HideDownloadButton == value) return;
            _state.Settings.HideDownloadButton = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool AlwaysShowWorkspaces
    {
        get => _state.Settings.AlwaysShowWorkspaces;
        set
        {
            if (_state.Settings.AlwaysShowWorkspaces == value) return;
            _state.Settings.AlwaysShowWorkspaces = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool UseCompactWorkspaceDrawer
    {
        get => _state.Settings.UseCompactWorkspaceDrawer;
        set
        {
            if (_state.Settings.UseCompactWorkspaceDrawer == value) return;
            _state.Settings.UseCompactWorkspaceDrawer = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool HideAllServicesWorkspace
    {
        get => _state.Settings.HideAllServicesWorkspace;
        set
        {
            if (_state.Settings.HideAllServicesWorkspace == value) return;
            _state.Settings.HideAllServicesWorkspace = value;
            OnPropertyChanged();
            RebuildWorkspaces();
            SaveState();
        }
    }

    // ==================== PRIVACY SETTINGS ====================
    public bool PrivateNotifications
    {
        get => _state.Settings.PrivateNotifications;
        set
        {
            if (_state.Settings.PrivateNotifications == value) return;
            _state.Settings.PrivateNotifications = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool ClipboardNotifications
    {
        get => _state.Settings.ClipboardNotifications;
        set
        {
            if (_state.Settings.ClipboardNotifications == value) return;
            _state.Settings.ClipboardNotifications = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool NotifyTaskBarOnMessage
    {
        get => _state.Settings.NotifyTaskBarOnMessage;
        set
        {
            if (_state.Settings.NotifyTaskBarOnMessage == value) return;
            _state.Settings.NotifyTaskBarOnMessage = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool IsTwoFactorAutoCatcherEnabled
    {
        get => _state.Settings.IsTwoFactorAutoCatcherEnabled;
        set
        {
            if (_state.Settings.IsTwoFactorAutoCatcherEnabled == value) return;
            _state.Settings.IsTwoFactorAutoCatcherEnabled = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public string TwoFactorAutoCatcherMatcher
    {
        get => _state.Settings.TwoFactorAutoCatcherMatcher;
        set
        {
            if (_state.Settings.TwoFactorAutoCatcherMatcher == value) return;
            _state.Settings.TwoFactorAutoCatcherMatcher = value ?? string.Empty;
            OnPropertyChanged();
            SaveState();
        }
    }

    public string WebRtcIpHandlingPolicy
    {
        get => _state.Settings.WebRtcIpHandlingPolicy;
        set
        {
            if (_state.Settings.WebRtcIpHandlingPolicy == value) return;
            _state.Settings.WebRtcIpHandlingPolicy = value ?? "disable_non_proxied_udp";
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool Sentry
    {
        get => _state.Settings.Sentry;
        set
        {
            if (_state.Settings.Sentry == value) return;
            _state.Settings.Sentry = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public string SearchEngine
    {
        get => _state.Settings.SearchEngine;
        set
        {
            if (_state.Settings.SearchEngine == value) return;
            _state.Settings.SearchEngine = value ?? "startPage";
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool IsLockingFeatureEnabled
    {
        get => _state.Settings.IsLockingFeatureEnabled;
        set
        {
            if (_state.Settings.IsLockingFeatureEnabled == value) return;
            _state.Settings.IsLockingFeatureEnabled = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public string LockedPassword
    {
        get => _state.Settings.LockedPassword;
        set
        {
            if (_state.Settings.LockedPassword == value) return;
            _state.Settings.LockedPassword = value ?? string.Empty;
            OnPropertyChanged();
            SaveState();
        }
    }

    public int InactivityLockMinutes
    {
        get => _state.Settings.InactivityLockMinutes;
        set
        {
            var clamped = Math.Clamp(value, 0, 1440);
            if (_state.Settings.InactivityLockMinutes == clamped) return;
            _state.Settings.InactivityLockMinutes = clamped;
            OnPropertyChanged();
            SaveState();
        }
    }

    // ==================== LANGUAGE SETTINGS ====================
    public string Locale
    {
        get => _state.Settings.Locale;
        set
        {
            if (_state.Settings.Locale == value) return;
            _state.Settings.Locale = value ?? "en-US";
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool EnableSpellchecking
    {
        get => _state.Settings.EnableSpellchecking;
        set
        {
            if (_state.Settings.EnableSpellchecking == value) return;
            _state.Settings.EnableSpellchecking = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public string SpellcheckerLanguage
    {
        get => _state.Settings.SpellcheckerLanguage;
        set
        {
            if (_state.Settings.SpellcheckerLanguage == value) return;
            _state.Settings.SpellcheckerLanguage = value ?? "en-us";
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool EnableTranslator
    {
        get => _state.Settings.EnableTranslator;
        set
        {
            if (_state.Settings.EnableTranslator == value) return;
            _state.Settings.EnableTranslator = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public string TranslatorEngine
    {
        get => _state.Settings.TranslatorEngine;
        set
        {
            if (_state.Settings.TranslatorEngine == value) return;
            _state.Settings.TranslatorEngine = value ?? "LibreTranslate";
            OnPropertyChanged();
            SaveState();
        }
    }

    public string TranslatorLanguage
    {
        get => _state.Settings.TranslatorLanguage;
        set
        {
            if (_state.Settings.TranslatorLanguage == value) return;
            _state.Settings.TranslatorLanguage = value ?? "en";
            OnPropertyChanged();
            SaveState();
        }
    }

    // ==================== ADVANCED SETTINGS ====================
    public bool EnableGpuAcceleration
    {
        get => _state.Settings.EnableGpuAcceleration;
        set
        {
            if (_state.Settings.EnableGpuAcceleration == value) return;
            _state.Settings.EnableGpuAcceleration = value;
            OnPropertyChanged();
            SaveState();
            GlobalSettingsChanged?.Invoke(_state.Settings);
        }
    }

    public bool EnableGlobalHideShortcut
    {
        get => _state.Settings.EnableGlobalHideShortcut;
        set
        {
            if (_state.Settings.EnableGlobalHideShortcut == value) return;
            _state.Settings.EnableGlobalHideShortcut = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool UseSelfSignedCertificates
    {
        get => _state.Settings.UseSelfSignedCertificates;
        set
        {
            if (_state.Settings.UseSelfSignedCertificates == value) return;
            _state.Settings.UseSelfSignedCertificates = value;
            OnPropertyChanged();
            SaveState();
            GlobalSettingsChanged?.Invoke(_state.Settings);
        }
    }

    public string DownloadFolderPath
    {
        get => _state.Settings.DownloadFolderPath;
        set
        {
            if (_state.Settings.DownloadFolderPath == value) return;
            _state.Settings.DownloadFolderPath = value ?? string.Empty;
            OnPropertyChanged();
            SaveState();
            GlobalSettingsChanged?.Invoke(_state.Settings);
        }
    }

    public string UserAgentPref
    {
        get => _state.Settings.UserAgentPref;
        set
        {
            if (_state.Settings.UserAgentPref == value) return;
            _state.Settings.UserAgentPref = value ?? string.Empty;
            OnPropertyChanged();
            SaveState();
            GlobalSettingsChanged?.Invoke(_state.Settings);
        }
    }

    public string ShortcutActivateNextService
    {
        get => _state.Settings.ShortcutActivateNextService;
        set
        {
            if (_state.Settings.ShortcutActivateNextService == value) return;
            _state.Settings.ShortcutActivateNextService = string.IsNullOrWhiteSpace(value) ? "Ctrl+Tab" : value.Trim();
            OnPropertyChanged();
            SaveState();
        }
    }

    public string ShortcutActivatePreviousService
    {
        get => _state.Settings.ShortcutActivatePreviousService;
        set
        {
            if (_state.Settings.ShortcutActivatePreviousService == value) return;
            _state.Settings.ShortcutActivatePreviousService = string.IsNullOrWhiteSpace(value) ? "Ctrl+Shift+Tab" : value.Trim();
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool ActivateServiceUsesAlt
    {
        get => _state.Settings.ActivateServiceUsesAlt;
        set
        {
            if (_state.Settings.ActivateServiceUsesAlt == value) return;
            _state.Settings.ActivateServiceUsesAlt = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    // ==================== UPDATES SETTINGS ====================
    public bool AutomaticUpdates
    {
        get => _state.Settings.AutomaticUpdates;
        set
        {
            if (_state.Settings.AutomaticUpdates == value) return;
            _state.Settings.AutomaticUpdates = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool BetaUpdates
    {
        get => _state.Settings.BetaUpdates;
        set
        {
            if (_state.Settings.BetaUpdates == value) return;
            _state.Settings.BetaUpdates = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    // ==================== COMMANDS ====================
    [RelayCommand]
    public void SelectSettingsTab(string? tabName)
    {
        if (string.IsNullOrWhiteSpace(tabName)) return;
        ActiveSettingsTab = tabName;
        if (string.Equals(tabName, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            RefreshCacheSizeAsync();
        }
    }

    [RelayCommand]
    public void ImportFromDefaultFerdium()
    {
        var defaultPath = FerdiumSettingsImporter.GetDefaultFerdiumSettingsPath();
        ApplyImportedFerdiumFile(defaultPath);
    }

    [RelayCommand]
    public void RequestImportFerdiumFromFile()
    {
        ImportFerdiumFileRequested?.Invoke();
    }

    public void ApplyImportedFerdiumFile(string filePath)
    {
        var result = FerdiumSettingsImporter.ImportFromFile(filePath, _state.Settings);
        SettingsImportStatusText = result.Message;

        if (result.Success)
        {
            SystemIntegrationService.SetAutoLaunchOnStart(_state.Settings.AutoLaunchOnStart);
            SaveState();
            NotifyAllSettingsChanged();
            ThemeOrBackdropChanged?.Invoke();
            TrayVisibilityChanged?.Invoke(_state.Settings.EnableSystemTray);
            GlobalSettingsChanged?.Invoke(_state.Settings);
            NotifyTrayStatus();
        }
    }

    [RelayCommand]
    public async Task ClearAllCacheAsync()
    {
        CacheSizeText = "Clearing cache...";
        if (ClearAllCacheRequested is not null)
        {
            await ClearAllCacheRequested.Invoke();
        }
        RefreshCacheSizeAsync();
    }

    [RelayCommand]
    public void OpenProfileFolder()
    {
        SystemIntegrationService.OpenFolderInExplorer(SystemIntegrationService.GetProfileFolderPath());
    }

    [RelayCommand]
    public void RequestBrowseDownloadFolder()
    {
        BrowseDownloadFolderRequested?.Invoke();
    }

    [RelayCommand]
    public void CheckForUpdates()
    {
        var channel = BetaUpdates ? "beta" : "stable";
        UpdateStatusText = $"Checked ({channel} channel): You are using the latest version ({AppVersionText}).";
    }

    [RelayCommand]
    public void OpenFerdiumGitHub()
    {
        WebViewHostManager.OpenExternalUrl("https://github.com/ferdium/ferdium-app");
    }

    [RelayCommand]
    public void OpenTranslationPage()
    {
        WebViewHostManager.OpenExternalUrl("https://crowdin.com/project/ferdium-app");
    }

    public void RefreshCacheSizeAsync()
    {
        CacheSizeText = "Calculating...";
        _ = Task.Run(() =>
        {
            var bytes = SystemIntegrationService.GetDirectorySizeBytes(
                SystemIntegrationService.GetWebViewProfilesRootPath());
            var formatted = SystemIntegrationService.FormatBytes(bytes);
            CacheSizeText = $"WebView2 cache & profiles are currently using {formatted} of disk space.";
        });
    }

    private void NotifyAllSettingsChanged()
    {
        OnPropertyChanged(string.Empty);
    }
}
