using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FerdiumApp.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _muteItem;
    private readonly Icon _appIcon;

    public event EventHandler? OpenRequested;
    public event EventHandler? ToggleMuteRequested;
    public event EventHandler? ExitRequested;

    public TrayIconService()
    {
        _appIcon = LoadEmbeddedAppIcon() ?? CreateFluentTrayIcon();

        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Open Ferdium Plus");
        openItem.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        _muteItem = new ToolStripMenuItem("Mute Notifications & Audio");
        _muteItem.Click += (_, _) => ToggleMuteRequested?.Invoke(this, EventArgs.Empty);

        var exitItem = new ToolStripMenuItem("Quit Ferdium Plus");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        menu.Items.Add(openItem);
        menu.Items.Add(_muteItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "Ferdium Plus",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetVisible(bool visible)
    {
        _notifyIcon.Visible = visible;
    }

    public void ShowBalloonNotification(string title, string body)
    {
        if (!_notifyIcon.Visible)
        {
            return;
        }

        _notifyIcon.ShowBalloonTip(
            5000,
            string.IsNullOrWhiteSpace(title) ? "Ferdium Plus" : title,
            string.IsNullOrWhiteSpace(body) ? "New notification" : body,
            ToolTipIcon.Info);
    }

    public void UpdateStatus(int totalUnreadCount, bool isMuted)
    {
        _muteItem.Checked = isMuted;
        var suffix = isMuted ? " (Muted)" : string.Empty;
        var text = totalUnreadCount > 0
            ? $"Ferdium — {totalUnreadCount} unread{suffix}"
            : $"Ferdium Plus{suffix}";

        // NotifyIcon.Text has a 63-char Win32 limit
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private static Icon? LoadEmbeddedAppIcon()
    {
        try
        {
            var resourceInfo = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/app-icon.ico", UriKind.Absolute));
            if (resourceInfo?.Stream is not null)
            {
                using var stream = resourceInfo.Stream;
                return new Icon(stream, new Size(32, 32));
            }
        }
        catch
        {
            // Fall back to generated icon if WPF resource stream is unavailable
        }

        return null;
    }

    private static Icon CreateFluentTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        using var brush = new SolidBrush(Color.FromArgb(255, 115, 103, 240));
        g.FillEllipse(brush, 2, 2, 28, 28);

        using var font = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString("F", font, textBrush, new RectangleF(0, 1, 32, 31), sf);

        var hIcon = bitmap.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _appIcon.Dispose();
    }
}
