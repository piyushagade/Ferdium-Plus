using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Toolkit.Uwp.Notifications;

namespace FerdiumApp.Services;

public sealed class NotificationActivatedEventArgs : EventArgs
{
    public required string ServiceId { get; init; }
    public string? NotificationId { get; init; }
}

public sealed record FormattedNotification(string Title, string Body, string? Attribution);

/// <summary>
/// Displays native Windows 10/11 toast notifications for web services,
/// respecting privacy mode, silent flags, and click-to-focus activation,
/// with graceful fallback to the system tray balloon.
/// </summary>
public sealed class NotificationService : IDisposable
{
    private readonly Action<string, string>? _fallbackShowBalloon;

    public event EventHandler<NotificationActivatedEventArgs>? NotificationActivated;

    public NotificationService(Action<string, string>? fallbackShowBalloon = null)
    {
        _fallbackShowBalloon = fallbackShowBalloon;
        try
        {
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Toast activation registration failed: {ex.Message}");
        }
    }

    public void ShowNotification(
        string serviceId,
        string serviceName,
        string? title,
        string? body,
        bool isSilent,
        bool privateNotifications,
        string? tag,
        string? notificationId,
        string? iconFilePath = null)
    {
        var formatted = FormatNotification(serviceName, title, body, privateNotifications);

        try
        {
            var builder = new ToastContentBuilder()
                .AddArgument("serviceId", serviceId);

            if (!string.IsNullOrWhiteSpace(notificationId))
            {
                builder.AddArgument("notificationId", notificationId);
            }

            if (!string.IsNullOrWhiteSpace(iconFilePath) && File.Exists(iconFilePath))
            {
                builder.AddAppLogoOverride(new Uri(iconFilePath), ToastGenericAppLogoCrop.Default);
            }

            builder.AddText(formatted.Title);

            if (!string.IsNullOrWhiteSpace(formatted.Body))
            {
                builder.AddText(formatted.Body);
            }

            if (!string.IsNullOrWhiteSpace(formatted.Attribution))
            {
                builder.AddAttributionText(formatted.Attribution);
            }

            if (isSilent)
            {
                builder.AddAudio(new ToastAudio { Silent = true });
            }

            builder.Show(toast =>
            {
                toast.Group = SanitizeToastKey(serviceId);
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    toast.Tag = SanitizeToastKey(tag);
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Native toast failed, falling back to tray balloon: {ex.Message}");
            _fallbackShowBalloon?.Invoke(
                formatted.Title,
                string.IsNullOrWhiteSpace(formatted.Body) ? serviceName : formatted.Body);
        }
    }

    public static FormattedNotification FormatNotification(
        string serviceName,
        string? rawTitle,
        string? rawBody,
        bool privateNotifications)
    {
        var safeService = string.IsNullOrWhiteSpace(serviceName) ? "Ferdium" : serviceName.Trim();

        if (privateNotifications)
        {
            return new FormattedNotification(safeService, "New notification", null);
        }

        var title = string.IsNullOrWhiteSpace(rawTitle) ? safeService : rawTitle.Trim();
        var body = string.IsNullOrWhiteSpace(rawBody) ? string.Empty : rawBody.Trim();
        var attribution = string.Equals(title, safeService, StringComparison.OrdinalIgnoreCase)
            ? null
            : safeService;

        return new FormattedNotification(title, body, attribution);
    }

    /// <summary>
    /// Windows Toast Tag and Group have a 64-character limit (16 chars on older builds).
    /// Hash long keys deterministically so replacement/grouping always works safely.
    /// </summary>
    public static string SanitizeToastKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "ferdium";
        }

        var trimmed = key.Trim();
        if (trimmed.Length <= 48 && trimmed.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
        {
            return trimmed;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(trimmed));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        try
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("serviceId", out var serviceId) && !string.IsNullOrWhiteSpace(serviceId))
            {
                args.TryGetValue("notificationId", out var notificationId);
                NotificationActivated?.Invoke(this, new NotificationActivatedEventArgs
                {
                    ServiceId = serviceId,
                    NotificationId = notificationId,
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Toast activation parse failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated -= OnToastActivated;
        }
        catch
        {
            // Ignore shutdown errors
        }
    }
}
