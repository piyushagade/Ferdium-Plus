using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FerdiumApp.Services;

/// <summary>
/// Provides razor-sharp 256px embedded brand icons for all built-in services,
/// and disk-cached high-resolution favicons for custom user URLs.
/// </summary>
public static class ServiceIconService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    private static readonly ConcurrentDictionary<string, ImageSource?> MemoryCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> BundledIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "whatsapp", "slack", "discord", "teams", "telegram",
        "gmail", "outlook", "messenger", "gmessages", "gvoice", "gchat", "notion",
        "linear", "github", "jira", "trello", "linkedin",
        "x", "reddit", "spotify", "instagram", "facebook", "youtube",
    };

    private static readonly Dictionary<string, string> DomainToBundledId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["web.whatsapp.com"] = "whatsapp",
        ["whatsapp.com"] = "whatsapp",
        ["app.slack.com"] = "slack",
        ["slack.com"] = "slack",
        ["discord.com"] = "discord",
        ["teams.microsoft.com"] = "teams",
        ["web.telegram.org"] = "telegram",
        ["telegram.org"] = "telegram",
        ["mail.google.com"] = "gmail",
        ["outlook.live.com"] = "outlook",
        ["outlook.office.com"] = "outlook",
        ["outlook.office365.com"] = "outlook",
        ["www.messenger.com"] = "messenger",
        ["messenger.com"] = "messenger",
        ["messages.google.com"] = "gmessages",
        ["voice.google.com"] = "gvoice",
        ["chat.google.com"] = "gchat",
        ["www.notion.so"] = "notion",
        ["notion.so"] = "notion",
        ["linear.app"] = "linear",
        ["github.com"] = "github",
        ["www.atlassian.com"] = "jira",
        ["trello.com"] = "trello",
        ["www.linkedin.com"] = "linkedin",
        ["linkedin.com"] = "linkedin",
        ["x.com"] = "x",
        ["twitter.com"] = "x",
        ["www.reddit.com"] = "reddit",
        ["reddit.com"] = "reddit",
        ["open.spotify.com"] = "spotify",
        ["spotify.com"] = "spotify",
        ["www.instagram.com"] = "instagram",
        ["instagram.com"] = "instagram",
        ["www.facebook.com"] = "facebook",
        ["facebook.com"] = "facebook",
        ["www.youtube.com"] = "youtube",
        ["youtube.com"] = "youtube",
        ["m.youtube.com"] = "youtube",
    };

    private static readonly string CacheDirectory = InitializeCacheDirectory();

    private static string InitializeCacheDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "FerdiumPlus", "Icons");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string ExtractDomain(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(ServiceCatalogService.NormalizeUrl(url), UriKind.Absolute, out var uri))
        {
            return uri.Host.ToLowerInvariant();
        }

        return string.Empty;
    }

    public static string? ResolveBundledId(string? catalogId, string? url)
    {
        if (!string.IsNullOrWhiteSpace(catalogId) && BundledIds.Contains(catalogId))
        {
            return catalogId.ToLowerInvariant();
        }

        var domain = ExtractDomain(url);
        if (!string.IsNullOrEmpty(domain) && DomainToBundledId.TryGetValue(domain, out var mapped))
        {
            return mapped;
        }

        return null;
    }

    public static bool HasBundledIcon(string? catalogId, string? url) =>
        ResolveBundledId(catalogId, url) is not null;

    public static Uri? GetBundledPackUri(string? catalogId, string? url)
    {
        var id = ResolveBundledId(catalogId, url);
        if (id is null) return null;
        return new Uri($"pack://application:,,,/FerdiumPlus;component/Assets/Icons/{id}.png", UriKind.Absolute);
    }

    public static ImageSource? TryGetBundledIcon(string? catalogId, string? url)
    {
        var id = ResolveBundledId(catalogId, url);
        if (id is null)
        {
            return null;
        }

        var cacheKey = $"bundled:{id}";
        if (MemoryCache.TryGetValue(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var packUri = new Uri($"pack://application:,,,/FerdiumPlus;component/Assets/Icons/{id}.png", UriKind.Absolute);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = packUri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 128;
            bitmap.EndInit();
            bitmap.Freeze();

            MemoryCache[cacheKey] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<ImageSource?> GetIconAsync(string? catalogId, string? url)
    {
        var bundled = TryGetBundledIcon(catalogId, url);
        if (bundled is not null)
        {
            return bundled;
        }

        var domain = ExtractDomain(url);
        if (string.IsNullOrEmpty(domain))
        {
            return null;
        }

        if (MemoryCache.TryGetValue(domain, out var cached) && cached is not null)
        {
            return cached;
        }

        var cachePath = GetCachePath(domain);

        if (File.Exists(cachePath))
        {
            var fromDisk = LoadFrozenBitmapFromFile(cachePath);
            if (fromDisk is not null)
            {
                MemoryCache[domain] = fromDisk;
                return fromDisk;
            }
        }

        try
        {
            var iconUrl =
                $"https://t2.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url=https://{Uri.EscapeDataString(domain)}&size=256";
            var bytes = await Http.GetByteArrayAsync(iconUrl).ConfigureAwait(false);
            if (bytes.Length > 200)
            {
                await File.WriteAllBytesAsync(cachePath, bytes).ConfigureAwait(false);
                var bmp = LoadFrozenBitmapFromBytes(bytes);
                if (bmp is not null)
                {
                    MemoryCache[domain] = bmp;
                    return bmp;
                }
            }
        }
        catch
        {
            // Offline or unreachable — caller falls back cleanly to monogram
        }

        return null;
    }

    public static ImageSource? SaveAndLoadFaviconStream(string? catalogId, string? url, Stream stream)
    {
        // Never overwrite a crisp bundled 256px brand icon with a low-res tab favicon
        if (HasBundledIcon(catalogId, url))
        {
            return null;
        }

        try
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            if (bytes.Length < 128)
            {
                return null;
            }

            var bmp = LoadFrozenBitmapFromBytes(bytes);
            if (bmp is null || bmp.PixelWidth < 48)
            {
                return null;
            }

            var domain = ExtractDomain(url);
            if (!string.IsNullOrEmpty(domain))
            {
                MemoryCache[domain] = bmp;
                try
                {
                    File.WriteAllBytes(GetCachePath(domain), bytes);
                }
                catch
                {
                    // Non-critical cache write failure
                }
            }

            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static string GetCachePath(string domain)
    {
        var safeName = string.Concat(domain.Select(c => char.IsLetterOrDigit(c) || c == '.' || c == '-' ? c : '_'));
        return Path.Combine(CacheDirectory, $"{safeName}.png");
    }

    private static BitmapImage? LoadFrozenBitmapFromFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return LoadFrozenBitmapFromBytes(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? LoadFrozenBitmapFromBytes(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 128;
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
}
