using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FerdiumApp.Models;
using FerdiumApp.Services;
using FerdiumApp.ViewModels;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FerdiumApp;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly WebViewHostManager _webViewManager;
    private readonly BrowserExtensionService _extensionService;
    private readonly TrayIconService _trayService;
    private readonly NotificationService _notificationService;
    private readonly DispatcherTimer _maintenanceTimer;
    private ExtensionPopupWindow? _activeExtensionPopup;
    private bool _isExplicitExit;
    private DateTime? _suspendedAtUtc;

    private const string ServiceDragDataFormat = "FerdiumApp.ServiceItemViewModel";
    private Point _serviceDragStartPoint;
    private ServiceItemViewModel? _draggedService;
    private bool _isDraggingService;

    public MainWindow()
    {
        InitializeComponent();

        var persistence = new SettingsPersistenceService();
        var catalog = new ServiceCatalogService();
        _extensionService = new BrowserExtensionService();
        _viewModel = new MainWindowViewModel(persistence, catalog);
        _viewModel.InitializeExtensions(_extensionService);
        DataContext = _viewModel;

        _webViewManager = new WebViewHostManager(WebViewHostGrid);
        _webViewManager.SetExtensionService(_extensionService);
        _webViewManager.ApplyGlobalSettings(_viewModel.Settings);
        _webViewManager.ServiceStateChanged += (_, args) =>
        {
            Dispatcher.Invoke(() => _viewModel.ApplyWebViewState(args));
        };
        _webViewManager.ServiceNotificationReceived += (_, args) =>
        {
            Dispatcher.Invoke(() => HandleServiceNotification(args));
        };

        _trayService = new TrayIconService();
        _trayService.SetVisible(_viewModel.EnableSystemTray);
        _trayService.OpenRequested += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        _trayService.ToggleMuteRequested += (_, _) => Dispatcher.Invoke(() =>
        {
            _viewModel.ToggleGlobalMute();
            _webViewManager.UpdateAllMuteStates(_viewModel.GetAllServiceConfigs(), _viewModel.IsEffectiveMuted);
        });
        _trayService.ExitRequested += (_, _) => Dispatcher.Invoke(() =>
        {
            _isExplicitExit = true;
            Close();
        });

        _notificationService = new NotificationService((title, body) =>
            Dispatcher.Invoke(() => _trayService.ShowBalloonNotification(title, body)));
        _notificationService.NotificationActivated += (_, args) =>
        {
            Dispatcher.InvokeAsync(async () =>
            {
                RestoreFromTray();
                var target = _viewModel.AllServices.FirstOrDefault(s => s.Id == args.ServiceId);
                if (target is not null)
                {
                    await _viewModel.ActivateServiceAsync(target);
                }

                _webViewManager.ReportNotificationClicked(args.NotificationId);
            });
        };

        WireViewModelEvents();

        _maintenanceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _maintenanceTimer.Tick += async (_, _) =>
        {
            await _viewModel.CheckAutoHibernateAsync();
            await _viewModel.CheckAutoLockAsync();
            _viewModel.NotifyTrayStatus();
            _viewModel.UpdateResourceFootprint();
        };

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;

        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private void WireViewModelEvents()
    {
        _viewModel.ServiceActivationRequested += async (config, globalMuted, suspendBg) =>
        {
            await _webViewManager.ActivateServiceAsync(config, globalMuted, suspendBg);
        };

        _viewModel.ServiceHibernationRequested += async (serviceId, trimMemory) =>
        {
            await _webViewManager.HibernateServiceAsync(serviceId, trimMemory);
        };

        _viewModel.ServiceHideRequested += id => _webViewManager.HideService(id);

        _viewModel.ServiceReloadRequested += id => _webViewManager.Reload(id);
        _viewModel.ServiceGoBackRequested += id => _webViewManager.GoBack(id);
        _viewModel.ServiceGoForwardRequested += id => _webViewManager.GoForward(id);
        _viewModel.ServiceNavigateRequested += (id, url) => _webViewManager.Navigate(id, url);
        _viewModel.ServiceDevToolsRequested += id => _webViewManager.OpenDevTools(id);

        _viewModel.ThemeOrBackdropChanged += ApplyThemeAndBackdrop;
        _viewModel.GlobalSettingsChanged += settings => _webViewManager.ApplyGlobalSettings(settings);
        _viewModel.TrayVisibilityChanged += visible => _trayService.SetVisible(visible);
        _viewModel.ClearAllCacheRequested += () => _webViewManager.ClearAllCacheAsync();
        _viewModel.BrowseDownloadFolderRequested += BrowseDownloadFolder;
        _viewModel.ImportFerdiumFileRequested += BrowseAndImportFerdiumSettings;
        _viewModel.ExtensionsChangedRequested += () => _webViewManager.SyncExtensionsAcrossAllLoadedProfilesAsync();
        _viewModel.BrowseCrxFileRequested += BrowseAndInstallCrxExtension;
        _viewModel.BrowseExtensionFolderRequested += BrowseAndLoadUnpackedExtension;
        _viewModel.OpenExtensionOptionsRequested += OpenExtensionOptionsWindow;
        _viewModel.TrayStatusChanged += (unread, muted) =>
        {
            _trayService.UpdateStatus(unread, muted);
            _webViewManager.UpdateAllMuteStates(_viewModel.GetAllServiceConfigs(), muted);
        };

        // Hide WebView2 host while any XAML overlay is open to prevent HWND airspace clipping
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainWindowViewModel.IsQuickSwitchOpen)
                or nameof(MainWindowViewModel.IsAddServiceOpen)
                or nameof(MainWindowViewModel.IsSettingsOpen)
                or nameof(MainWindowViewModel.IsWorkspacesOpen)
                or nameof(MainWindowViewModel.IsSessionShareOpen)
                or nameof(MainWindowViewModel.IsEditServiceOpen)
                or nameof(MainWindowViewModel.IsServiceUnlockOpen))
            {
                var anyOverlay = _viewModel.IsQuickSwitchOpen
                    || _viewModel.IsAddServiceOpen
                    || _viewModel.IsSettingsOpen
                    || _viewModel.IsWorkspacesOpen
                    || _viewModel.IsSessionShareOpen
                    || _viewModel.IsEditServiceOpen
                    || _viewModel.IsServiceUnlockOpen;

                WebViewHostGrid.Visibility = anyOverlay ? Visibility.Collapsed : Visibility.Visible;

                if (_viewModel.IsQuickSwitchOpen)
                {
                    Dispatcher.BeginInvoke(
                        () => QuickSwitchInput.Focus(),
                        DispatcherPriority.Input);
                }
                else if (_viewModel.IsEditServiceOpen)
                {
                    EditLockSecretBox.Password = string.Empty;
                    EditLockSecretConfirmBox.Password = string.Empty;
                }
                else if (_viewModel.IsServiceUnlockOpen)
                {
                    UnlockSecretBox.Password = string.Empty;
                    if (_viewModel.IsUnlockSecretMode)
                    {
                        Dispatcher.BeginInvoke(
                            () => UnlockSecretBox.Focus(),
                            DispatcherPriority.Input);
                    }
                }
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DisableWindowChromeCaptionInterception();
    }

    private void DisableWindowChromeCaptionInterception()
    {
        var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome is null)
        {
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = false,
            });
            return;
        }

        if (chrome.IsFrozen)
        {
            var cloned = (System.Windows.Shell.WindowChrome)chrome.Clone();
            cloned.CaptionHeight = 0;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, cloned);
        }
        else
        {
            chrome.CaptionHeight = 0;
        }
    }

    private void TitleBarDragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // Ignore if mouse was released before DragMove started
            }
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyThemeAndBackdrop();
        DisableWindowChromeCaptionInterception();
        Dispatcher.BeginInvoke(DisableWindowChromeCaptionInterception, DispatcherPriority.ApplicationIdle);
        _maintenanceTimer.Start();

        if (_viewModel.StartMinimized)
        {
            if (_viewModel.EnableSystemTray && _viewModel.MinimizeToSystemTray)
            {
                Hide();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        await _viewModel.InitializeActiveServiceAsync();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            _ = _viewModel.LockServicesOnMinimizeAsync();
            if (_viewModel.EnableSystemTray && _viewModel.MinimizeToSystemTray)
            {
                Hide();
            }
        }
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            Dispatcher.BeginInvoke(async () => await _viewModel.LockServicesOnSystemSuspendAsync());
        }
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _suspendedAtUtc = DateTime.UtcNow;
            Dispatcher.BeginInvoke(async () => await _viewModel.LockServicesOnSystemSuspendAsync());
        }
        else if (e.Mode == PowerModes.Resume && _viewModel.ReloadAfterResume)
        {
            var idleMinutes = _suspendedAtUtc.HasValue
                ? (DateTime.UtcNow - _suspendedAtUtc.Value).TotalMinutes
                : _viewModel.ReloadAfterResumeTime;
            _suspendedAtUtc = null;

            if (idleMinutes >= _viewModel.ReloadAfterResumeTime)
            {
                Dispatcher.Invoke(() => _webViewManager.ReloadAllLoaded());
            }
        }
    }

    private void BrowseDownloadFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Default Download Folder",
        };
        if (!string.IsNullOrWhiteSpace(_viewModel.DownloadFolderPath) && Directory.Exists(_viewModel.DownloadFolderPath))
        {
            dialog.InitialDirectory = _viewModel.DownloadFolderPath;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.DownloadFolderPath = dialog.FolderName;
        }
    }

    private void BrowseAndImportFerdiumSettings()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Ferdium settings.json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "settings.json",
        };

        var defaultDir = Path.GetDirectoryName(FerdiumSettingsImporter.GetDefaultFerdiumSettingsPath());
        if (!string.IsNullOrEmpty(defaultDir) && Directory.Exists(defaultDir))
        {
            dialog.InitialDirectory = defaultDir;
        }

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ApplyImportedFerdiumFile(dialog.FileName);
        }
    }

    private void ApplyThemeAndBackdrop()
    {
        var theme = _viewModel.ThemeMode switch
        {
            AppThemeMode.Light => ApplicationTheme.Light,
            AppThemeMode.Dark => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Light
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark,
        };

        var backdrop = _viewModel.UseMica
            ? WindowBackdropType.Mica
            : WindowBackdropType.None;

        ApplicationThemeManager.Apply(theme, backdrop, updateAccent: false);
        WindowBackdropType = backdrop;

        try
        {
            var accent = (Color)ColorConverter.ConvertFromString(_viewModel.AccentColor);
            ApplicationAccentColorManager.Apply(accent, theme);
        }
        catch
        {
            // Keep default accent on invalid hex
        }
    }

    private void HandleServiceNotification(ServiceNotificationReceivedEventArgs args)
    {
        if (_viewModel.IsEffectiveMuted)
        {
            return;
        }

        var service = _viewModel.AllServices.FirstOrDefault(s => s.Id == args.ServiceId);
        if (service is not null && service.Config.IsNotificationsEffectivelyMuted())
        {
            return;
        }

        var isSilent = args.IsSilent || (service?.Config.MuteNotificationSound == true);
        var isPrivate = _viewModel.PrivateNotifications
            || (service?.Config.HideNotificationPreview == true)
            || (service?.IsLocked == true);

        _notificationService.ShowNotification(
            args.ServiceId,
            service?.DisplayName ?? args.ServiceName,
            args.Title,
            args.Body,
            isSilent,
            isPrivate,
            args.Tag,
            args.NotificationId);
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isExplicitExit && _viewModel.EnableSystemTray &&
            (_viewModel.CloseToSystemTray || _viewModel.RunInBackground))
        {
            e.Cancel = true;
            _ = _viewModel.LockServicesOnMinimizeAsync();
            Hide();
            return;
        }

        if (_viewModel.ConfirmOnQuit)
        {
            var result = System.Windows.MessageBox.Show(
                this,
                "Are you sure you want to quit Ferdium?",
                "Quit Ferdium",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
        _maintenanceTimer.Stop();
        _notificationService.Dispose();
        _webViewManager.Dispose();
        _trayService.Dispose();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            _viewModel.OpenQuickSwitch();
            e.Handled = true;
        }
        else if (e.Key == Key.L &&
                 (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (_viewModel.CanQuickLockActiveService)
            {
                _ = _viewModel.LockActiveServiceAsync();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (_viewModel.IsQuickSwitchOpen || _viewModel.IsAddServiceOpen ||
                _viewModel.IsSettingsOpen || _viewModel.IsWorkspacesOpen)
            {
                _viewModel.CloseAllOverlays();
                e.Handled = true;
            }
        }
        else if (e.Key == Key.OemComma && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            _viewModel.OpenSettings();
            e.Handled = true;
        }
    }

    private void AddressBar_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.NavigateActiveService();
            e.Handled = true;
        }
    }

    private async void QuickSwitchInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            _viewModel.MoveQuickSwitchSelection(1);
            if (_viewModel.SelectedQuickSwitchItem is not null)
            {
                QuickSwitchList.ScrollIntoView(_viewModel.SelectedQuickSwitchItem);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            _viewModel.MoveQuickSwitchSelection(-1);
            if (_viewModel.SelectedQuickSwitchItem is not null)
            {
                QuickSwitchList.ScrollIntoView(_viewModel.SelectedQuickSwitchItem);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            var target = _viewModel.SelectedQuickSwitchItem
                ?? _viewModel.QuickSwitchResults.FirstOrDefault();
            if (target is not null)
            {
                await _viewModel.SelectQuickSwitchItemAsync(target);
            }

            e.Handled = true;
        }
    }

    private async void QuickSwitchItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBoxItem { DataContext: ServiceItemViewModel item })
        {
            await _viewModel.SelectQuickSwitchItemAsync(item);
            e.Handled = true;
        }
    }

    private void QuickSwitchList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scrollViewer = FindDescendantScrollViewer(QuickSwitchList);
        if (scrollViewer is null)
        {
            return;
        }

        // Dampen wheel scroll (~42px per standard 120-delta notch instead of jumping 3 full rows)
        var targetOffset = Math.Clamp(
            scrollViewer.VerticalOffset - (e.Delta * 0.35),
            0,
            scrollViewer.ScrollableHeight);
        scrollViewer.ScrollToVerticalOffset(targetOffset);
        e.Handled = true;
    }

    private static System.Windows.Controls.ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        if (root is System.Windows.Controls.ScrollViewer sv)
        {
            return sv;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindDescendantScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void OverlayBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _viewModel.CloseAllOverlays();
    }

    private void EditLockSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox pb)
        {
            _viewModel.EditLockSecret = pb.Password;
        }
    }

    private void EditLockSecretConfirmBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox pb)
        {
            _viewModel.EditLockSecretConfirm = pb.Password;
        }
    }

    private void UnlockSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox pb)
        {
            _viewModel.UnlockSecretInput = pb.Password;
        }
    }

    private async void UnlockSecretBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await _viewModel.SubmitServiceUnlockAsync();
            e.Handled = true;
        }
    }

    private void OverlayCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void ServiceItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _serviceDragStartPoint = e.GetPosition(this);
        _draggedService = (sender as FrameworkElement)?.DataContext as ServiceItemViewModel;
    }

    private void ServiceItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingService)
        {
            _draggedService = null;
        }
    }

    private void ServiceItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedService is null || _isDraggingService)
        {
            return;
        }

        var currentPos = e.GetPosition(this);
        var deltaX = Math.Abs(currentPos.X - _serviceDragStartPoint.X);
        var deltaY = Math.Abs(currentPos.Y - _serviceDragStartPoint.Y);

        if (deltaX < SystemParameters.MinimumHorizontalDragDistance &&
            deltaY < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var service = _draggedService;
        _isDraggingService = true;
        service.IsDragging = true;

        try
        {
            var data = new DataObject(ServiceDragDataFormat, service);
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move);
        }
        finally
        {
            service.IsDragging = false;
            ClearServiceDropIndicators();
            _isDraggingService = false;
            _draggedService = null;
        }
    }

    private void ServiceItem_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(ServiceDragDataFormat))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var source = e.Data.GetData(ServiceDragDataFormat) as ServiceItemViewModel;
        var targetElement = sender as FrameworkElement;
        var target = targetElement?.DataContext as ServiceItemViewModel;

        if (source is null || target is null || targetElement is null || ReferenceEquals(source, target))
        {
            ClearServiceDropIndicators();
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        var insertAfter = e.GetPosition(targetElement).Y >= targetElement.ActualHeight / 2.0;
        ClearServiceDropIndicators(except: target);
        target.IsDropTargetBefore = !insertAfter;
        target.IsDropTargetAfter = insertAfter;

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ServiceItem_DragLeave(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ServiceItemViewModel target)
        {
            target.IsDropTargetBefore = false;
            target.IsDropTargetAfter = false;
        }
    }

    private void ServiceItem_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(ServiceDragDataFormat))
        {
            return;
        }

        var source = e.Data.GetData(ServiceDragDataFormat) as ServiceItemViewModel;
        var targetElement = sender as FrameworkElement;
        var target = targetElement?.DataContext as ServiceItemViewModel;

        ClearServiceDropIndicators();

        if (source is not null && target is not null && targetElement is not null && !ReferenceEquals(source, target))
        {
            var insertAfter = e.GetPosition(targetElement).Y >= targetElement.ActualHeight / 2.0;
            _viewModel.ReorderService(source, target, insertAfter);
        }

        e.Handled = true;
    }

    private void ClearServiceDropIndicators(ServiceItemViewModel? except = null)
    {
        foreach (var item in _viewModel.VisibleServices)
        {
            if (ReferenceEquals(item, except))
            {
                continue;
            }

            item.IsDropTargetBefore = false;
            item.IsDropTargetAfter = false;
        }
    }

    private void ExtensionsManageButton_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsDropdownPopup.IsOpen = !ExtensionsDropdownPopup.IsOpen;
    }

    private void ExtensionsDropdownManage_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsDropdownPopup.IsOpen = false;
        _viewModel.OpenExtensionsSettings();
    }

    private async void TitleBarExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        ExtensionsDropdownPopup.IsOpen = false;

        if (sender is not FrameworkElement button ||
            button.Tag is not BrowserExtensionItemViewModel item)
        {
            return;
        }

        // Toggle closed if clicking the same extension button while its popup is open
        if (_activeExtensionPopup is not null && _activeExtensionPopup.IsVisible)
        {
            var wasSame = string.Equals(_activeExtensionPopup.ExtensionId, item.Id, StringComparison.OrdinalIgnoreCase);
            try
            {
                _activeExtensionPopup.Close();
            }
            catch
            {
                // Ignore if already closed
            }
            _activeExtensionPopup = null;
            if (wasSame)
            {
                return;
            }
        }

        var target = await _webViewManager.ResolveExtensionPageTargetAsync(item.Config, openOptions: false);
        if (target is null)
        {
            _viewModel.ExtensionStatusMessage =
                $"Extension \"{item.Name}\" does not define a popup page, or no service profile is ready yet.";
            return;
        }

        var anchor = TitleBarExtensionsManageButton;
        var topLeftPx = anchor.PointToScreen(new Point(0, 0));
        var bottomRightPx = anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight));
        var source = PresentationSource.FromVisual(button);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeftDip = transform.Transform(topLeftPx);
        var bottomRightDip = transform.Transform(bottomRightPx);
        var anchorRect = new Rect(topLeftDip, bottomRightDip);

        var popup = new ExtensionPopupWindow(
            item.Id,
            item.Name,
            target.Value.Environment,
            target.Value.Url,
            closeOnDeactivate: true)
        {
            Owner = this,
        };
        popup.PositionBelowAnchor(anchorRect);
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeExtensionPopup, popup))
            {
                _activeExtensionPopup = null;
            }
        };

        _activeExtensionPopup = popup;
        popup.Show();
        popup.Activate();
    }

    private async void OpenExtensionOptionsWindow(BrowserExtensionConfig config)
    {
        var target = await _webViewManager.ResolveExtensionPageTargetAsync(config, openOptions: true);
        if (target is null)
        {
            _viewModel.ExtensionStatusMessage = $"Extension \"{config.Name}\" does not define an options page.";
            return;
        }

        var optionsWindow = new ExtensionPopupWindow(
            config.Id,
            $"{config.Name} — Options",
            target.Value.Environment,
            target.Value.Url,
            closeOnDeactivate: false)
        {
            Owner = this,
        };
        optionsWindow.Show();
    }

    private async void BrowseAndInstallCrxExtension()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Chrome Extension Package (.crx or .zip)",
            Filter = "Chrome Extension Packages (*.crx;*.zip)|*.crx;*.zip|All Files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.InstallExtensionFromCrxFileAsync(dialog.FileName);
        }
    }

    private async void BrowseAndLoadUnpackedExtension()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Unpacked Chrome Extension Folder",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await _viewModel.InstallExtensionFromFolderAsync(dialog.FolderName);
        }
    }

    private void SettingsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ScrollViewer scrollViewer)
        {
            return;
        }

        const double scrollSpeedFactor = 0.22;
        var nextOffset = scrollViewer.VerticalOffset - (e.Delta * scrollSpeedFactor);
        scrollViewer.ScrollToVerticalOffset(Math.Clamp(nextOffset, 0, scrollViewer.ScrollableHeight));
        e.Handled = true;
    }
}
