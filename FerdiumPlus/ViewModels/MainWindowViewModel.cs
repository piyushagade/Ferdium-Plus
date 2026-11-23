using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FerdiumApp.Models;
using FerdiumApp.Services;

namespace FerdiumApp.ViewModels;

public sealed class SessionShareOption
{
    public required string? ProfileKey { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public bool IsSelected { get; init; }
    public ImageSource? IconImage { get; init; }
}

public enum EditServiceDialogMode
{
    EditExisting = 0,
    AddNewService = 1,
    AddAnotherAccount = 2,
}

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly SettingsPersistenceService _persistence;
    private readonly ServiceCatalogService _catalog;
    private readonly AppState _state;
    private readonly HashSet<string> _unlockedServiceIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Delegate used to invoke Windows Hello verification; replaceable in unit tests.
    /// </summary>
    public Func<string, Task<(bool Verified, string Message)>> WindowsHelloVerifier { get; set; } =
        ServiceLockService.VerifyWindowsHelloAsync;

    public event Func<ServiceConfig, bool, bool, Task>? ServiceActivationRequested;
    public event Func<string, bool, Task>? ServiceHibernationRequested;
    public event Action<string>? ServiceHideRequested;
    public event Action<string>? ServiceReloadRequested;
    public event Action<string>? ServiceGoBackRequested;
    public event Action<string>? ServiceGoForwardRequested;
    public event Action<string, string>? ServiceNavigateRequested;
    public event Action<string>? ServiceDevToolsRequested;
    public event Action? ThemeOrBackdropChanged;
    public event Action<int, bool>? TrayStatusChanged;

    public ObservableCollection<ServiceItemViewModel> AllServices { get; } = new();
    public ObservableCollection<ServiceItemViewModel> VisibleServices { get; } = new();
    public ObservableCollection<WorkspaceItemViewModel> Workspaces { get; } = new();
    public ObservableCollection<ServiceItemViewModel> QuickSwitchResults { get; } = new();
    public ObservableCollection<ServiceCatalogItem> CatalogResults { get; } = new();
    public ObservableCollection<SessionShareOption> SessionShareOptions { get; } = new();

    public IReadOnlyList<string> AccentPresets { get; } = new[]
    {
        "#7367F0",
        "#0078D4",
        "#0099BC",
        "#107C10",
        "#E74856",
        "#CA5010",
        "#8764B8",
    };

    public IReadOnlyList<string> AccountBadgeColorPresets { get; } = new[]
    {
        "#0078D4",
        "#7367F0",
        "#107C10",
        "#CA5010",
        "#E3008C",
        "#0099BC",
        "#4A5568",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveService))]
    [NotifyPropertyChangedFor(nameof(CanQuickLockActiveService))]
    [NotifyPropertyChangedFor(nameof(QuickLockTooltip))]
    private ServiceItemViewModel? _activeService;

    [ObservableProperty]
    private WorkspaceItemViewModel? _activeWorkspace;

    [ObservableProperty]
    private string _addressBarText = string.Empty;

    [ObservableProperty]
    private bool _isQuickSwitchOpen;

    [ObservableProperty]
    private string _quickSwitchQuery = string.Empty;

    [ObservableProperty]
    private ServiceItemViewModel? _selectedQuickSwitchItem;

    [ObservableProperty]
    private bool _isAddServiceOpen;

    [ObservableProperty]
    private string _catalogSearchQuery = string.Empty;

    [ObservableProperty]
    private string _customServiceName = string.Empty;

    [ObservableProperty]
    private string _customServiceUrl = string.Empty;

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private bool _isWorkspacesOpen;

    [ObservableProperty]
    private bool _isSessionShareOpen;

    [ObservableProperty]
    private ServiceItemViewModel? _sessionShareTargetService;

    [ObservableProperty]
    private bool _isEditServiceOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditServiceDialogTitle))]
    [NotifyPropertyChangedFor(nameof(EditServiceSaveButtonText))]
    [NotifyPropertyChangedFor(nameof(IsAddingAccountMode))]
    private EditServiceDialogMode _editDialogMode = EditServiceDialogMode.EditExisting;

    public bool IsAddingAccountMode => EditDialogMode == EditServiceDialogMode.AddAnotherAccount;

    [ObservableProperty]
    private ServiceItemViewModel? _editServiceTarget;

    [ObservableProperty]
    private string _editServiceName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditPreviewBadgeText))]
    [NotifyPropertyChangedFor(nameof(EditPreviewHasBadge))]
    private string _editAccountLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditPreviewBadgeText))]
    [NotifyPropertyChangedFor(nameof(EditPreviewHasBadge))]
    private string _editAccountBadge = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditPreviewBadgeBrush))]
    private string _editAccountBadgeColor = "#0078D4";

    [ObservableProperty]
    private bool _editIsolateSession;

    // Lifecycle options in Edit Service popup
    [ObservableProperty]
    private bool _editLoadOnStartup;

    [ObservableProperty]
    private bool _editHibernateOnStartup;

    [ObservableProperty]
    private bool _editNeverHibernate;

    // Mute & Notifications sub-options in Edit Service popup
    [ObservableProperty]
    private bool _editIsMuted;

    [ObservableProperty]
    private bool _editMuteNotifications;

    [ObservableProperty]
    private bool _editMuteNotificationSound;

    [ObservableProperty]
    private bool _editMuteAudio;

    [ObservableProperty]
    private bool _editHideUnreadBadge;

    [ObservableProperty]
    private bool _editHideNotificationPreview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditMuteSnoozeSummary))]
    private DateTime? _editMuteUntilUtc;

    // Per-service Lock options in Edit Service popup
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditLockMode))]
    [NotifyPropertyChangedFor(nameof(EditIsSecretLockMode))]
    [NotifyPropertyChangedFor(nameof(EditIsWindowsHelloLockMode))]
    [NotifyPropertyChangedFor(nameof(EditIsAnyLockMode))]
    [NotifyPropertyChangedFor(nameof(EditLockSecretPlaceholder))]
    private int _editLockModeIndex;

    [ObservableProperty]
    private string _editLockSecret = string.Empty;

    [ObservableProperty]
    private string _editLockSecretConfirm = string.Empty;

    [ObservableProperty]
    private bool _editRelockOnSwitchAway;

    [ObservableProperty]
    private bool _editAutoLockEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditAutoLockSummary))]
    private int _editAutoLockMinutes = 5;

    [ObservableProperty]
    private bool _editRelockOnMinimize;

    [ObservableProperty]
    private bool _editRelockOnSystemSuspend = true;

    [ObservableProperty]
    private bool _editHasExistingLockSecret;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditLockValidationError))]
    private string _editLockValidationError = string.Empty;

    // Unlock Service Overlay state
    [ObservableProperty]
    private bool _isServiceUnlockOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnlockWindowsHelloMode))]
    [NotifyPropertyChangedFor(nameof(IsUnlockSecretMode))]
    [NotifyPropertyChangedFor(nameof(UnlockPromptSubtitle))]
    [NotifyPropertyChangedFor(nameof(UnlockInputPlaceholder))]
    private ServiceItemViewModel? _unlockTargetService;

    [ObservableProperty]
    private string _unlockSecretInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnlockError))]
    private string _unlockErrorMessage = string.Empty;

    public ServiceLockMode EditLockMode => (ServiceLockMode)Math.Clamp(EditLockModeIndex, 0, 3);
    public bool EditIsSecretLockMode => EditLockMode is ServiceLockMode.Passcode or ServiceLockMode.Password;
    public bool EditIsWindowsHelloLockMode => EditLockMode == ServiceLockMode.WindowsHello;
    public bool EditIsAnyLockMode => EditLockMode != ServiceLockMode.None;
    public bool HasEditLockValidationError => !string.IsNullOrEmpty(EditLockValidationError);
    public bool CanQuickLockActiveService => ActiveService is not null && ActiveService.CanLockNow;
    public string QuickLockTooltip => ActiveService is not null
        ? $"Lock {ActiveService.DisplayName} (Ctrl+Shift+L)"
        : "Lock active service";
    public string EditAutoLockSummary => EditAutoLockMinutes == 1
        ? "Lock after 1 min of inactivity"
        : $"Lock after {EditAutoLockMinutes} min of inactivity";
    public string EditLockSecretPlaceholder => EditLockMode == ServiceLockMode.Passcode
        ? (EditHasExistingLockSecret ? "Leave blank to keep current passcode" : "Enter numeric passcode (4+ digits)")
        : (EditHasExistingLockSecret ? "Leave blank to keep current password" : "Enter password (4+ characters)");

    public string EditMuteSnoozeSummary
    {
        get
        {
            if (!EditMuteUntilUtc.HasValue || EditMuteUntilUtc.Value <= DateTime.UtcNow)
            {
                return "Not snoozed";
            }

            var local = EditMuteUntilUtc.Value.ToLocalTime();
            return $"Snoozed until {local:ddd h:mm tt}";
        }
    }

    public bool IsUnlockWindowsHelloMode => UnlockTargetService?.Config.LockMode == ServiceLockMode.WindowsHello;
    public bool IsUnlockSecretMode => UnlockTargetService?.Config.LockMode is ServiceLockMode.Passcode or ServiceLockMode.Password;
    public bool HasUnlockError => !string.IsNullOrEmpty(UnlockErrorMessage);
    public string UnlockInputPlaceholder => UnlockTargetService?.Config.LockMode == ServiceLockMode.Passcode
        ? "Enter passcode..."
        : "Enter password...";
    public string UnlockPromptSubtitle => UnlockTargetService?.Config.LockMode switch
    {
        ServiceLockMode.Passcode => $"Enter passcode to unlock {UnlockTargetService.DisplayName}",
        ServiceLockMode.Password => $"Enter password to unlock {UnlockTargetService.DisplayName}",
        ServiceLockMode.WindowsHello => $"Verify with Windows Hello to unlock {UnlockTargetService.DisplayName}",
        _ => "Unlock service",
    };

    public string EditServiceDialogTitle => EditDialogMode switch
    {
        EditServiceDialogMode.AddNewService => "Configure New Service",
        EditServiceDialogMode.AddAnotherAccount => "Add Another Account",
        _ => "Edit Service",
    };

    public string EditServiceSaveButtonText => EditDialogMode switch
    {
        EditServiceDialogMode.AddNewService => "Add Service",
        EditServiceDialogMode.AddAnotherAccount => "Add Account",
        _ => "Save",
    };

    public string EditPreviewBadgeText => ServiceItemViewModel.ComputeAccountBadge(EditAccountBadge, EditAccountLabel);
    public bool EditPreviewHasBadge => !string.IsNullOrEmpty(EditPreviewBadgeText);
    public Brush EditPreviewBadgeBrush
    {
        get
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(EditAccountBadgeColor);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }
            catch
            {
                var fallback = new SolidColorBrush(Color.FromRgb(0, 120, 212));
                fallback.Freeze();
                return fallback;
            }
        }
    }

    [ObservableProperty]
    private string _newWorkspaceName = string.Empty;

    [ObservableProperty]
    private string _resourceFootprintText = string.Empty;

    public bool HasActiveService => ActiveService is not null;

    // Settings properties bound directly to WinUI 3 Settings overlay
    public AppThemeMode ThemeMode
    {
        get => _state.Settings.ThemeMode;
        set
        {
            if (_state.Settings.ThemeMode == value) return;
            _state.Settings.ThemeMode = value;
            OnPropertyChanged();
            SaveState();
            ThemeOrBackdropChanged?.Invoke();
        }
    }

    public int ThemeModeIndex
    {
        get => (int)_state.Settings.ThemeMode;
        set => ThemeMode = (AppThemeMode)Math.Clamp(value, 0, 2);
    }

    public bool UseMica
    {
        get => _state.Settings.UseMica;
        set
        {
            if (_state.Settings.UseMica == value) return;
            _state.Settings.UseMica = value;
            OnPropertyChanged();
            SaveState();
            ThemeOrBackdropChanged?.Invoke();
        }
    }

    public string AccentColor
    {
        get => _state.Settings.AccentColor;
        set
        {
            if (_state.Settings.AccentColor == value) return;
            _state.Settings.AccentColor = value;
            OnPropertyChanged();
            SaveState();
            ThemeOrBackdropChanged?.Invoke();
        }
    }

    public bool LazyLoadServices
    {
        get => _state.Settings.LazyLoadServices;
        set
        {
            if (_state.Settings.LazyLoadServices == value) return;
            _state.Settings.LazyLoadServices = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool SuspendBackgroundWebViews
    {
        get => _state.Settings.SuspendBackgroundWebViews;
        set
        {
            if (_state.Settings.SuspendBackgroundWebViews == value) return;
            _state.Settings.SuspendBackgroundWebViews = value;
            OnPropertyChanged();
            SaveState();
            GlobalSettingsChanged?.Invoke(_state.Settings);
        }
    }

    public int AutoHibernateMinutes
    {
        get => _state.Settings.AutoHibernateMinutes;
        set
        {
            var clamped = Math.Clamp(value, 0, 240);
            if (_state.Settings.AutoHibernateMinutes == clamped) return;
            _state.Settings.AutoHibernateMinutes = clamped;
            if (clamped > 0)
            {
                _state.Settings.HibernationStrategySeconds = clamped * 60;
                OnPropertyChanged(nameof(HibernationStrategySeconds));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoHibernateSummary));
            SaveState();
        }
    }

    public string AutoHibernateSummary =>
        AutoHibernateMinutes == 0
            ? "Disabled"
            : $"After {AutoHibernateMinutes} min idle";

    public bool TrimMemoryOnHibernate
    {
        get => _state.Settings.TrimMemoryOnHibernate;
        set
        {
            if (_state.Settings.TrimMemoryOnHibernate == value) return;
            _state.Settings.TrimMemoryOnHibernate = value;
            OnPropertyChanged();
            SaveState();
        }
    }

    public bool MinimizeToTray
    {
        get => _state.Settings.CloseToSystemTray;
        set => CloseToSystemTray = value;
    }

    public bool IsGlobalMuted
    {
        get => _state.Settings.IsMuted;
        set
        {
            if (_state.Settings.IsMuted == value) return;
            _state.Settings.IsMuted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEffectiveMuted));
            SaveState();
            NotifyTrayStatus();
        }
    }

    public bool ShowServiceNames
    {
        get => _state.Settings.ShowServiceNames;
        set
        {
            if (_state.Settings.ShowServiceNames == value) return;
            _state.Settings.ShowServiceNames = value;
            OnPropertyChanged();
            NotifySidebarLayoutChanged();
            SaveState();
        }
    }

    public bool ShowWebControls
    {
        get => !string.Equals(_state.Settings.NavigationBarBehaviour, "never", StringComparison.OrdinalIgnoreCase);
        set => NavigationBarBehaviour = value ? "always" : "never";
    }

    public MainWindowViewModel(
        SettingsPersistenceService persistence,
        ServiceCatalogService catalog)
    {
        _persistence = persistence;
        _catalog = catalog;
        _state = _persistence.Load();

        foreach (var service in _state.Services)
        {
            AllServices.Add(new ServiceItemViewModel(service));
        }

        RebuildWorkspaces();
        RefreshVisibleServices();
        RefreshSessionBadges();
        RefreshCatalogResults();
        RefreshQuickSwitchResults();
        UpdateResourceFootprint();
    }

    public async Task InitializeActiveServiceAsync()
    {
        var initial = VisibleServices.FirstOrDefault(s =>
                s.Id == _state.ActiveServiceId && !s.Config.HibernateOnStartup && !s.IsLocked)
            ?? VisibleServices.FirstOrDefault(s => !s.Config.HibernateOnStartup && !s.IsLocked);

        if (initial is not null)
        {
            await ActivateServiceAsync(initial);
        }

        // Preload services marked "Load on startup" (and all others if LazyLoadServices is disabled),
        // always respecting per-service "Hibernate on startup" and locks.
        var toPreload = AllServices
            .Where(s => s != initial
                && !s.Config.HibernateOnStartup
                && !s.IsLocked
                && (s.Config.LoadOnStartup || (!LazyLoadServices && VisibleServices.Contains(s))))
            .ToList();

        if (toPreload.Count > 0)
        {
            foreach (var service in toPreload)
            {
                if (ServiceActivationRequested is not null)
                {
                    await ServiceActivationRequested.Invoke(service.Config, IsGlobalMuted, SuspendBackgroundWebViews);
                }
            }

            if (initial is not null)
            {
                await ActivateServiceAsync(initial);
            }
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ActivateServiceAsync(ServiceItemViewModel? service)
    {
        if (service is null) return;

        // Re-lock previous service if configured to re-lock on switch away
        if (ActiveService is not null &&
            ActiveService.Id != service.Id &&
            ActiveService.Config.HasLockConfigured &&
            ActiveService.Config.RelockOnSwitchAway)
        {
            _unlockedServiceIds.Remove(ActiveService.Id);
            ActiveService.IsLocked = true;
        }

        // Gate locked services behind the Unlock Service overlay
        if (service.IsLocked)
        {
            await OpenServiceUnlockAsync(service);
            return;
        }

        IsSettingsOpen = false;
        IsServiceUnlockOpen = false;

        foreach (var item in AllServices)
        {
            item.IsActive = item.Id == service.Id;
        }

        ActiveService = service;
        service.LastUsedUtc = DateTime.UtcNow;
        AddressBarText = service.CurrentUrl;
        _state.ActiveServiceId = service.Id;
        SaveState();

        if (ServiceActivationRequested is not null)
        {
            await ServiceActivationRequested.Invoke(
                service.Config,
                IsGlobalMuted,
                SuspendBackgroundWebViews);
        }

        UpdateResourceFootprint();
    }

    [RelayCommand]
    public async Task HibernateServiceAsync(ServiceItemViewModel? service)
    {
        if (service is null) return;

        if (ServiceHibernationRequested is not null)
        {
            await ServiceHibernationRequested.Invoke(service.Id, TrimMemoryOnHibernate);
        }

        service.IsLoaded = false;
        service.IsSuspended = false;
        service.IsLoading = false;

        if (ActiveService?.Id == service.Id)
        {
            service.IsActive = false;
            ActiveService = null;
        }

        UpdateResourceFootprint();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ToggleHibernateServiceAsync(ServiceItemViewModel? service)
    {
        if (service is null) return;

        if (service.IsLoaded)
        {
            await HibernateServiceAsync(service);
        }
        else
        {
            await ActivateServiceAsync(service);
        }
    }

    [RelayCommand]
    public async Task HibernateInactiveServicesAsync()
    {
        var targets = AllServices
            .Where(s => !s.IsActive && s.IsLoaded && s.Config.IsHibernationEnabled)
            .ToList();

        foreach (var service in targets)
        {
            await HibernateServiceAsync(service);
        }
    }

    public async Task CheckAutoHibernateAsync()
    {
        if (AutoHibernateMinutes <= 0) return;

        var cutoff = DateTime.UtcNow.AddMinutes(-AutoHibernateMinutes);
        var idleServices = AllServices
            .Where(s => !s.IsActive && s.IsLoaded && s.Config.IsHibernationEnabled && s.LastUsedUtc < cutoff)
            .ToList();

        foreach (var service in idleServices)
        {
            await HibernateServiceAsync(service);
        }
    }

    public void ApplyWebViewState(ServiceWebViewStateChangedEventArgs args)
    {
        var vm = AllServices.FirstOrDefault(s => s.Id == args.ServiceId);
        if (vm is null) return;

        if (args.IsLoaded.HasValue) vm.IsLoaded = args.IsLoaded.Value;
        if (args.IsLoading.HasValue) vm.IsLoading = args.IsLoading.Value;
        if (args.IsSuspended.HasValue) vm.IsSuspended = args.IsSuspended.Value;
        if (args.CanGoBack.HasValue) vm.CanGoBack = args.CanGoBack.Value;
        if (args.CanGoForward.HasValue) vm.CanGoForward = args.CanGoForward.Value;
        if (args.Favicon is not null) vm.IconImage = args.Favicon;

        if (!string.IsNullOrWhiteSpace(args.CurrentUrl))
        {
            vm.CurrentUrl = args.CurrentUrl;
            if (ActiveService?.Id == vm.Id)
            {
                AddressBarText = args.CurrentUrl;
            }
        }

        if (args.Title is not null)
        {
            var (direct, indirect) = UnreadBadgeParser.ParseFromTitle(args.Title);
            vm.UnreadCount = direct;
            vm.HasIndirectActivity = indirect;
            NotifyTrayStatus();
        }

        UpdateResourceFootprint();
    }

    [RelayCommand]
    public void ToggleGlobalMute()
    {
        IsGlobalMuted = !IsGlobalMuted;
    }

    [RelayCommand]
    public void ToggleServiceMute(ServiceItemViewModel? service)
    {
        if (service is null) return;
        service.IsMuted = !service.IsMuted;
        service.Config.IsMuted = service.IsMuted;
        SaveState();
        NotifyTrayStatus();
    }

    [RelayCommand]
    public void ReloadActiveService()
    {
        if (ActiveService is not null)
        {
            ServiceReloadRequested?.Invoke(ActiveService.Id);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task ReloadServiceAsync(ServiceItemViewModel? service)
    {
        if (service is null) return;

        if (!service.IsLoaded)
        {
            await ActivateServiceAsync(service);
            return;
        }

        ServiceReloadRequested?.Invoke(service.Id);
    }

    [RelayCommand]
    public void GoBackActiveService()
    {
        if (ActiveService is not null)
        {
            ServiceGoBackRequested?.Invoke(ActiveService.Id);
        }
    }

    [RelayCommand]
    public void GoForwardActiveService()
    {
        if (ActiveService is not null)
        {
            ServiceGoForwardRequested?.Invoke(ActiveService.Id);
        }
    }

    [RelayCommand]
    public void NavigateActiveService()
    {
        if (ActiveService is not null && !string.IsNullOrWhiteSpace(AddressBarText))
        {
            ServiceNavigateRequested?.Invoke(ActiveService.Id, AddressBarText);
        }
    }

    [RelayCommand]
    public void OpenActiveInBrowser()
    {
        if (ActiveService is not null)
        {
            WebViewHostManager.OpenExternalUrl(ActiveService.CurrentUrl);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task OpenDevToolsForServiceAsync(ServiceItemViewModel? service)
    {
        if (service is null) return;

        if (!service.IsLoaded)
        {
            await ActivateServiceAsync(service);
        }

        ServiceDevToolsRequested?.Invoke(service.Id);
    }

    [RelayCommand]
    public async Task RemoveServiceAsync(ServiceItemViewModel? service)
    {
        if (service is null) return;

        await HibernateServiceAsync(service);
        AllServices.Remove(service);
        _state.Services.RemoveAll(s => s.Id == service.Id);
        foreach (var ws in _state.Workspaces)
        {
            ws.ServiceIds.Remove(service.Id);
        }

        RebuildWorkspaces();
        RefreshVisibleServices();
        RefreshSessionBadges();
        RefreshQuickSwitchResults();
        SaveState();

        if (ActiveService is null && VisibleServices.Count > 0)
        {
            await ActivateServiceAsync(VisibleServices[0]);
        }

        NotifyTrayStatus();
        UpdateResourceFootprint();
    }

    public bool ReorderService(ServiceItemViewModel? source, ServiceItemViewModel? target, bool insertAfter)
    {
        if (source is null || target is null || ReferenceEquals(source, target))
        {
            return false;
        }

        var oldAllIndex = AllServices.IndexOf(source);
        var targetAllIndex = AllServices.IndexOf(target);
        var oldVisibleIndex = VisibleServices.IndexOf(source);
        var targetVisibleIndex = VisibleServices.IndexOf(target);

        if (oldAllIndex < 0 || targetAllIndex < 0 || oldVisibleIndex < 0 || targetVisibleIndex < 0)
        {
            return false;
        }

        var newAllIndex = insertAfter ? targetAllIndex + 1 : targetAllIndex;
        if (oldAllIndex < newAllIndex)
        {
            newAllIndex--;
        }
        newAllIndex = Math.Clamp(newAllIndex, 0, AllServices.Count - 1);

        if (oldAllIndex == newAllIndex)
        {
            return false;
        }

        AllServices.Move(oldAllIndex, newAllIndex);

        var config = _state.Services[oldAllIndex];
        _state.Services.RemoveAt(oldAllIndex);
        _state.Services.Insert(newAllIndex, config);

        var newVisibleIndex = insertAfter ? targetVisibleIndex + 1 : targetVisibleIndex;
        if (oldVisibleIndex < newVisibleIndex)
        {
            newVisibleIndex--;
        }
        newVisibleIndex = Math.Clamp(newVisibleIndex, 0, VisibleServices.Count - 1);
        if (oldVisibleIndex != newVisibleIndex)
        {
            VisibleServices.Move(oldVisibleIndex, newVisibleIndex);
        }

        RefreshQuickSwitchResults();
        SaveState();
        return true;
    }

    [RelayCommand]
    public void OpenQuickSwitch()
    {
        CloseAllOverlays();
        QuickSwitchQuery = string.Empty;
        RefreshQuickSwitchResults();
        IsQuickSwitchOpen = true;
    }

    [RelayCommand]
    public async Task SelectQuickSwitchItemAsync(ServiceItemViewModel? service)
    {
        IsQuickSwitchOpen = false;
        if (service is null) return;

        if (!VisibleServices.Contains(service))
        {
            SelectWorkspace(Workspaces.FirstOrDefault(w => w.IsAllServices));
        }

        await ActivateServiceAsync(service);
    }

    [RelayCommand]
    public void OpenAddService()
    {
        CloseAllOverlays();
        CatalogSearchQuery = string.Empty;
        CustomServiceName = string.Empty;
        CustomServiceUrl = string.Empty;
        RefreshCatalogResults();
        IsAddServiceOpen = true;
    }

    [RelayCommand]
    public Task AddFromCatalogAsync(ServiceCatalogItem? item)
    {
        if (item is null) return Task.CompletedTask;
        var config = _catalog.CreateServiceFromCatalog(item);
        OpenNewServiceEditor(config);
        return Task.CompletedTask;
    }

    [RelayCommand]
    public Task AddCustomServiceAsync()
    {
        if (string.IsNullOrWhiteSpace(CustomServiceUrl)) return Task.CompletedTask;
        var config = _catalog.CreateCustomService(CustomServiceName, CustomServiceUrl);
        CustomServiceName = string.Empty;
        CustomServiceUrl = string.Empty;
        OpenNewServiceEditor(config);
        return Task.CompletedTask;
    }

    private void OpenNewServiceEditor(ServiceConfig draftConfig)
    {
        CloseAllOverlays();
        EditDialogMode = EditServiceDialogMode.AddNewService;
        var draftVm = new ServiceItemViewModel(draftConfig);
        EditServiceTarget = draftVm;

        var isDuplicate = HasExistingServiceMatch(draftConfig);
        PopulateEditFieldsFromConfig(draftConfig, forceIsolate: isDuplicate);

        if (isDuplicate)
        {
            var siblingCount = CountMatchingServices(draftConfig);
            EditAccountLabel = $"Account {siblingCount + 1}";
            EditAccountBadge = (siblingCount + 1).ToString();
            EditAccountBadgeColor = AccountBadgeColorPresets[siblingCount % AccountBadgeColorPresets.Count];
        }

        IsEditServiceOpen = true;
    }

    private int CountMatchingServices(ServiceConfig candidate)
    {
        var candidateDomain = ServiceIconService.ExtractDomain(candidate.Url);
        return _state.Services.Count(s =>
            (!string.Equals(candidate.CatalogId, "custom", StringComparison.OrdinalIgnoreCase) &&
             string.Equals(s.CatalogId, candidate.CatalogId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(candidateDomain) &&
             string.Equals(ServiceIconService.ExtractDomain(s.Url), candidateDomain, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task AddServiceConfigAsync(ServiceConfig config, int? insertIndex = null)
    {
        // When adding another instance of an already-added service, automatically isolate its login
        // so the user can sign into a separate account without colliding with the first account's cookies.
        if (string.IsNullOrWhiteSpace(config.SessionProfileKey) && HasExistingServiceMatch(config))
        {
            config.SessionProfileKey = "isolated";
        }

        if (insertIndex.HasValue && insertIndex.Value >= 0 && insertIndex.Value <= _state.Services.Count)
        {
            _state.Services.Insert(insertIndex.Value, config);
        }
        else
        {
            _state.Services.Add(config);
        }

        if (ActiveWorkspace?.Config is not null)
        {
            ActiveWorkspace.Config.ServiceIds.Add(config.Id);
        }

        var vm = new ServiceItemViewModel(config);
        if (insertIndex.HasValue && insertIndex.Value >= 0 && insertIndex.Value <= AllServices.Count)
        {
            AllServices.Insert(insertIndex.Value, vm);
        }
        else
        {
            AllServices.Add(vm);
        }

        RebuildWorkspaces();
        RefreshVisibleServices();
        RefreshSessionBadges();
        RefreshQuickSwitchResults();
        SaveState();

        await ActivateServiceAsync(vm);
    }

    private bool HasExistingServiceMatch(ServiceConfig candidate)
    {
        var candidateDomain = ServiceIconService.ExtractDomain(candidate.Url);
        return _state.Services.Any(existing =>
            (!string.Equals(candidate.CatalogId, "custom", StringComparison.OrdinalIgnoreCase) &&
             string.Equals(existing.CatalogId, candidate.CatalogId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(candidateDomain) &&
             string.Equals(ServiceIconService.ExtractDomain(existing.Url), candidateDomain, StringComparison.OrdinalIgnoreCase)));
    }

    [RelayCommand]
    public void OpenSettings()
    {
        CloseAllOverlays();
        UpdateResourceFootprint();
        if (string.Equals(ActiveSettingsTab, "Advanced", StringComparison.OrdinalIgnoreCase))
        {
            RefreshCacheSizeAsync();
        }
        IsSettingsOpen = true;
    }

    [RelayCommand]
    public void OpenWorkspaces()
    {
        CloseAllOverlays();
        IsWorkspacesOpen = true;
    }

    [RelayCommand]
    public void OpenSessionShare(ServiceItemViewModel? service)
    {
        if (service is null) return;

        CloseAllOverlays();
        SessionShareTargetService = service;
        SessionShareOptions.Clear();

        var currentKey = service.Config.SessionProfileKey;
        var isSharedDefault = string.IsNullOrWhiteSpace(currentKey) ||
            string.Equals(currentKey, "shared", StringComparison.OrdinalIgnoreCase);
        var isIsolated = string.Equals(currentKey, "isolated", StringComparison.OrdinalIgnoreCase);

        SessionShareOptions.Add(new SessionShareOption
        {
            ProfileKey = null,
            Title = "Shared Default Profile (Default)",
            Subtitle = "Share extensions (e.g., Bitwarden), cookies, and logins across services",
            IsSelected = isSharedDefault,
        });

        SessionShareOptions.Add(new SessionShareOption
        {
            ProfileKey = "isolated",
            Title = "Isolated (this service only)",
            Subtitle = "Keep cookies, logins, and extension storage separate from other services",
            IsSelected = isIsolated,
        });

        foreach (var other in AllServices)
        {
            if (other.Id == service.Id) continue;

            var otherKey = other.Config.SessionProfileKey;
            var effectiveKey = string.Equals(otherKey, "isolated", StringComparison.OrdinalIgnoreCase)
                ? other.Id
                : string.IsNullOrWhiteSpace(otherKey) || string.Equals(otherKey, "shared", StringComparison.OrdinalIgnoreCase)
                    ? other.Id
                    : otherKey;

            var isSame = !isSharedDefault && !isIsolated &&
                string.Equals(currentKey, effectiveKey, StringComparison.OrdinalIgnoreCase);

            SessionShareOptions.Add(new SessionShareOption
            {
                ProfileKey = effectiveKey,
                Title = $"Share with {other.Name}",
                Subtitle = $"Reuse {other.Name}'s dedicated login & cookie profile",
                IsSelected = isSame,
                IconImage = other.IconImage,
            });
        }

        IsSessionShareOpen = true;
    }

    [RelayCommand]
    public async Task ApplySessionShareAsync(SessionShareOption? option)
    {
        var target = SessionShareTargetService;
        if (target is null || option is null)
        {
            IsSessionShareOpen = false;
            return;
        }

        var newKey = string.IsNullOrWhiteSpace(option.ProfileKey) ? null : option.ProfileKey.Trim();
        var changed = !string.Equals(target.Config.SessionProfileKey, newKey, StringComparison.OrdinalIgnoreCase);
        IsSessionShareOpen = false;

        if (!changed)
        {
            return;
        }

        var wasLoaded = target.IsLoaded;
        target.Config.SessionProfileKey = newKey;
        RefreshSessionBadges();
        SaveState();

        if (wasLoaded && ServiceHibernationRequested is not null)
        {
            await ServiceHibernationRequested.Invoke(target.Id, false);
            target.IsLoaded = false;
            target.IsSuspended = false;
            target.IsLoading = false;
        }

        await ActivateServiceAsync(target);
    }

    public void RefreshSessionBadges()
    {
        var byId = AllServices.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var service in AllServices)
        {
            var key = service.Config.SessionProfileKey;
            if (string.IsNullOrWhiteSpace(key) ||
                string.Equals(key, "shared", StringComparison.OrdinalIgnoreCase))
            {
                service.SessionBadgeLabel = string.Empty;
            }
            else if (string.Equals(key, "isolated", StringComparison.OrdinalIgnoreCase))
            {
                service.SessionBadgeLabel = "Isolated session";
            }
            else if (byId.TryGetValue(key, out var owner))
            {
                service.SessionBadgeLabel = $"Shares login with {owner.Name}";
            }
            else
            {
                service.SessionBadgeLabel = "Shared login profile";
            }
        }
    }

    partial void OnEditLoadOnStartupChanged(bool value)
    {
        if (value && EditHibernateOnStartup)
        {
            EditHibernateOnStartup = false;
        }
    }

    partial void OnEditHibernateOnStartupChanged(bool value)
    {
        if (value && EditLoadOnStartup)
        {
            EditLoadOnStartup = false;
        }
    }

    private void PopulateEditFieldsFromConfig(ServiceConfig cfg, bool? forceIsolate = null)
    {
        EditServiceName = cfg.Name;
        EditAccountLabel = cfg.AccountLabel ?? string.Empty;
        EditAccountBadge = cfg.AccountBadge ?? string.Empty;
        EditAccountBadgeColor = string.IsNullOrWhiteSpace(cfg.AccountBadgeColor)
            ? AccountBadgeColorPresets[0]
            : cfg.AccountBadgeColor!;
        EditIsolateSession = forceIsolate ?? string.Equals(cfg.SessionProfileKey, "isolated", StringComparison.OrdinalIgnoreCase);

        EditLoadOnStartup = cfg.LoadOnStartup;
        EditHibernateOnStartup = cfg.HibernateOnStartup;
        EditNeverHibernate = !cfg.IsHibernationEnabled;

        EditIsMuted = cfg.IsMuted;
        EditMuteNotifications = cfg.MuteNotifications;
        EditMuteNotificationSound = cfg.MuteNotificationSound;
        EditMuteAudio = cfg.MuteAudio;
        EditHideUnreadBadge = cfg.HideUnreadBadge;
        EditHideNotificationPreview = cfg.HideNotificationPreview;
        EditMuteUntilUtc = cfg.MuteUntilUtc.HasValue && cfg.MuteUntilUtc.Value > DateTime.UtcNow
            ? cfg.MuteUntilUtc
            : null;

        EditLockModeIndex = (int)cfg.LockMode;
        EditLockSecret = string.Empty;
        EditLockSecretConfirm = string.Empty;
        EditRelockOnSwitchAway = cfg.RelockOnSwitchAway;
        if (cfg.HasLockConfigured)
        {
            EditAutoLockEnabled = cfg.AutoLockEnabled;
            EditAutoLockMinutes = Math.Clamp(cfg.AutoLockMinutes <= 0 ? 5 : cfg.AutoLockMinutes, 1, 120);
            EditRelockOnMinimize = cfg.RelockOnMinimize;
            EditRelockOnSystemSuspend = cfg.RelockOnSystemSuspend;
        }
        else
        {
            EditAutoLockEnabled = true;
            EditAutoLockMinutes = 5;
            EditRelockOnMinimize = false;
            EditRelockOnSystemSuspend = true;
        }
        EditHasExistingLockSecret = !string.IsNullOrWhiteSpace(cfg.LockSecretHash) && !string.IsNullOrWhiteSpace(cfg.LockSecretSalt);
        EditLockValidationError = string.Empty;
    }

    [RelayCommand]
    public void OpenEditService(ServiceItemViewModel? service)
    {
        if (service is null) return;

        CloseAllOverlays();
        EditDialogMode = EditServiceDialogMode.EditExisting;
        EditServiceTarget = service;
        PopulateEditFieldsFromConfig(service.Config);
        IsEditServiceOpen = true;
    }

    [RelayCommand]
    public void OpenAddAnotherAccount(ServiceItemViewModel? service)
    {
        if (service is null) return;

        CloseAllOverlays();
        EditDialogMode = EditServiceDialogMode.AddAnotherAccount;
        EditServiceTarget = service;
        PopulateEditFieldsFromConfig(service.Config, forceIsolate: true);

        // Reset per-account lock & mute to clean defaults for the new account
        EditIsMuted = false;
        EditMuteNotifications = false;
        EditMuteNotificationSound = false;
        EditMuteAudio = false;
        EditHideUnreadBadge = false;
        EditHideNotificationPreview = false;
        EditMuteUntilUtc = null;
        EditLockModeIndex = (int)ServiceLockMode.None;
        EditHasExistingLockSecret = false;
        EditRelockOnSwitchAway = false;
        EditAutoLockEnabled = true;
        EditAutoLockMinutes = 5;
        EditRelockOnMinimize = false;
        EditRelockOnSystemSuspend = true;

        var siblingCount = CountMatchingServices(service.Config);
        EditAccountLabel = $"Account {siblingCount + 1}";
        EditAccountBadge = (siblingCount + 1).ToString();
        EditAccountBadgeColor = AccountBadgeColorPresets[siblingCount % AccountBadgeColorPresets.Count];
        IsEditServiceOpen = true;
    }

    [RelayCommand]
    public void SetEditMuteSnooze(string? preset)
    {
        EditMuteUntilUtc = preset?.ToLowerInvariant() switch
        {
            "15m" => DateTime.UtcNow.AddMinutes(15),
            "1h" => DateTime.UtcNow.AddHours(1),
            "4h" => DateTime.UtcNow.AddHours(4),
            "tomorrow" => DateTime.Today.AddDays(1).AddHours(9).ToUniversalTime(),
            _ => null,
        };
    }

    [RelayCommand]
    public void SelectEditBadgeColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            EditAccountBadgeColor = hex;
        }
    }

    [RelayCommand]
    public void SetEditAutoLockMinutes(string? preset)
    {
        if (int.TryParse(preset, out var mins))
        {
            EditAutoLockMinutes = Math.Clamp(mins, 1, 120);
            EditAutoLockEnabled = true;
        }
    }

    private bool TryValidateAndResolveLockSecret(
        ServiceConfig existingConfig,
        out string? resolvedHash,
        out string? resolvedSalt)
    {
        EditLockValidationError = string.Empty;
        resolvedHash = existingConfig.LockSecretHash;
        resolvedSalt = existingConfig.LockSecretSalt;

        var mode = EditLockMode;
        if (mode is ServiceLockMode.None or ServiceLockMode.WindowsHello)
        {
            resolvedHash = null;
            resolvedSalt = null;
            return true;
        }

        var hasNewSecret = !string.IsNullOrEmpty(EditLockSecret);
        if (!hasNewSecret)
        {
            if (EditHasExistingLockSecret && existingConfig.LockMode == mode)
            {
                return true;
            }

            EditLockValidationError = mode == ServiceLockMode.Passcode
                ? "Please enter a numeric passcode (at least 4 digits)."
                : "Please enter a password (at least 4 characters).";
            return false;
        }

        if (mode == ServiceLockMode.Passcode)
        {
            if (EditLockSecret.Length < 4 || !EditLockSecret.All(char.IsDigit))
            {
                EditLockValidationError = "Passcode must be at least 4 digits (numbers only).";
                return false;
            }
        }
        else if (EditLockSecret.Length < 4)
        {
            EditLockValidationError = "Password must be at least 4 characters.";
            return false;
        }

        if (!string.Equals(EditLockSecret, EditLockSecretConfirm, StringComparison.Ordinal))
        {
            EditLockValidationError = mode == ServiceLockMode.Passcode
                ? "Passcodes do not match."
                : "Passwords do not match.";
            return false;
        }

        var (hash, salt) = ServiceLockService.HashSecret(EditLockSecret);
        resolvedHash = hash;
        resolvedSalt = salt;
        return true;
    }

    private void ApplyEditFieldsToConfig(ServiceConfig cfg, string? lockHash, string? lockSalt)
    {
        var cleanName = string.IsNullOrWhiteSpace(EditServiceName) ? cfg.Name : EditServiceName.Trim();
        cfg.Name = cleanName;
        cfg.Monogram = ServiceCatalogService.BuildMonogram(cleanName);
        cfg.AccountLabel = string.IsNullOrWhiteSpace(EditAccountLabel) ? null : EditAccountLabel.Trim();
        cfg.AccountBadge = string.IsNullOrWhiteSpace(EditAccountBadge) ? null : EditAccountBadge.Trim();
        cfg.AccountBadgeColor = string.IsNullOrWhiteSpace(EditAccountBadgeColor) ? null : EditAccountBadgeColor.Trim();

        cfg.LoadOnStartup = EditLoadOnStartup;
        cfg.HibernateOnStartup = EditHibernateOnStartup;
        cfg.IsHibernationEnabled = !EditNeverHibernate;

        cfg.IsMuted = EditIsMuted;
        cfg.MuteNotifications = EditMuteNotifications;
        cfg.MuteNotificationSound = EditMuteNotificationSound;
        cfg.MuteAudio = EditMuteAudio;
        cfg.HideUnreadBadge = EditHideUnreadBadge;
        cfg.HideNotificationPreview = EditHideNotificationPreview;
        cfg.MuteUntilUtc = EditMuteUntilUtc;

        cfg.LockMode = EditLockMode;
        cfg.LockSecretHash = lockHash;
        cfg.LockSecretSalt = lockSalt;
        cfg.RelockOnSwitchAway = EditRelockOnSwitchAway;
        cfg.AutoLockEnabled = EditLockMode != ServiceLockMode.None && EditAutoLockEnabled;
        cfg.AutoLockMinutes = Math.Clamp(EditAutoLockMinutes <= 0 ? 5 : EditAutoLockMinutes, 1, 120);
        cfg.RelockOnMinimize = EditLockMode != ServiceLockMode.None && EditRelockOnMinimize;
        cfg.RelockOnSystemSuspend = EditLockMode != ServiceLockMode.None && EditRelockOnSystemSuspend;
    }

    [RelayCommand]
    public async Task SaveEditServiceAsync()
    {
        var target = EditServiceTarget;
        if (target is null)
        {
            IsEditServiceOpen = false;
            return;
        }

        var baseConfigForValidation = EditDialogMode == EditServiceDialogMode.EditExisting
            ? target.Config
            : new ServiceConfig();

        if (!TryValidateAndResolveLockSecret(baseConfigForValidation, out var lockHash, out var lockSalt))
        {
            return;
        }

        IsEditServiceOpen = false;
        EditLockSecret = string.Empty;
        EditLockSecretConfirm = string.Empty;

        if (EditDialogMode == EditServiceDialogMode.AddNewService)
        {
            var newCfg = target.Config;
            ApplyEditFieldsToConfig(newCfg, lockHash, lockSalt);
            newCfg.SessionProfileKey = EditIsolateSession ? "isolated" : null;

            // Keep newly created service unlocked for the immediate setup sign-in
            if (newCfg.HasLockConfigured)
            {
                _unlockedServiceIds.Add(newCfg.Id);
            }

            await AddServiceConfigAsync(newCfg);
            if (newCfg.HasLockConfigured)
            {
                var addedVm = AllServices.FirstOrDefault(s => s.Id == newCfg.Id);
                if (addedVm is not null) addedVm.IsLocked = false;
            }
            return;
        }

        if (EditDialogMode == EditServiceDialogMode.AddAnotherAccount)
        {
            var clone = new ServiceConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = target.Config.Name,
                Url = target.Config.Url,
                CatalogId = target.Config.CatalogId,
                AccentColor = target.Config.AccentColor,
                Monogram = target.Config.Monogram,
                IsEnabled = true,
                CustomUserAgent = target.Config.CustomUserAgent,
                SessionProfileKey = EditIsolateSession ? "isolated" : null,
            };
            ApplyEditFieldsToConfig(clone, lockHash, lockSalt);

            if (clone.HasLockConfigured)
            {
                _unlockedServiceIds.Add(clone.Id);
            }

            var targetIndex = AllServices.IndexOf(target);
            var insertIndex = targetIndex >= 0 ? targetIndex + 1 : (int?)null;

            foreach (var ws in _state.Workspaces)
            {
                if (ws.ServiceIds.Contains(target.Id) && !ws.ServiceIds.Contains(clone.Id))
                {
                    ws.ServiceIds.Add(clone.Id);
                }
            }

            await AddServiceConfigAsync(clone, insertIndex);
            if (clone.HasLockConfigured)
            {
                var addedVm = AllServices.FirstOrDefault(s => s.Id == clone.Id);
                if (addedVm is not null) addedVm.IsLocked = false;
            }
            return;
        }

        var wasIsolated = string.Equals(target.Config.SessionProfileKey, "isolated", StringComparison.OrdinalIgnoreCase);
        var sessionChanged = false;

        ApplyEditFieldsToConfig(target.Config, lockHash, lockSalt);

        if (EditIsolateSession && !wasIsolated)
        {
            target.Config.SessionProfileKey = "isolated";
            sessionChanged = true;
        }
        else if (!EditIsolateSession && wasIsolated)
        {
            target.Config.SessionProfileKey = null;
            sessionChanged = true;
        }

        if (!target.Config.HasLockConfigured)
        {
            _unlockedServiceIds.Remove(target.Id);
            target.IsLocked = false;
        }
        else if (!_unlockedServiceIds.Contains(target.Id))
        {
            // Keep currently active service unlocked while user just configured its lock
            if (ActiveService?.Id == target.Id)
            {
                _unlockedServiceIds.Add(target.Id);
                target.IsLocked = false;
            }
            else
            {
                target.IsLocked = true;
            }
        }

        target.RefreshMetadataFromConfig();
        OnPropertyChanged(nameof(CanQuickLockActiveService));
        OnPropertyChanged(nameof(QuickLockTooltip));
        RefreshSessionBadges();
        RefreshQuickSwitchResults();
        NotifyTrayStatus();
        SaveState();

        if (sessionChanged)
        {
            var wasLoaded = target.IsLoaded;
            if (wasLoaded && ServiceHibernationRequested is not null)
            {
                await ServiceHibernationRequested.Invoke(target.Id, false);
                target.IsLoaded = false;
                target.IsSuspended = false;
                target.IsLoading = false;
            }

            await ActivateServiceAsync(target);
        }
    }

    public async Task OpenServiceUnlockAsync(ServiceItemViewModel service)
    {
        CloseAllOverlays();
        UnlockTargetService = service;
        UnlockSecretInput = string.Empty;
        UnlockErrorMessage = string.Empty;
        IsServiceUnlockOpen = true;

        if (service.Config.LockMode == ServiceLockMode.WindowsHello)
        {
            await SubmitServiceUnlockAsync();
        }
    }

    [RelayCommand]
    public async Task SubmitServiceUnlockAsync()
    {
        var target = UnlockTargetService;
        if (target is null)
        {
            IsServiceUnlockOpen = false;
            return;
        }

        UnlockErrorMessage = string.Empty;
        bool verified;

        if (target.Config.LockMode == ServiceLockMode.WindowsHello)
        {
            var (ok, message) = await WindowsHelloVerifier(target.DisplayName);
            verified = ok;
            if (!ok)
            {
                UnlockErrorMessage = string.IsNullOrWhiteSpace(message)
                    ? "Windows Hello verification failed."
                    : message;
            }
        }
        else
        {
            verified = ServiceLockService.VerifySecret(
                UnlockSecretInput,
                target.Config.LockSecretHash,
                target.Config.LockSecretSalt);
            if (!verified)
            {
                UnlockErrorMessage = target.Config.LockMode == ServiceLockMode.Passcode
                    ? "Incorrect passcode. Try again."
                    : "Incorrect password. Try again.";
            }
        }

        if (!verified)
        {
            return;
        }

        _unlockedServiceIds.Add(target.Id);
        target.IsLocked = false;
        UnlockSecretInput = string.Empty;
        UnlockErrorMessage = string.Empty;
        IsServiceUnlockOpen = false;

        await ActivateServiceAsync(target);
    }

    [RelayCommand]
    public async Task LockServiceAsync(ServiceItemViewModel? service)
    {
        if (service is null || !service.Config.HasLockConfigured) return;

        _unlockedServiceIds.Remove(service.Id);
        service.IsLocked = true;

        if (ActiveService?.Id == service.Id)
        {
            service.IsActive = false;
            ServiceHideRequested?.Invoke(service.Id);
            ActiveService = null;
        }

        OnPropertyChanged(nameof(CanQuickLockActiveService));
        OnPropertyChanged(nameof(QuickLockTooltip));
        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task LockActiveServiceAsync()
    {
        if (ActiveService is not null && ActiveService.CanLockNow)
        {
            await LockServiceAsync(ActiveService);
        }
    }

    public async Task CheckAutoLockAsync(DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var dueServices = AllServices
            .Where(s =>
                s.Config.HasLockConfigured &&
                s.Config.AutoLockEnabled &&
                s.Config.AutoLockMinutes > 0 &&
                !s.IsLocked &&
                (now - s.LastUsedUtc) >= TimeSpan.FromMinutes(s.Config.AutoLockMinutes))
            .ToList();

        foreach (var service in dueServices)
        {
            await LockServiceAsync(service);
        }
    }

    public async Task LockServicesOnMinimizeAsync()
    {
        var targets = AllServices
            .Where(s => s.Config.HasLockConfigured && s.Config.RelockOnMinimize && !s.IsLocked)
            .ToList();

        foreach (var service in targets)
        {
            await LockServiceAsync(service);
        }
    }

    public async Task LockServicesOnSystemSuspendAsync()
    {
        var targets = AllServices
            .Where(s => s.Config.HasLockConfigured && s.Config.RelockOnSystemSuspend && !s.IsLocked)
            .ToList();

        foreach (var service in targets)
        {
            await LockServiceAsync(service);
        }
    }

    [RelayCommand]
    public void CloseAllOverlays()
    {
        IsQuickSwitchOpen = false;
        IsAddServiceOpen = false;
        IsSettingsOpen = false;
        IsWorkspacesOpen = false;
        IsSessionShareOpen = false;
        IsEditServiceOpen = false;
        IsServiceUnlockOpen = false;
    }

    [RelayCommand]
    public void SelectWorkspace(WorkspaceItemViewModel? workspace)
    {
        if (workspace is null) return;

        foreach (var ws in Workspaces)
        {
            ws.IsActive = ws == workspace;
        }

        ActiveWorkspace = workspace;
        _state.ActiveWorkspaceId = workspace.Id;
        RefreshVisibleServices();
        SaveState();
    }

    [RelayCommand]
    public void CreateWorkspace()
    {
        if (string.IsNullOrWhiteSpace(NewWorkspaceName)) return;

        var ws = new WorkspaceConfig
        {
            Name = NewWorkspaceName.Trim(),
            ServiceIds = ActiveService is not null
                ? new List<string> { ActiveService.Id }
                : new List<string>(),
        };

        _state.Workspaces.Add(ws);
        NewWorkspaceName = string.Empty;
        RebuildWorkspaces();
        SaveState();
    }

    [RelayCommand]
    public void DeleteWorkspace(WorkspaceItemViewModel? workspace)
    {
        if (workspace?.Config is null) return;

        _state.Workspaces.RemoveAll(w => w.Id == workspace.Config.Id);
        if (_state.ActiveWorkspaceId == workspace.Config.Id)
        {
            _state.ActiveWorkspaceId = null;
        }

        RebuildWorkspaces();
        RefreshVisibleServices();
        SaveState();
    }

    [RelayCommand]
    public void SelectAccentColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex))
        {
            AccentColor = hex;
        }
    }

    public IReadOnlyList<ServiceConfig> GetAllServiceConfigs() => _state.Services;

    partial void OnQuickSwitchQueryChanged(string value) => RefreshQuickSwitchResults();

    partial void OnCatalogSearchQueryChanged(string value) => RefreshCatalogResults();

    private void RebuildWorkspaces()
    {
        Workspaces.Clear();
        WorkspaceItemViewModel? allItem = null;
        if (!HideAllServicesWorkspace || _state.Workspaces.Count == 0)
        {
            allItem = new WorkspaceItemViewModel(null, "All Services", AllServices.Count);
            Workspaces.Add(allItem);
        }

        foreach (var ws in _state.Workspaces)
        {
            var count = AllServices.Count(s => ws.ServiceIds.Contains(s.Id));
            Workspaces.Add(new WorkspaceItemViewModel(ws, ws.Name, count));
        }

        var selected = Workspaces.FirstOrDefault(w => w.Id == _state.ActiveWorkspaceId)
            ?? allItem
            ?? Workspaces.FirstOrDefault();
        foreach (var ws in Workspaces)
        {
            ws.IsActive = ws == selected;
        }

        ActiveWorkspace = selected;
    }

    private void RefreshVisibleServices()
    {
        VisibleServices.Clear();
        var filterIds = ActiveWorkspace?.Config?.ServiceIds;
        var items = filterIds is null
            ? AllServices
            : AllServices.Where(s => filterIds.Contains(s.Id));

        foreach (var item in items)
        {
            VisibleServices.Add(item);
        }
    }

    public void MoveQuickSwitchSelection(int delta)
    {
        if (QuickSwitchResults.Count == 0)
        {
            SelectedQuickSwitchItem = null;
            return;
        }

        var currentIndex = SelectedQuickSwitchItem is not null
            ? QuickSwitchResults.IndexOf(SelectedQuickSwitchItem)
            : -1;

        int nextIndex;
        if (currentIndex < 0)
        {
            nextIndex = delta >= 0 ? 0 : QuickSwitchResults.Count - 1;
        }
        else
        {
            nextIndex = (currentIndex + delta + QuickSwitchResults.Count) % QuickSwitchResults.Count;
        }

        SelectedQuickSwitchItem = QuickSwitchResults[nextIndex];
    }

    private void RefreshQuickSwitchResults()
    {
        QuickSwitchResults.Clear();
        var query = QuickSwitchQuery?.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? AllServices
            : AllServices.Where(s =>
                s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.AccountLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                s.Url.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var match in matches)
        {
            QuickSwitchResults.Add(match);
        }

        if (SelectedQuickSwitchItem is null || !QuickSwitchResults.Contains(SelectedQuickSwitchItem))
        {
            SelectedQuickSwitchItem = QuickSwitchResults.FirstOrDefault();
        }
    }

    private void RefreshCatalogResults()
    {
        CatalogResults.Clear();
        foreach (var item in _catalog.Search(CatalogSearchQuery))
        {
            CatalogResults.Add(item);
        }
    }

    public void NotifyTrayStatus()
    {
        var effectiveMuted = IsEffectiveMuted;
        var totalUnread = AllServices
            .Where(s => !s.Config.HideUnreadBadge && (ShowMessageBadgeWhenMuted || !s.Config.IsNotificationsEffectivelyMuted()))
            .Sum(s => s.UnreadCount);
        TrayStatusChanged?.Invoke(totalUnread, effectiveMuted);
    }

    public void UpdateResourceFootprint()
    {
        try
        {
            using var proc = Process.GetCurrentProcess();
            var hostMb = proc.WorkingSet64 / (1024 * 1024);
            var loaded = AllServices.Count(s => s.IsLoaded);
            var suspended = AllServices.Count(s => s.IsSuspended);
            ResourceFootprintText =
                $"WPF Host: {hostMb} MB • {loaded}/{AllServices.Count} WebViews loaded ({suspended} suspended)";
        }
        catch
        {
            ResourceFootprintText = $"{AllServices.Count(s => s.IsLoaded)}/{AllServices.Count} WebViews loaded";
        }
    }

    private void SaveState()
    {
        _persistence.Save(_state);
    }
}
