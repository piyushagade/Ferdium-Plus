using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace FerdiumApp.Services;

/// <summary>
/// Pure & Win32 helpers powering Privacy, Language, Advanced, and Shell settings:
/// 2FA auto-catcher, taskbar flashing, system idle time, search/translate URLs,
/// shortcut matching, and global hide hotkey registration.
/// </summary>
public static partial class PrivacyAndShellHelpers
{
    public const int GlobalHideHotKeyId = 0xF3D1;
    public const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkF = 0x46;

    [GeneratedRegex(@"\b(\d{4,8})\b", RegexOptions.Compiled)]
    private static partial Regex TwoFactorCodeRegex();

    public static bool TryExtractTwoFactorCode(
        string? title,
        string? body,
        string? matcherCsv,
        out string code)
    {
        code = string.Empty;
        var combined = $"{title} {body}".Trim();
        if (string.IsNullOrWhiteSpace(combined))
        {
            return false;
        }

        var keywords = (matcherCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (keywords.Length > 0 &&
            !keywords.Any(k => combined.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var match = TwoFactorCodeRegex().Match(combined);
        if (!match.Success)
        {
            return false;
        }

        code = match.Groups[1].Value;
        return true;
    }

    public static string BuildSearchUrl(string query, string? searchEngine)
    {
        var encoded = Uri.EscapeDataString(query.Trim());
        return (searchEngine ?? "startPage").ToLowerInvariant() switch
        {
            "google" => $"https://www.google.com/search?q={encoded}",
            "duckduckgo" => $"https://duckduckgo.com/?q={encoded}",
            "bing" => $"https://www.bing.com/search?q={encoded}",
            _ => $"https://www.startpage.com/do/search?q={encoded}",
        };
    }

    public static string BuildTranslateUrl(string text, string? engine, string? targetLanguage)
    {
        var encoded = Uri.EscapeDataString(text.Trim());
        var lang = string.IsNullOrWhiteSpace(targetLanguage) ? "en" : targetLanguage.Trim();

        if (string.Equals(engine, "Google", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://translate.google.com/?sl=auto&tl={Uri.EscapeDataString(lang)}&text={encoded}&op=translate";
        }

        return $"https://translator.ferdium.org/?source=auto&target={Uri.EscapeDataString(lang)}&q={encoded}";
    }

    public static bool LooksLikeSearchQuery(string input)
    {
        var trimmed = input.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return false;
        if (trimmed.Contains(' ')) return true;
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !trimmed.Contains('.');
    }

    public static bool MatchesShortcut(Key pressedKey, ModifierKeys modifiers, string? shortcutText)
    {
        if (string.IsNullOrWhiteSpace(shortcutText))
        {
            return false;
        }

        var parts = shortcutText.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var expectedModifiers = ModifierKeys.None;
        Key? expectedKey = null;

        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    expectedModifiers |= ModifierKeys.Control;
                    break;
                case "shift":
                    expectedModifiers |= ModifierKeys.Shift;
                    break;
                case "alt":
                    expectedModifiers |= ModifierKeys.Alt;
                    break;
                case "tab":
                    expectedKey = Key.Tab;
                    break;
                case "pagedown":
                case "pgdn":
                    expectedKey = Key.PageDown;
                    break;
                case "pageup":
                case "pgup":
                    expectedKey = Key.PageUp;
                    break;
                default:
                    if (Enum.TryParse<Key>(part, ignoreCase: true, out var parsed))
                    {
                        expectedKey = parsed;
                    }
                    break;
            }
        }

        return expectedKey.HasValue &&
               pressedKey == expectedKey.Value &&
               modifiers == expectedModifiers;
    }

    public static void FlashTaskbarWindow(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = handle,
                dwFlags = 0x00000002 | 0x0000000C, // FLASHW_TRAY | FLASHW_TIMERNOFG
                uCount = 4,
                dwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }
        catch
        {
            // Ignore Win32 interop failures
        }
    }

    public static double GetSystemIdleMinutes()
    {
        try
        {
            var info = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>(),
            };
            if (!GetLastInputInfo(ref info))
            {
                return 0;
            }

            var elapsedMs = unchecked((uint)Environment.TickCount - info.dwTime);
            return elapsedMs / 60000.0;
        }
        catch
        {
            return 0;
        }
    }

    public static bool RegisterGlobalHideHotkey(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;
            return RegisterHotKey(handle, GlobalHideHotKeyId, ModControl | ModAlt, VkF);
        }
        catch
        {
            return false;
        }
    }

    public static void UnregisterGlobalHideHotkey(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            UnregisterHotKey(handle, GlobalHideHotKeyId);
        }
        catch
        {
            // Ignore
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
