using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace FerdiumApp;

public partial class ExtensionPopupWindow : Window
{
    private readonly CoreWebView2Environment _environment;
    private readonly string _targetUrl;
    private readonly bool _closeOnDeactivate;
    private bool _isClosing;

    public string ExtensionId { get; }

    public ExtensionPopupWindow(
        string extensionId,
        string title,
        CoreWebView2Environment environment,
        string targetUrl,
        bool closeOnDeactivate = true)
    {
        InitializeComponent();

        ExtensionId = extensionId;
        Title = title;
        _environment = environment;
        _targetUrl = targetUrl;
        _closeOnDeactivate = closeOnDeactivate;

        if (!closeOnDeactivate)
        {
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
        var workArea = SystemParameters.WorkArea;
        var left = anchorScreenDipRect.Left;
        var top = anchorScreenDipRect.Bottom + 4;

        if (left + Width > workArea.Right - 8)
        {
            left = Math.Max(workArea.Left + 8, anchorScreenDipRect.Right - Width);
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

            PopupWebView.CoreWebView2.NewWindowRequested += (_, args) =>
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
                            closeOnDeactivate: false)
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

            PopupWebView.CoreWebView2.Navigate(_targetUrl);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize extension popup WebView2: {ex.Message}");
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
