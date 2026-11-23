using System.Text.RegularExpressions;

namespace FerdiumApp.Services;

public static partial class UnreadBadgeParser
{
    [GeneratedRegex(@"^\s*[\(\[]\s*(\d+)\+?\s*[\)\]]", RegexOptions.Compiled)]
    private static partial Regex PrefixCountRegex();

    [GeneratedRegex(@"[\(\[]\s*(\d+)\+?\s*[\)\]]\s*$", RegexOptions.Compiled)]
    private static partial Regex SuffixCountRegex();

    /// <summary>
    /// Extracts unread message counts from standard web app document titles
    /// such as "(3) WhatsApp", "[12] Slack", "Gmail (5)", or "• Discord".
    /// </summary>
    public static (int DirectCount, bool HasIndirectActivity) ParseFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (0, false);
        }

        var prefixMatch = PrefixCountRegex().Match(title);
        if (prefixMatch.Success && int.TryParse(prefixMatch.Groups[1].Value, out var prefixCount))
        {
            return (prefixCount, false);
        }

        var suffixMatch = SuffixCountRegex().Match(title);
        if (suffixMatch.Success && int.TryParse(suffixMatch.Groups[1].Value, out var suffixCount))
        {
            return (suffixCount, false);
        }

        var trimmed = title.TrimStart();
        if (trimmed.StartsWith('•') || trimmed.StartsWith('*') || trimmed.StartsWith('●'))
        {
            return (0, true);
        }

        return (0, false);
    }
}
