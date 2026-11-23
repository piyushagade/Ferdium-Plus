using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FerdiumApp.Models;

namespace FerdiumApp.ViewModels;

public sealed partial class BrowserExtensionItemViewModel : ObservableObject
{
    private readonly Action<BrowserExtensionItemViewModel, bool> _onEnabledToggled;
    private bool _suppressToggleCallback;

    public BrowserExtensionConfig Config { get; }

    public string Id => Config.Id;
    public string Name => Config.Name;
    public string VersionBadge => $"v{Config.Version}";
    public string Description => Config.Description;
    public string ManifestBadge => $"MV{Config.ManifestVersion}";
    public string SourceBadge => Config.Source switch
    {
        "webstore" => "Web Store",
        "crx" => "CRX",
        _ => "Local",
    };

    public bool HasPopup => !string.IsNullOrWhiteSpace(Config.PopupPath);
    public bool HasOptions => !string.IsNullOrWhiteSpace(Config.OptionsPath);
    public ImageSource? IconImage { get; }

    [ObservableProperty]
    private bool _isEnabled;

    public BrowserExtensionItemViewModel(
        BrowserExtensionConfig config,
        Action<BrowserExtensionItemViewModel, bool> onEnabledToggled)
    {
        Config = config;
        _onEnabledToggled = onEnabledToggled;

        _suppressToggleCallback = true;
        _isEnabled = config.IsEnabled;
        _suppressToggleCallback = false;

        IconImage = TryLoadIcon(config.IconPath);
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressToggleCallback)
        {
            return;
        }

        _onEnabledToggled(this, value);
    }

    private static ImageSource? TryLoadIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 48;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
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
