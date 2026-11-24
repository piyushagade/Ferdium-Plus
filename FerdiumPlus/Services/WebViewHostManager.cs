using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FerdiumApp.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FerdiumApp.Services;

public sealed class ServiceWebViewStateChangedEventArgs : EventArgs
{
    public required string ServiceId { get; init; }
    public string? Title { get; init; }
    public string? CurrentUrl { get; init; }
    public bool? IsLoading { get; init; }
    public bool? CanGoBack { get; init; }
    public bool? CanGoForward { get; init; }
    public bool? IsSuspended { get; init; }
    public bool? IsLoaded { get; init; }
    public System.Windows.Media.ImageSource? Favicon { get; init; }
}

public sealed class ServiceNotificationReceivedEventArgs : EventArgs
{
    public required string ServiceId { get; init; }
    public required string ServiceName { get; init; }
    public required string NotificationId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public bool IsSilent { get; init; }
}

/// <summary>
/// Manages per-service isolated WebView2 instances with lazy-loading,
/// Chromium TrySuspendAsync background throttling, and full hibernation disposal.
/// </summary>
public sealed class WebViewHostManager : IDisposable
{
    private readonly Panel _hostContainer;
    private readonly Dictionary<string, WebView2> _webViews = new();
    private readonly Dictionary<string, bool> _initialized = new();
    private readonly Dictionary<string, Task<CoreWebView2Environment>> _environments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CoreWebView2Notification> _activeNotifications = new();
    private readonly Dictionary<string, HashSet<string>> _grantedNotificationOrigins = new();
    private readonly Dictionary<string, string> _serviceProfileFolders = new();
    private readonly Dictionary<string, Dictionary<string, string>> _runtimeExtensionIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _profileSyncLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _profilesRoot;
    private readonly string _extensionsLogPath;
    private BrowserExtensionService? _extensionService;
    private DateTime _suppressExtensionPopupsUntilUtc = DateTime.MinValue;
    private string? _activeServiceId;
    private string _globalUserAgent = string.Empty;
    private string _downloadFolderPath = string.Empty;
    private bool _enableGpuAcceleration = true;
    private bool _useSelfSignedCertificates = false;
    private bool _sandboxServices = false;
    private bool _universalDarkMode = true;
    private bool _isAppDarkTheme = true;
    private bool _splitMode = false;
    private int _splitColumns = 3;
    private string _webRtcIpHandlingPolicy = "disable_non_proxied_udp";
    private string _searchEngine = "startPage";
    private string _locale = "en-US";
    private bool _enableSpellchecking = true;
    private string _spellcheckerLanguage = "en-us";
    private bool _enableTranslator = false;
    private string _translatorEngine = "LibreTranslate";
    private string _translatorLanguage = "en";
    private WebView2? _todosWebView;

    public event EventHandler<ServiceWebViewStateChangedEventArgs>? ServiceStateChanged;
    public event EventHandler<ServiceNotificationReceivedEventArgs>? ServiceNotificationReceived;

    public void ApplyGlobalSettings(AppSettings settings, bool? isDarkTheme = null)
    {
        _globalUserAgent = settings.UserAgentPref ?? string.Empty;
        _downloadFolderPath = settings.DownloadFolderPath ?? string.Empty;
        _enableGpuAcceleration = settings.EnableGpuAcceleration;
        _useSelfSignedCertificates = settings.UseSelfSignedCertificates;
        _sandboxServices = settings.SandboxServices;
        _universalDarkMode = settings.UniversalDarkMode;
        if (isDarkTheme.HasValue) _isAppDarkTheme = isDarkTheme.Value;
        _splitMode = settings.SplitMode;
        _splitColumns = Math.Clamp(settings.SplitColumns, 1, 5);
        _webRtcIpHandlingPolicy = settings.WebRtcIpHandlingPolicy ?? "disable_non_proxied_udp";
        _searchEngine = settings.SearchEngine ?? "startPage";
        _locale = settings.Locale ?? "en-US";
        _enableSpellchecking = settings.EnableSpellchecking;
        _spellcheckerLanguage = settings.SpellcheckerLanguage ?? "en-us";
        _enableTranslator = settings.EnableTranslator;
        _translatorEngine = settings.TranslatorEngine ?? "LibreTranslate";
        _translatorLanguage = settings.TranslatorLanguage ?? "en";

        foreach (var (id, webView) in _webViews)
        {
            if (_initialized.GetValueOrDefault(id) && webView.CoreWebView2?.Profile is not null)
            {
                if (!string.IsNullOrWhiteSpace(_downloadFolderPath) && Directory.Exists(_downloadFolderPath))
                {
                    webView.CoreWebView2.Profile.DefaultDownloadFolderPath = _downloadFolderPath;
                }

                _ = ApplyUniversalDarkModeAsync(webView.CoreWebView2);
            }
        }

        UpdateSplitLayout();

        if (!settings.SuspendBackgroundWebViews)
        {
            ResumeAllSuspended();
        }
    }

    public async Task EnsureTodosWebViewAsync(Panel container, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        if (_todosWebView is null)
        {
            _todosWebView = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.Transparent,
            };
            container.Children.Clear();
            container.Children.Add(_todosWebView);

            try
            {
                var env = await GetOrCreateEnvironmentAsync("_todos");
                await _todosWebView.EnsureCoreWebView2Async(env);
                _todosWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _todosWebView.CoreWebView2.Navigate(url);
            }
            catch
            {
                // Ignore WebView2 init errors for Todos drawer
            }
        }
        else if (_todosWebView.CoreWebView2 is not null &&
                 !string.Equals(_todosWebView.CoreWebView2.Source, url, StringComparison.OrdinalIgnoreCase))
        {
            _todosWebView.CoreWebView2.Navigate(url);
        }
    }

    private void UpdateSplitLayout()
    {
        if (_hostContainer is not Grid hostGrid) return;

        hostGrid.ColumnDefinitions.Clear();

        if (!_splitMode || _webViews.Count <= 1)
        {
            foreach (var (id, wv) in _webViews)
            {
                Grid.SetColumn(wv, 0);
                wv.Visibility = id == _activeServiceId ? Visibility.Visible : Visibility.Collapsed;
            }
            return;
        }

        var visibleIds = new List<string>();
        if (!string.IsNullOrEmpty(_activeServiceId) && _webViews.ContainsKey(_activeServiceId))
        {
            visibleIds.Add(_activeServiceId);
        }
        foreach (var id in _webViews.Keys)
        {
            if (visibleIds.Count >= _splitColumns) break;
            if (!visibleIds.Contains(id))
            {
                visibleIds.Add(id);
            }
        }

        for (int i = 0; i < visibleIds.Count; i++)
        {
            hostGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        foreach (var (id, wv) in _webViews)
        {
            var col = visibleIds.IndexOf(id);
            if (col >= 0)
            {
                Grid.SetColumn(wv, col);
                wv.Visibility = Visibility.Visible;
                if (_initialized.GetValueOrDefault(id))
                {
                    wv.CoreWebView2?.Resume();
                }
            }
            else
            {
                wv.Visibility = Visibility.Collapsed;
            }
        }
    }

    private async Task ApplyUniversalDarkModeAsync(CoreWebView2 core)
    {
        try
        {
            var enable = _universalDarkMode && _isAppDarkTheme;
            var js = enable
                ? "(function(){let s=document.getElementById('ferdium-udm');if(!s){s=document.createElement('style');s.id='ferdium-udm';s.textContent=':root{color-scheme:dark;}';document.head&&document.head.appendChild(s);}})();"
                : "(function(){let s=document.getElementById('ferdium-udm');if(s)s.remove();})();";
            await core.ExecuteScriptAsync(js);
        }
        catch
        {
            // Ignore script errors on restricted pages
        }
    }

    public void ResumeAllSuspended()
    {
        foreach (var (id, webView) in _webViews)
        {
            if (_initialized.GetValueOrDefault(id) && webView.CoreWebView2 is not null)
            {
                try
                {
                    webView.CoreWebView2.Resume();
                    ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
                    {
                        ServiceId = id,
                        IsSuspended = false,
                    });
                }
                catch
                {
                    // Ignore if already active or tearing down
                }
            }
        }
    }

    public async Task ClearAllCacheAsync()
    {
        foreach (var (id, webView) in _webViews.ToList())
        {
            if (_initialized.GetValueOrDefault(id) && webView.CoreWebView2?.Profile is not null)
            {
                try
                {
                    await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage);
                }
                catch
                {
                    // Ignore teardown races
                }
            }
        }
    }

    public void ReloadAllLoaded()
    {
        foreach (var (id, webView) in _webViews.ToList())
        {
            if (_initialized.GetValueOrDefault(id) && webView.CoreWebView2 is not null)
            {
                try
                {
                    webView.CoreWebView2.Reload();
                }
                catch
                {
                    // Ignore reload errors
                }
            }
        }
    }

    public WebViewHostManager(Panel hostContainer)
    {
        _hostContainer = hostContainer;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _profilesRoot = Path.Combine(localAppData, "FerdiumPlus", "Profiles");
        _extensionsLogPath = Path.Combine(localAppData, "FerdiumPlus", "extensions.log");
        Directory.CreateDirectory(_profilesRoot);
    }

    private void LogExtensionInfo(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(_extensionsLogPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.AppendAllText(_extensionsLogPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignore logging failures
        }
    }

    public void SetExtensionService(BrowserExtensionService extensionService)
    {
        _extensionService = extensionService;
    }

    public bool IsServiceLoaded(string serviceId) => _webViews.ContainsKey(serviceId);

    /// <summary>
    /// Initializes and navigates a service's WebView2 silently in the background (Visibility.Collapsed)
    /// without changing the active visible service. Used for Load-on-Startup services, including locked ones,
    /// so they can receive notifications without ever exposing their UI before unlock.
    /// </summary>
    public async Task PreloadServiceInBackgroundAsync(ServiceConfig service, bool isGlobalMuted)
    {
        if (_webViews.ContainsKey(service.Id))
        {
            return;
        }

        await CreateWebViewForServiceAsync(service, isGlobalMuted);
    }

    public async Task ActivateServiceAsync(
        ServiceConfig service,
        bool isGlobalMuted,
        bool suspendBackgroundWebViews)
    {
        var previousServiceId = _activeServiceId;
        _activeServiceId = service.Id;

        WebView2? prevWebViewToSuspend = null;
        if (!string.IsNullOrEmpty(previousServiceId) &&
            previousServiceId != service.Id &&
            _webViews.TryGetValue(previousServiceId, out var prevWebView))
        {
            prevWebView.Visibility = Visibility.Collapsed;
            if (suspendBackgroundWebViews &&
                _initialized.GetValueOrDefault(previousServiceId) &&
                prevWebView.CoreWebView2 is not null)
            {
                prevWebViewToSuspend = prevWebView;
            }
        }

        if (!_webViews.TryGetValue(service.Id, out var webView))
        {
            webView = await CreateWebViewForServiceAsync(service, isGlobalMuted);
        }
        else
        {
            webView.Visibility = _activeServiceId == service.Id
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (_activeServiceId == service.Id &&
                _initialized.GetValueOrDefault(service.Id) &&
                webView.CoreWebView2 is not null)
            {
                webView.CoreWebView2.Resume();
                webView.CoreWebView2.IsMuted = isGlobalMuted || service.IsAudioEffectivelyMuted;
                ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
                {
                    ServiceId = service.Id,
                    IsSuspended = false,
                    CurrentUrl = webView.CoreWebView2.Source,
                    CanGoBack = webView.CoreWebView2.CanGoBack,
                    CanGoForward = webView.CoreWebView2.CanGoForward,
                });
            }
        }

        if (_splitMode)
        {
            UpdateSplitLayout();
        }
        else if (prevWebViewToSuspend?.CoreWebView2 is not null &&
            !string.IsNullOrEmpty(previousServiceId) &&
            _activeServiceId != previousServiceId)
        {
            try
            {
                var suspended = await prevWebViewToSuspend.CoreWebView2.TrySuspendAsync();
                ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
                {
                    ServiceId = previousServiceId,
                    IsSuspended = suspended,
                });
            }
            catch
            {
                // Suspension can fail if audio/capture is active; safe to ignore
            }
        }
    }

    private async Task<WebView2> CreateWebViewForServiceAsync(ServiceConfig service, bool isGlobalMuted)
    {
        var webView = new WebView2
        {
            Visibility = _activeServiceId == service.Id ? Visibility.Visible : Visibility.Collapsed,
            DefaultBackgroundColor = System.Drawing.Color.Transparent,
        };

        _webViews[service.Id] = webView;
        _initialized[service.Id] = false;
        _hostContainer.Children.Add(webView);

        ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
        {
            ServiceId = service.Id,
            IsLoaded = true,
            IsLoading = true,
            IsSuspended = false,
            CurrentUrl = service.Url,
        });

        try
        {
            var profileFolderName = ResolveProfileFolderName(service, _sandboxServices);
            _serviceProfileFolders[service.Id] = profileFolderName;
            var environment = await GetOrCreateEnvironmentAsync(profileFolderName);
            await webView.EnsureCoreWebView2Async(environment);

            if (!_webViews.ContainsKey(service.Id))
            {
                // Service was hibernated/removed while initializing
                webView.Dispose();
                return webView;
            }

            _initialized[service.Id] = true;
            webView.Visibility = _activeServiceId == service.Id
                ? Visibility.Visible
                : Visibility.Collapsed;
            var core = webView.CoreWebView2;

            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.IsMuted = isGlobalMuted || service.IsAudioEffectivelyMuted;

            if (!string.IsNullOrWhiteSpace(_downloadFolderPath) && Directory.Exists(_downloadFolderPath) && core.Profile is not null)
            {
                core.Profile.DefaultDownloadFolderPath = _downloadFolderPath;
            }

            var effectiveUserAgent = !string.IsNullOrWhiteSpace(service.CustomUserAgent)
                ? service.CustomUserAgent
                : _globalUserAgent;
            if (!string.IsNullOrWhiteSpace(effectiveUserAgent))
            {
                core.Settings.UserAgent = effectiveUserAgent;
            }

            core.DocumentTitleChanged += (_, _) =>
            {
                ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
                {
                    ServiceId = service.Id,
                    Title = core.DocumentTitle,
                });
            };

            core.FaviconChanged += async (_, _) =>
            {
                try
                {
                    using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                    if (stream is not null)
                    {
                        var icon = ServiceIconService.SaveAndLoadFaviconStream(service.CatalogId, service.Url, stream);
                        if (icon is not null)
                        {
                            ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
                            {
                                ServiceId = service.Id,
                                Favicon = icon,
                            });
                        }
                    }
                }
                catch
                {
                    // Safe to ignore if favicon stream is unavailable
                }
            };

            core.NavigationStarting += (_, args) =>
            {
                ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
                {
                    ServiceId = service.Id,
                    IsLoading = true,
                    CurrentUrl = args.Uri,
                });
            };

            core.NavigationCompleted += async (_, _) =>
            {
                await EnsureNotificationPermissionAsync(core, service.Id, core.Source);
                await ApplyUniversalDarkModeAsync(core);

                ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
                {
                    ServiceId = service.Id,
                    IsLoading = false,
                    CurrentUrl = core.Source,
                    Title = core.DocumentTitle,
                    CanGoBack = core.CanGoBack,
                    CanGoForward = core.CanGoForward,
                });
            };

            core.ContextMenuRequested += (_, args) =>
            {
                var selection = args.ContextMenuTarget.SelectionText;
                if (string.IsNullOrWhiteSpace(selection)) return;

                var searchItem = core.Environment.CreateContextMenuItem(
                    $"Search with {_searchEngine}",
                    null,
                    CoreWebView2ContextMenuItemKind.Command);
                searchItem.CustomItemSelected += (_, _) =>
                {
                    OpenExternalUrl(PrivacyAndShellHelpers.BuildSearchUrl(selection, _searchEngine));
                };
                args.MenuItems.Add(searchItem);

                if (_enableTranslator)
                {
                    var translateItem = core.Environment.CreateContextMenuItem(
                        $"Translate selection ({_translatorLanguage})",
                        null,
                        CoreWebView2ContextMenuItemKind.Command);
                    translateItem.CustomItemSelected += (_, _) =>
                    {
                        OpenExternalUrl(PrivacyAndShellHelpers.BuildTranslateUrl(selection, _translatorEngine, _translatorLanguage));
                    };
                    args.MenuItems.Add(translateItem);
                }
            };

            core.PermissionRequested += (_, args) =>
            {
                if (args.PermissionKind == CoreWebView2PermissionKind.Notifications)
                {
                    args.State = CoreWebView2PermissionState.Allow;
                    args.SavesInProfile = true;
                    args.Handled = true;
                }
            };

            core.NotificationReceived += (_, args) =>
            {
                args.Handled = true;
                var notification = args.Notification;
                var notificationId = Guid.NewGuid().ToString("N");
                _activeNotifications[notificationId] = notification;

                notification.CloseRequested += (_, _) =>
                {
                    _activeNotifications.Remove(notificationId);
                };

                try
                {
                    notification.ReportShown();
                }
                catch
                {
                    // Ignore if WebView2 is tearing down
                }

                ServiceNotificationReceived?.Invoke(this, new ServiceNotificationReceivedEventArgs
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    NotificationId = notificationId,
                    Title = notification.Title ?? string.Empty,
                    Body = notification.Body ?? string.Empty,
                    Tag = notification.Tag ?? string.Empty,
                    IsSilent = notification.IsSilent,
                });
            };

            await EnsureNotificationPermissionAsync(core, service.Id, service.Url);

            core.SourceChanged += (_, _) =>
            {
                ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
                {
                    ServiceId = service.Id,
                    CurrentUrl = core.Source,
                    CanGoBack = core.CanGoBack,
                    CanGoForward = core.CanGoForward,
                });
            };

            core.NewWindowRequested += (_, args) =>
            {
                if (!args.IsUserInitiated && DateTime.UtcNow < _suppressExtensionPopupsUntilUtc)
                {
                    args.Handled = true;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(args.Uri))
                {
                    // Keep same-host popups (e.g., OAuth/SSO) in-app; open external links in default browser
                    if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var targetUri) &&
                        Uri.TryCreate(service.Url, UriKind.Absolute, out var serviceUri) &&
                        !string.Equals(targetUri.Host, serviceUri.Host, StringComparison.OrdinalIgnoreCase) &&
                        !targetUri.Host.Contains("login", StringComparison.OrdinalIgnoreCase) &&
                        !targetUri.Host.Contains("auth", StringComparison.OrdinalIgnoreCase) &&
                        !targetUri.Host.Contains("accounts.google", StringComparison.OrdinalIgnoreCase) &&
                        !targetUri.Host.Contains("microsoftonline", StringComparison.OrdinalIgnoreCase))
                    {
                        args.Handled = true;
                        OpenExternalUrl(args.Uri);
                    }
                }
            };

            core.Navigate(service.Url);

            if (core.Profile is not null)
            {
                _ = SyncExtensionsToProfileAsync(profileFolderName, core.Profile);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView2 init failed for {service.Name}: {ex.Message}");
            LogExtensionInfo($"WebView2 init failed for {service.Name}: {ex}");
            ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
            {
                ServiceId = service.Id,
                IsLoading = false,
            });
        }

        return webView;
    }

    public void HideService(string serviceId)
    {
        if (_webViews.TryGetValue(serviceId, out var webView))
        {
            webView.Visibility = Visibility.Collapsed;
        }

        if (_activeServiceId == serviceId)
        {
            _activeServiceId = null;
        }
    }

    public void ResumeActiveService()
    {
        if (string.IsNullOrEmpty(_activeServiceId)) return;
        if (_webViews.TryGetValue(_activeServiceId, out var webView))
        {
            webView.Visibility = Visibility.Visible;
            if (_initialized.GetValueOrDefault(_activeServiceId) && webView.CoreWebView2 is not null)
            {
                try
                {
                    webView.CoreWebView2.Resume();
                }
                catch
                {
                    // Safe to ignore if already active
                }
            }
        }
    }

    public async Task<System.Windows.Media.ImageSource?> CaptureActivePreviewAsync()
    {
        if (string.IsNullOrEmpty(_activeServiceId)) return null;
        if (!_webViews.TryGetValue(_activeServiceId, out var webView) ||
            !_initialized.GetValueOrDefault(_activeServiceId) ||
            webView.CoreWebView2 is null ||
            webView.Visibility != Visibility.Visible)
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream();
            await webView.CoreWebView2.CapturePreviewAsync(
                CoreWebView2CapturePreviewImageFormat.Png,
                ms);
            ms.Position = 0;

            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 960;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public async Task HibernateServiceAsync(string serviceId, bool trimMemory)
    {
        if (!_webViews.TryGetValue(serviceId, out var webView))
        {
            return;
        }

        if (trimMemory && _initialized.GetValueOrDefault(serviceId) && webView.CoreWebView2?.Profile is not null)
        {
            try
            {
                // Clear HTTP/disk cache while preserving cookies, localStorage, and logins
                await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage);
            }
            catch
            {
                // Safe to ignore if WebView2 is already tearing down
            }
        }

        _webViews.Remove(serviceId);
        _initialized.Remove(serviceId);
        _grantedNotificationOrigins.Remove(serviceId);
        _hostContainer.Children.Remove(webView);
        webView.Dispose();

        if (_activeServiceId == serviceId)
        {
            _activeServiceId = null;
        }

        ServiceStateChanged?.Invoke(this, new ServiceWebViewStateChangedEventArgs
        {
            ServiceId = serviceId,
            IsLoaded = false,
            IsSuspended = false,
            IsLoading = false,
        });

        if (trimMemory)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
        }
    }

    public void UpdateMuteState(string serviceId, bool isMuted)
    {
        if (_webViews.TryGetValue(serviceId, out var webView) &&
            _initialized.GetValueOrDefault(serviceId) &&
            webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.IsMuted = isMuted;
        }
    }

    public void UpdateAllMuteStates(IEnumerable<ServiceConfig> services, bool isGlobalMuted)
    {
        foreach (var service in services)
        {
            UpdateMuteState(service.Id, isGlobalMuted || service.IsAudioEffectivelyMuted);
        }
    }

    public void GoBack(string serviceId)
    {
        if (TryGetCore(serviceId, out var core) && core.CanGoBack)
        {
            core.GoBack();
        }
    }

    public void GoForward(string serviceId)
    {
        if (TryGetCore(serviceId, out var core) && core.CanGoForward)
        {
            core.GoForward();
        }
    }

    public void Reload(string serviceId)
    {
        if (TryGetCore(serviceId, out var core))
        {
            core.Reload();
        }
    }

    public void Navigate(string serviceId, string url)
    {
        if (TryGetCore(serviceId, out var core))
        {
            core.Navigate(ServiceCatalogService.NormalizeUrl(url));
        }
    }

    public void OpenDevTools(string serviceId)
    {
        if (TryGetCore(serviceId, out var core))
        {
            core.OpenDevToolsWindow();
        }
    }

    public static void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Ignore shell launch errors
        }
    }

    public void ReportNotificationClicked(string? notificationId)
    {
        if (string.IsNullOrWhiteSpace(notificationId))
        {
            return;
        }

        if (_activeNotifications.Remove(notificationId, out var notification))
        {
            try
            {
                notification.ReportClicked();
            }
            catch
            {
                // Ignore if notification or WebView2 is already disposed
            }
        }
    }

    private async Task EnsureNotificationPermissionAsync(CoreWebView2 core, string serviceId, string? url)
    {
        if (core.Profile is null || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return;
        }

        var origin = uri.GetLeftPart(UriPartial.Authority);
        if (!_grantedNotificationOrigins.TryGetValue(serviceId, out var origins))
        {
            origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _grantedNotificationOrigins[serviceId] = origins;
        }

        if (!origins.Add(origin))
        {
            return;
        }

        try
        {
            await core.Profile.SetPermissionStateAsync(
                CoreWebView2PermissionKind.Notifications,
                origin,
                CoreWebView2PermissionState.Allow);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set notification permission for {origin}: {ex.Message}");
        }
    }

    public static string ResolveProfileFolderName(ServiceConfig service) =>
        ResolveProfileFolderName(service, sandboxServices: false);

    public static string ResolveProfileFolderName(ServiceConfig service, bool sandboxServices)
    {
        if (string.IsNullOrWhiteSpace(service.SessionProfileKey))
        {
            return sandboxServices ? SanitizeProfileKey(service.Id) : "_shared";
        }

        var trimmed = service.SessionProfileKey.Trim();
        if (string.Equals(trimmed, "shared", StringComparison.OrdinalIgnoreCase))
        {
            return "_shared";
        }

        if (string.Equals(trimmed, "isolated", StringComparison.OrdinalIgnoreCase))
        {
            return SanitizeProfileKey(service.Id);
        }

        return SanitizeProfileKey(trimmed);
    }

    private static string SanitizeProfileKey(string key)
    {
        var sanitized = string.Concat(key.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
        return string.IsNullOrWhiteSpace(sanitized) ? "default" : sanitized;
    }

    private Task<CoreWebView2Environment> GetOrCreateEnvironmentAsync(string profileFolderName)
    {
        if (_environments.TryGetValue(profileFolderName, out var existingTask))
        {
            return existingTask;
        }

        var profileDir = Path.Combine(_profilesRoot, profileFolderName);
        Directory.CreateDirectory(profileDir);

        var browserArgs = "--enable-features=msWebOOUI,msPdfOOUI --disable-backgrounding-occluded-windows --disable-renderer-backgrounding";
        if (!_enableGpuAcceleration)
        {
            browserArgs += " --disable-gpu";
        }
        if (_useSelfSignedCertificates)
        {
            browserArgs += " --ignore-certificate-errors";
        }
        if (!string.IsNullOrWhiteSpace(_webRtcIpHandlingPolicy) &&
            !string.Equals(_webRtcIpHandlingPolicy, "default", StringComparison.OrdinalIgnoreCase))
        {
            browserArgs += $" --force-webrtc-ip-handling-policy={_webRtcIpHandlingPolicy}";
        }
        if (!_enableSpellchecking)
        {
            browserArgs += " --disable-spell-checking";
        }

        var envOptions = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = browserArgs,
            AreBrowserExtensionsEnabled = true,
            Language = !string.IsNullOrWhiteSpace(_locale) ? _locale : null,
        };

        var createTask = CoreWebView2Environment.CreateAsync(null, profileDir, envOptions);
        _environments[profileFolderName] = createTask;

        _ = createTask.ContinueWith(
            t =>
            {
                if (t.IsFaulted || t.IsCanceled)
                {
                    _environments.Remove(profileFolderName);
                }
            },
            TaskScheduler.FromCurrentSynchronizationContext());

        return createTask;
    }

    public async Task SyncExtensionsAcrossAllLoadedProfilesAsync()
    {
        if (_extensionService is null)
        {
            return;
        }

        var syncedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (serviceId, webView) in _webViews.ToList())
        {
            if (!_initialized.GetValueOrDefault(serviceId) || webView.CoreWebView2?.Profile is null)
            {
                continue;
            }

            var profileFolder = _serviceProfileFolders.GetValueOrDefault(serviceId, serviceId);
            if (syncedProfiles.Add(profileFolder))
            {
                await SyncExtensionsToProfileAsync(profileFolder, webView.CoreWebView2.Profile);
            }
        }
    }

    public string? GetActiveServiceUrl()
    {
        if (!string.IsNullOrEmpty(_activeServiceId) &&
            TryGetCore(_activeServiceId, out var activeCore) &&
            !string.IsNullOrWhiteSpace(activeCore.Source))
        {
            return activeCore.Source;
        }

        foreach (var (serviceId, webView) in _webViews)
        {
            if (_initialized.GetValueOrDefault(serviceId) &&
                webView.CoreWebView2 is not null &&
                !string.IsNullOrWhiteSpace(webView.CoreWebView2.Source))
            {
                return webView.CoreWebView2.Source;
            }
        }

        return null;
    }

    public async Task<(CoreWebView2Environment Environment, string Url)?> ResolveExtensionPageTargetAsync(
        BrowserExtensionConfig extension,
        bool openOptions = false)
    {
        var relativePage = openOptions
            ? extension.OptionsPath
            : extension.PopupPath ?? extension.OptionsPath;
        if (string.IsNullOrWhiteSpace(relativePage))
        {
            return null;
        }

        string profileFolder = "_shared";
        CoreWebView2Profile? activeProfile = null;

        if (!string.IsNullOrEmpty(_activeServiceId) &&
            TryGetCore(_activeServiceId, out var activeCore) &&
            activeCore.Profile is not null)
        {
            profileFolder = _serviceProfileFolders.GetValueOrDefault(_activeServiceId, _activeServiceId);
            activeProfile = activeCore.Profile;
        }
        else
        {
            foreach (var (serviceId, webView) in _webViews)
            {
                if (_initialized.GetValueOrDefault(serviceId) && webView.CoreWebView2?.Profile is not null)
                {
                    profileFolder = _serviceProfileFolders.GetValueOrDefault(serviceId, serviceId);
                    activeProfile = webView.CoreWebView2.Profile;
                    break;
                }
            }
        }

        var environment = await GetOrCreateEnvironmentAsync(profileFolder);

        string? runtimeId = null;
        if (activeProfile is not null)
        {
            await SyncExtensionsToProfileAsync(profileFolder, activeProfile);
            if (_runtimeExtensionIds.TryGetValue(profileFolder, out var map))
            {
                map.TryGetValue(extension.Id, out runtimeId);
            }
        }

        if (string.IsNullOrWhiteSpace(runtimeId))
        {
            // Fallback: if WebStore ID is present, Chromium uses it as the runtime extension ID
            // when the CRX includes its manifest key.
            runtimeId = extension.WebStoreId;
        }

        if (string.IsNullOrWhiteSpace(runtimeId))
        {
            return null;
        }

        var cleanRelative = relativePage.TrimStart('/');
        return (environment, $"chrome-extension://{runtimeId}/{cleanRelative}");
    }

    private async Task SyncExtensionsToProfileAsync(string profileFolderName, CoreWebView2Profile profile)
    {
        if (_extensionService is null)
        {
            return;
        }

        if (!_profileSyncLocks.TryGetValue(profileFolderName, out var syncLock))
        {
            syncLock = new SemaphoreSlim(1, 1);
            _profileSyncLocks[profileFolderName] = syncLock;
        }

        await syncLock.WaitAsync();
        try
        {
            if (!_runtimeExtensionIds.TryGetValue(profileFolderName, out var profileMap))
            {
                profileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _runtimeExtensionIds[profileFolderName] = profileMap;
            }

            IReadOnlyList<CoreWebView2BrowserExtension> loadedExtensions;
            try
            {
                loadedExtensions = await profile.GetBrowserExtensionsAsync();
            }
            catch (Exception ex)
            {
                LogExtensionInfo($"GetBrowserExtensionsAsync failed for profile {profileFolderName}: {ex.Message}");
                return;
            }

            var desired = _extensionService.GetAll();
            var loadedById = loadedExtensions.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);
            var activeRuntimeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var ext in desired)
            {
                if (string.IsNullOrWhiteSpace(ext.UnpackedPath) || !Directory.Exists(ext.UnpackedPath))
                {
                    continue;
                }

                CoreWebView2BrowserExtension? runtimeExt = null;

                if (profileMap.TryGetValue(ext.Id, out var knownRuntimeId) &&
                    loadedById.TryGetValue(knownRuntimeId, out var foundByKnown))
                {
                    runtimeExt = foundByKnown;
                }
                else if (!string.IsNullOrWhiteSpace(ext.WebStoreId) &&
                         loadedById.TryGetValue(ext.WebStoreId, out var foundByStoreId))
                {
                    runtimeExt = foundByStoreId;
                    profileMap[ext.Id] = runtimeExt.Id;
                }
                else
                {
                    runtimeExt = loadedExtensions.FirstOrDefault(le =>
                        string.Equals(le.Name, ext.Name, StringComparison.OrdinalIgnoreCase));
                    if (runtimeExt is not null)
                    {
                        profileMap[ext.Id] = runtimeExt.Id;
                    }
                }

                if (runtimeExt is null)
                {
                    if (!ext.IsEnabled)
                    {
                        continue;
                    }

                    try
                    {
                        _suppressExtensionPopupsUntilUtc = DateTime.UtcNow.AddSeconds(8);
                        runtimeExt = await profile.AddBrowserExtensionAsync(ext.UnpackedPath);
                        profileMap[ext.Id] = runtimeExt.Id;
                        loadedById[runtimeExt.Id] = runtimeExt;
                        LogExtensionInfo($"Loaded extension '{ext.Name}' ({runtimeExt.Id}) into profile {profileFolderName}");
                    }
                    catch (Exception ex)
                    {
                        LogExtensionInfo($"AddBrowserExtensionAsync failed for '{ext.Name}' in {profileFolderName}: {ex.Message}");
                        continue;
                    }
                }

                activeRuntimeIds.Add(runtimeExt.Id);

                if (runtimeExt.IsEnabled != ext.IsEnabled)
                {
                    try
                    {
                        await runtimeExt.EnableAsync(ext.IsEnabled);
                    }
                    catch (Exception ex)
                    {
                        LogExtensionInfo($"EnableAsync failed for '{ext.Name}': {ex.Message}");
                    }
                }
            }

            // Only remove extensions that were previously managed by Ferdium (never touch built-in Edge extensions)
            var removedConfigIds = profileMap
                .Where(kvp => !desired.Any(d => string.Equals(d.Id, kvp.Key, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            foreach (var (removedConfigId, runtimeId) in removedConfigIds)
            {
                if (loadedById.TryGetValue(runtimeId, out var loaded))
                {
                    try
                    {
                        _suppressExtensionPopupsUntilUtc = DateTime.UtcNow.AddSeconds(8);
                        await loaded.RemoveAsync();
                    }
                    catch (Exception ex)
                    {
                        LogExtensionInfo($"RemoveAsync failed for '{loaded.Name}': {ex.Message}");
                    }
                }
                profileMap.Remove(removedConfigId);
            }
        }
        finally
        {
            syncLock.Release();
        }
    }

    private bool TryGetCore(string serviceId, out CoreWebView2 core)
    {
        if (_webViews.TryGetValue(serviceId, out var webView) &&
            _initialized.GetValueOrDefault(serviceId) &&
            webView.CoreWebView2 is not null)
        {
            core = webView.CoreWebView2;
            return true;
        }

        core = null!;
        return false;
    }

    public void Dispose()
    {
        foreach (var webView in _webViews.Values)
        {
            _hostContainer.Children.Remove(webView);
            webView.Dispose();
        }

        _todosWebView?.Dispose();
        _todosWebView = null;

        _webViews.Clear();
        _initialized.Clear();
        _environments.Clear();
        _activeNotifications.Clear();
        _grantedNotificationOrigins.Clear();
    }
}
