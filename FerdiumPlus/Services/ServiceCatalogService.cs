using FerdiumApp.Models;

namespace FerdiumApp.Services;

public sealed class ServiceCatalogService
{
    private static readonly IReadOnlyList<ServiceCatalogItem> Catalog = new List<ServiceCatalogItem>
    {
        new("whatsapp", "WhatsApp", "https://web.whatsapp.com", "Messaging", "#25D366", "WA", "End-to-end encrypted messaging and calls"),
        new("slack", "Slack", "https://app.slack.com/client", "Work", "#4A154B", "SL", "Team communication and channels"),
        new("discord", "Discord", "https://discord.com/app", "Messaging", "#5865F2", "DC", "Voice, video, and text communities"),
        new("teams", "Microsoft Teams", "https://teams.microsoft.com", "Work", "#6264A7", "MT", "Enterprise chat, meetings, and collaboration"),
        new("telegram", "Telegram", "https://web.telegram.org/a/", "Messaging", "#26A5E4", "TG", "Fast cloud-based messaging"),
        new("gmail", "Gmail", "https://mail.google.com", "Email", "#EA4335", "GM", "Google email and workspace inbox"),
        new("outlook", "Outlook", "https://outlook.live.com/mail/", "Email", "#0078D4", "OL", "Microsoft email and calendar"),
        new("messenger", "Messenger", "https://www.messenger.com", "Messaging", "#0084FF", "MS", "Meta direct messaging"),
        new("gmessages", "Google Messages", "https://messages.google.com/web/", "Messaging", "#1A73E8", "GM", "SMS, MMS, and RCS text messaging from your phone"),
        new("gvoice", "Google Voice", "https://voice.google.com", "Messaging", "#1E8E3E", "GV", "Calls, texts, and voicemail with your Google Voice number"),
        new("gchat", "Google Chat", "https://chat.google.com", "Work", "#00AC47", "GC", "Google Workspace team messaging"),
        new("notion", "Notion", "https://www.notion.so", "Productivity", "#2F3437", "NT", "Docs, wikis, and project tracking"),
        new("linear", "Linear", "https://linear.app", "Productivity", "#5E6AD2", "LN", "Streamlined issue tracking"),
        new("github", "GitHub", "https://github.com/notifications", "Developer", "#24292F", "GH", "Code, PRs, and notifications"),
        new("jira", "Jira", "https://www.atlassian.com/software/jira", "Work", "#0052CC", "JR", "Agile project management"),
        new("trello", "Trello", "https://trello.com", "Productivity", "#0079BF", "TR", "Kanban boards and tasks"),
        new("linkedin", "LinkedIn", "https://www.linkedin.com/messaging/", "Social", "#0A66C2", "IN", "Professional network and messages"),
        new("instagram", "Instagram", "https://www.instagram.com", "Social", "#E1306C", "IG", "Photos, reels, stories, and direct messages"),
        new("facebook", "Facebook", "https://www.facebook.com", "Social", "#1877F2", "FB", "Social feed, groups, and messages"),
        new("x", "X", "https://x.com/messages", "Social", "#14171A", "X", "Real-time posts and direct messages"),
        new("reddit", "Reddit", "https://www.reddit.com/chat", "Social", "#FF4500", "RD", "Communities and chat"),
        new("youtube", "YouTube", "https://www.youtube.com", "Media", "#FF0000", "YT", "Videos, livestreams, and subscriptions"),
        new("spotify", "Spotify", "https://open.spotify.com", "Media", "#1DB954", "SP", "Music and podcasts"),
    };

    public IReadOnlyList<ServiceCatalogItem> GetAll() => Catalog;

    public IReadOnlyList<ServiceCatalogItem> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Catalog;
        }

        var trimmed = query.Trim();
        return Catalog
            .Where(item =>
                item.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                item.Category.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public ServiceConfig CreateServiceFromCatalog(ServiceCatalogItem item)
    {
        return new ServiceConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = item.Name,
            Url = item.Url,
            CatalogId = item.Id,
            AccentColor = item.AccentColor,
            Monogram = item.Monogram,
            IsEnabled = true,
            IsHibernationEnabled = true,
        };
    }

    public ServiceConfig CreateCustomService(string name, string url)
    {
        var normalizedUrl = NormalizeUrl(url);
        var cleanName = string.IsNullOrWhiteSpace(name) ? ExtractHostName(normalizedUrl) : name.Trim();
        var monogram = BuildMonogram(cleanName);

        return new ServiceConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = cleanName,
            Url = normalizedUrl,
            CatalogId = "custom",
            AccentColor = "#7367F0",
            Monogram = monogram,
            IsEnabled = true,
            IsHibernationEnabled = true,
        };
    }

    public static string NormalizeUrl(string rawUrl)
    {
        var trimmed = (rawUrl ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return "https://ferdium.org";
        }

        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{trimmed}";
        }

        return trimmed;
    }

    public static string BuildMonogram(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2)
        {
            return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
        }

        if (words.Length == 1 && words[0].Length >= 2)
        {
            return words[0][..2].ToUpperInvariant();
        }

        return words.Length == 1 ? words[0].ToUpperInvariant() : "SV";
    }

    private static string ExtractHostName(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)
            : "Custom Service";
    }
}
