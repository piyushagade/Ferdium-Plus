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
    private string? _activeServiceId;
    private string _globalUserAgent = string.Empty;
    private string _downloadFolderPath = string.Empty;
    private bool _enableGpuAcceleration = true;
    private bool _useSelfSignedCertificates = false;

    public event EventHandler<ServiceWebViewStateChangedEventArgs>? ServiceStateChanged;
    public event EventHandler<ServiceNotificationReceivedEventArgs>? ServiceNotificationReceived;

    public void ApplyGlobalSettings(AppSettings settings)
    {
        _globalUserAgent = settings.UserAgentPref ?? string.Empty;
        _downloadFolderPath = settings.DownloadFolderPath ?? string.Empty;
        _enableGpuAcceleration = settings.EnableGpuAcceleration;
        _useSelfSignedCertificates = settings.UseSelfSignedCertificates;

        foreach (var (id, webView) in _webViews)
        {
            if (_initialized.GetValueOrDefault(id) && webView.CoreWebView2?.Profile is not null)
            {
                if (!string.IsNullOrWhiteSpace(_downloadFolderPath) && Directory.Exists(_downloadFolderPath))
                {
                    webView.CoreWebView2.Profile.DefaultDownloadFolderPath = _downloadFolderPath;
                }
            }
        }

        if (!settings.SuspendBackgroundWebViews)
        {
            ResumeAllSuspended();
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

        if (prevWebViewToSuspend?.CoreWebView2 is not null &&
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
            var profileFolderName = ResolveProfileFolderName(service);
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

    public static string ResolveProfileFolderName(ServiceConfig service)
    {
        if (string.IsNullOrWhiteSpace(service.SessionProfileKey))
        {
            return "_shared";
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

        var browserArgs = "--enable-features=msWebOOUI,msPdfOOUI";
        if (!_enableGpuAcceleration)
        {
            browserArgs += " --disable-gpu";
        }
        if (_useSelfSignedCertificates)
        {
            browserArgs += " --ignore-certificate-errors";
        }

        var envOptions = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = browserArgs,
            AreBrowserExtensionsEnabled = true,
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

        _webViews.Clear();
        _initialized.Clear();
        _environments.Clear();
        _activeNotifications.Clear();
        _grantedNotificationOrigins.Clear();
    }
}
