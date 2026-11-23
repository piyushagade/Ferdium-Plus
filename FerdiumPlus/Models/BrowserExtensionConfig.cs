namespace FerdiumApp.Models;

public sealed class BrowserExtensionConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "0.0.0";
    public string Description { get; set; } = string.Empty;
    public string UnpackedPath { get; set; } = string.Empty;
    public string Source { get; set; } = "local"; // "webstore" | "crx" | "local"
    public string? WebStoreId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int ManifestVersion { get; set; } = 3;
    public string? PopupPath { get; set; }
    public string? OptionsPath { get; set; }
    public string? IconPath { get; set; }
    public DateTime InstalledAtUtc { get; set; } = DateTime.UtcNow;
}
