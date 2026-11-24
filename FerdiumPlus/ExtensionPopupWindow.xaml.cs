using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace FerdiumApp;

public partial class ExtensionPopupWindow : Window
{
    private readonly CoreWebView2Environment _environment;
    private readonly string _targetUrl;
    private readonly string? _activeServiceUrl;
    private readonly bool _closeOnDeactivate;
    private Rect? _anchorRect;
    private bool _isClosing;

    public string ExtensionId { get; }

    public ExtensionPopupWindow(
        string extensionId,
        string title,
        CoreWebView2Environment environment,
        string targetUrl,
        bool closeOnDeactivate = true,
        string? activeServiceUrl = null)
    {
        InitializeComponent();

        ExtensionId = extensionId;
        Title = title;
        _environment = environment;
        _targetUrl = targetUrl;
        _activeServiceUrl = activeServiceUrl;
        _closeOnDeactivate = closeOnDeactivate;

        if (!closeOnDeactivate)
        {
            WindowChrome.SetWindowChrome(this, null);
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = true;
            Width = 680;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        Loaded += ExtensionPopupWindow_Loaded;
        Deactivated += ExtensionPopupWindow_Deactivated;
        Closed += ExtensionPopupWindow_Closed;
    }

    public void PositionBelowAnchor(Rect anchorScreenDipRect)
    {
        _anchorRect = anchorScreenDipRect;
        ApplyAnchorPosition();
    }

    private void ApplyAnchorPosition()
    {
        if (_anchorRect is not Rect anchor)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var left = anchor.Left;
        var top = anchor.Bottom + 4;

        if (left + Width > workArea.Right - 8)
        {
            left = Math.Max(workArea.Left + 8, anchor.Right - Width);
        }
        if (left < workArea.Left + 8)
        {
            left = workArea.Left + 8;
        }
        if (top + Height > workArea.Bottom - 8)
        {
            top = Math.Max(workArea.Top + 8, workArea.Bottom - Height - 8);
        }

        Left = left;
        Top = top;
    }

    private async void ExtensionPopupWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await PopupWebView.EnsureCoreWebView2Async(_environment);
            if (_isClosing || PopupWebView.CoreWebView2 is null)
            {
                return;
            }

            var core = PopupWebView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;

            if (_closeOnDeactivate)
            {
                core.WebMessageReceived += PopupWebView_WebMessageReceived;
            }

            var activeUrlJson = JsonSerializer.Serialize(_activeServiceUrl ?? string.Empty);
            var autoSizeFlag = _closeOnDeactivate ? "true" : "false";

            await core.AddScriptToExecuteOnDocumentCreatedAsync($$"""
(() => {
  const activeUrl = {{activeUrlJson}};
  const autoSize = {{autoSizeFlag}};

  // 1. Share the active service URL with the extension's background service worker
  try {
    if (activeUrl && globalThis.chrome && chrome.storage && chrome.storage.local) {
      chrome.storage.local.set({ __ferdium_active_tab_url: activeUrl });
    }
  } catch (_) {}

  // 2. Bridge chrome.tabs.query({ active: true, ... }) inside the popup itself
  try {
    const tabsApi = globalThis.chrome && chrome.tabs;
    if (tabsApi && typeof tabsApi.query === 'function') {
      const isServiceTab = (t) => t && typeof t.url === 'string' && /^https?:\/\//i.test(t.url);
      const makeSyntheticTab = (url) => ({
        id: 1,
        index: 0,
        windowId: 1,
        active: true,
        highlighted: true,
        selected: true,
        pinned: false,
        incognito: false,
        status: 'complete',
        url,
        title: '',
      });
      const pickBestTab = (allTabs) => {
        const candidates = (allTabs || []).filter(isServiceTab);
        if (candidates.length > 0) {
          if (activeUrl) {
            const exact = candidates.find((t) => t.url === activeUrl);
            if (exact) return Object.assign({}, exact, { active: true });
            try {
              const origin = new URL(activeUrl).origin;
              const sameOrigin = candidates.find((t) => {
                try { return new URL(t.url).origin === origin; } catch (_) { return false; }
              });
              if (sameOrigin) return Object.assign({}, sameOrigin, { active: true });
            } catch (_) {}
          }
          return Object.assign({}, candidates.find((t) => t.active) || candidates[0], { active: true });
        }
        return (activeUrl && /^https?:\/\//i.test(activeUrl)) ? makeSyntheticTab(activeUrl) : null;
      };

      const origQuery = tabsApi.query.bind(tabsApi);
      const wrappedQuery = (queryInfo, cb) => {
        const wantsActive = Boolean(queryInfo && queryInfo.active);
        const run = () => new Promise((resolve) => {
          const finish = (tabs) => {
            if (!wantsActive || (Array.isArray(tabs) && tabs.some(isServiceTab))) {
              resolve(tabs || []);
              return;
            }
            try {
              const allMaybe = origQuery({}, (allTabs) => {
                const best = pickBestTab(allTabs);
                resolve(best ? [Object.assign({}, best, { active: true })] : (tabs || []));
              });
              if (allMaybe && typeof allMaybe.then === 'function') {
                allMaybe.then((allTabs) => {
                  const best = pickBestTab(allTabs);
                  resolve(best ? [Object.assign({}, best, { active: true })] : (tabs || []));
                }).catch(() => resolve(tabs || []));
              }
            } catch (_) {
              resolve(tabs || []);
            }
          };
          try {
            const maybe = origQuery(queryInfo || {}, (tabs) => finish(tabs));
            if (maybe && typeof maybe.then === 'function') {
              maybe.then((tabs) => finish(tabs)).catch(() => resolve([]));
            }
          } catch (_) {
            resolve([]);
          }
        });
        const promise = run();
        if (typeof cb === 'function') {
          promise.then((res) => { try { cb(res); } catch (_) {} });
          return;
        }
        return promise;
      };
      Object.defineProperty(tabsApi, 'query', { value: wrappedQuery, configurable: true, writable: true });
    }
  } catch (_) {}

  // 3. Auto-fit popup window to the extension's rendered content dimensions
  if (autoSize && globalThis.chrome && chrome.webview && typeof chrome.webview.postMessage === 'function') {
    let lastW = 0;
    let lastH = 0;
    const reportSize = () => {
      try {
        const body = document.body;
        const docEl = document.documentElement;
        if (!body || !docEl) return;
        const bodyRect = body.getBoundingClientRect();
        const firstChild = body.firstElementChild;
        const childRect = firstChild ? firstChild.getBoundingClientRect() : null;
        const rawW = Math.ceil(
          (childRect && childRect.width > 120 && childRect.width < bodyRect.width - 20)
            ? childRect.width
            : Math.max(bodyRect.width, body.scrollWidth)
        );
        const rawH = Math.ceil(Math.max(bodyRect.height, body.scrollHeight, docEl.scrollHeight));
        if (rawW >= 180 && rawH >= 150 && (Math.abs(rawW - lastW) > 4 || Math.abs(rawH - lastH) > 4)) {
          lastW = rawW;
          lastH = rawH;
          chrome.webview.postMessage({ type: 'ferdium-popup-size', width: rawW, height: rawH });
        }
      } catch (_) {}
    };

    window.addEventListener('DOMContentLoaded', () => {
      setTimeout(reportSize, 60);
      setTimeout(reportSize, 250);
      if (typeof ResizeObserver === 'function' && document.body) {
        const ro = new ResizeObserver(() => reportSize());
        ro.observe(document.body);
      }
    });
    window.addEventListener('load', () => setTimeout(reportSize, 80));
  }
})();
""");

            core.NewWindowRequested += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Uri))
                {
                    return;
                }

                if (args.Uri.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
                {
                    args.Handled = true;
                    Dispatcher.Invoke(() =>
                    {
                        var child = new ExtensionPopupWindow(
                            ExtensionId,
                            Title,
                            _environment,
                            args.Uri,
                            closeOnDeactivate: false,
                            activeServiceUrl: _activeServiceUrl)
                        {
                            Owner = Owner,
                        };
                        child.Show();
                    });
                }
                else if (args.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                         args.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    args.Handled = true;
                    try
                    {
                        Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
                    }
                    catch
                    {
                        // Ignore shell open failures
                    }
                }
            };

            core.Navigate(_targetUrl);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize extension popup WebView2: {ex.Message}");
        }
    }

    private void PopupWebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_isClosing || !_closeOnDeactivate)
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl) ||
                !string.Equals(typeEl.GetString(), "ferdium-popup-size", StringComparison.Ordinal))
            {
                return;
            }

            if (root.TryGetProperty("width", out var wEl) &&
                root.TryGetProperty("height", out var hEl) &&
                wEl.TryGetDouble(out var contentWidth) &&
                hEl.TryGetDouble(out var contentHeight))
            {
                // Add 2px for the 1px border on each side
                var clampedW = Math.Clamp(Math.Ceiling(contentWidth) + 2, 260, 520);
                var clampedH = Math.Clamp(Math.Ceiling(contentHeight) + 2, 200, 660);

                Dispatcher.BeginInvoke(() =>
                {
                    if (_isClosing) return;
                    Width = clampedW;
                    Height = clampedH;
                    ApplyAnchorPosition();
                }, DispatcherPriority.Render);
            }
        }
        catch
        {
            // Ignore non-JSON or unrelated extension messages
        }
    }

    private void ExtensionPopupWindow_Deactivated(object? sender, EventArgs e)
    {
        if (_closeOnDeactivate && !_isClosing)
        {
            _isClosing = true;
            Dispatcher.BeginInvoke(() =>
            {
                var owner = Owner;
                var ownerIsActive = owner is not null && owner.IsActive;

                // Detach Owner before Close() to prevent Win32 DestroyWindow from
                // dropping the owner window behind other desktop windows.
                Owner = null;

                try
                {
                    Close();
                }
                catch
                {
                    // Ignore if already closing
                }

                if (ownerIsActive && owner is not null)
                {
                    owner.Activate();
                }
            }, DispatcherPriority.Input);
        }
    }

    private void ExtensionPopupWindow_Closed(object? sender, EventArgs e)
    {
        _isClosing = true;
        try
        {
            PopupWebView.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
    }
}
