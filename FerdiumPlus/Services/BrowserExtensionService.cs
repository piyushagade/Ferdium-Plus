using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FerdiumApp.Models;
using Microsoft.Web.WebView2.Core;

namespace FerdiumApp.Services;

public sealed record ParsedExtensionManifest(
    string Name,
    string Version,
    string Description,
    int ManifestVersion,
    string? PopupPath,
    string? OptionsPath,
    string? IconPath);

/// <summary>
/// Manages Chrome/Edge browser extensions for FerdiumPlus:
/// downloads CRX3 packages from the Chrome Web Store, unpacks .crx/.zip archives,
/// loads unpacked folders, resolves manifest metadata/icons, and persists registry.json.
/// </summary>
public sealed partial class BrowserExtensionService
{
    public const string BitwardenWebStoreId = "nngceckbapebfimnlniiiahkandclblb";

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _extensionsRoot;
    private readonly string _unpackedRoot;
    private readonly string _registryPath;
    private readonly List<BrowserExtensionConfig> _extensions = new();

    public BrowserExtensionService(string? customRootPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customRootPath))
        {
            _extensionsRoot = customRootPath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _extensionsRoot = Path.Combine(localAppData, "FerdiumPlus", "Extensions");
        }

        _unpackedRoot = Path.Combine(_extensionsRoot, "Unpacked");
        _registryPath = Path.Combine(_extensionsRoot, "registry.json");

        Directory.CreateDirectory(_extensionsRoot);
        Directory.CreateDirectory(_unpackedRoot);
        LoadRegistry();
    }

    public IReadOnlyList<BrowserExtensionConfig> GetAll() => _extensions.ToList();

    public IReadOnlyList<BrowserExtensionConfig> GetEnabled() =>
        _extensions.Where(e => e.IsEnabled).ToList();

    public BrowserExtensionConfig? GetById(string id) =>
        _extensions.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^[a-p]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactWebStoreIdRegex();

    [GeneratedRegex(@"\b([a-p]{32})\b", RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedWebStoreIdRegex();

    [GeneratedRegex(@"^__MSG_(.+)__$", RegexOptions.CultureInvariant)]
    private static partial Regex I18nPlaceholderRegex();

    [GeneratedRegex(@"\d+\.\d+\.\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex VersionNumberRegex();

    private static string ResolveBrowserVersion()
    {
        try
        {
            var raw = CoreWebView2Environment.GetAvailableBrowserVersionString();
            var match = VersionNumberRegex().Match(raw ?? string.Empty);
            if (match.Success)
            {
                return match.Value;
            }
        }
        catch
        {
            // Fall back to a modern Chromium version
        }

        return "145.0.0.0";
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        return client;
    }

    public static string? ParseWebStoreId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        if (ExactWebStoreIdRegex().IsMatch(trimmed))
        {
            return trimmed;
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = segments.Length - 1; i >= 0; i--)
            {
                if (ExactWebStoreIdRegex().IsMatch(segments[i]))
                {
                    return segments[i];
                }
            }
        }

        var match = EmbeddedWebStoreIdRegex().Match(trimmed);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static byte[] ExtractZipFromCrx(byte[] packageBytes)
    {
        if (packageBytes.Length < 12)
        {
            throw new InvalidDataException("Extension package is too small to be a valid CRX or ZIP archive.");
        }

        // Raw ZIP archive ("PK\x03\x04")
        if (packageBytes[0] == 0x50 && packageBytes[1] == 0x4B &&
            packageBytes[2] == 0x03 && packageBytes[3] == 0x04)
        {
            return packageBytes;
        }

        // CRX magic ("Cr24")
        var magic = Encoding.ASCII.GetString(packageBytes, 0, 4);
        if (!string.Equals(magic, "Cr24", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported extension package format (expected CRX or ZIP, got \"{magic}\").");
        }

        var version = BinaryPrimitives.ReadUInt32LittleEndian(packageBytes.AsSpan(4, 4));
        int zipOffset;

        if (version == 2)
        {
            if (packageBytes.Length < 16)
            {
                throw new InvalidDataException("Invalid CRX2 header.");
            }
            var publicKeyLen = BinaryPrimitives.ReadUInt32LittleEndian(packageBytes.AsSpan(8, 4));
            var signatureLen = BinaryPrimitives.ReadUInt32LittleEndian(packageBytes.AsSpan(12, 4));
            zipOffset = checked((int)(16 + publicKeyLen + signatureLen));
        }
        else if (version == 3)
        {
            var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(packageBytes.AsSpan(8, 4));
            zipOffset = checked((int)(12 + headerSize));
        }
        else
        {
            throw new InvalidDataException($"Unsupported CRX version: {version}.");
        }

        if (zipOffset >= packageBytes.Length)
        {
            throw new InvalidDataException("CRX header length exceeds file size.");
        }

        var zipBytes = new byte[packageBytes.Length - zipOffset];
        Buffer.BlockCopy(packageBytes, zipOffset, zipBytes, 0, zipBytes.Length);
        return zipBytes;
    }

    public static void ExtractZipSafely(byte[] zipBytes, string destinationDir)
    {
        var fullDestDir = Path.GetFullPath(destinationDir);
        if (Directory.Exists(fullDestDir))
        {
            Directory.Delete(fullDestDir, recursive: true);
        }
        Directory.CreateDirectory(fullDestDir);

        var destWithSep = fullDestDir.EndsWith(Path.DirectorySeparatorChar)
            ? fullDestDir
            : fullDestDir + Path.DirectorySeparatorChar;

        using var ms = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.FullName))
            {
                continue;
            }

            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var targetPath = Path.GetFullPath(Path.Combine(fullDestDir, relativePath));

            if (!targetPath.StartsWith(destWithSep, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(targetPath, fullDestDir, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Unsafe path detected in extension archive: {entry.FullName}");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var parentDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    public static string ResolveExtensionRoot(string folderPath)
    {
        if (File.Exists(Path.Combine(folderPath, "manifest.json")))
        {
            return folderPath;
        }

        foreach (var subDir in Directory.EnumerateDirectories(folderPath))
        {
            if (File.Exists(Path.Combine(subDir, "manifest.json")))
            {
                return subDir;
            }
        }

        throw new FileNotFoundException("No manifest.json found in the selected extension.");
    }

    public static ParsedExtensionManifest ParseManifest(string extensionDir)
    {
        var manifestPath = Path.Combine(extensionDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"manifest.json not found in {extensionDir}");
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
        var root = doc.RootElement;

        var defaultLocale = TryGetString(root, "default_locale");
        var rawName = TryGetString(root, "name");
        var rawDesc = TryGetString(root, "description");
        var version = TryGetString(root, "version") ?? "0.0.0";
        var manifestVersion = root.TryGetProperty("manifest_version", out var mvProp) && mvProp.TryGetInt32(out var mv)
            ? mv
            : 2;

        var name = ResolveI18nMessage(rawName, extensionDir, defaultLocale);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = Path.GetFileName(extensionDir);
        }
        var description = ResolveI18nMessage(rawDesc, extensionDir, defaultLocale) ?? string.Empty;

        string? popupPath = null;
        string? iconRelative = null;

        if (root.TryGetProperty("action", out var actionEl) && actionEl.ValueKind == JsonValueKind.Object)
        {
            popupPath = TryGetString(actionEl, "default_popup")?.TrimStart('/');
            iconRelative = PickBestIconPath(actionEl, "default_icon");
        }
        else if (root.TryGetProperty("browser_action", out var browserActionEl) && browserActionEl.ValueKind == JsonValueKind.Object)
        {
            popupPath = TryGetString(browserActionEl, "default_popup")?.TrimStart('/');
            iconRelative = PickBestIconPath(browserActionEl, "default_icon");
        }

        iconRelative ??= PickBestIconPath(root, "icons");

        string? optionsPath = null;
        if (root.TryGetProperty("options_ui", out var optionsUiEl) && optionsUiEl.ValueKind == JsonValueKind.Object)
        {
            optionsPath = TryGetString(optionsUiEl, "page")?.TrimStart('/');
        }
        optionsPath ??= TryGetString(root, "options_page")?.TrimStart('/');

        string? iconFullPath = null;
        if (!string.IsNullOrWhiteSpace(iconRelative))
        {
            var candidate = Path.Combine(extensionDir, iconRelative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
            {
                iconFullPath = candidate;
            }
        }

        return new ParsedExtensionManifest(
            Name: name,
            Version: version,
            Description: description,
            ManifestVersion: manifestVersion,
            PopupPath: string.IsNullOrWhiteSpace(popupPath) ? null : popupPath,
            OptionsPath: string.IsNullOrWhiteSpace(optionsPath) ? null : optionsPath,
            IconPath: iconFullPath);
    }

    public async Task<BrowserExtensionConfig> InstallFromWebStoreAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        var webStoreId = ParseWebStoreId(input);
        if (string.IsNullOrEmpty(webStoreId))
        {
            throw new ArgumentException(
                "Invalid Chrome Web Store URL or Extension ID. Expected a 32-character ID (e.g., nngceckbapebfimnlniiiahkandclblb).");
        }

        var chromeVersion = ResolveBrowserVersion();
        var url =
            $"https://clients2.google.com/service/update2/crx?response=redirect&os=win&arch=x64&os_arch=x86_64&nacl_arch=x86-64&prod=chromecrx&prodchannel=unknown&prodversion={Uri.EscapeDataString(chromeVersion)}&acceptformat=crx2,crx3&x=id%3D{Uri.EscapeDataString(webStoreId)}%26installsource%3Dondemand%26uc";

        using var response = await SharedHttpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            throw new HttpRequestException(
                $"Chrome Web Store returned no package for extension ID \"{webStoreId}\". Verify the ID or try installing from a .crx file.");
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Chrome Web Store download failed (HTTP {(int)response.StatusCode}). Check that extension ID \"{webStoreId}\" is valid.");
        }

        var crxBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (crxBytes.Length == 0)
        {
            throw new InvalidDataException("Chrome Web Store returned an empty package.");
        }

        var zipBytes = ExtractZipFromCrx(crxBytes);
        var targetDir = Path.Combine(_unpackedRoot, webStoreId);
        ExtractZipSafely(zipBytes, targetDir);

        var extensionRoot = ResolveExtensionRoot(targetDir);
        var manifest = ParseManifest(extensionRoot);

        var config = new BrowserExtensionConfig
        {
            Id = webStoreId,
            WebStoreId = webStoreId,
            Source = "webstore",
            UnpackedPath = extensionRoot,
            IsEnabled = true,
            InstalledAtUtc = DateTime.UtcNow,
            Name = manifest.Name,
            Version = manifest.Version,
            Description = manifest.Description,
            ManifestVersion = manifest.ManifestVersion,
            PopupPath = manifest.PopupPath,
            OptionsPath = manifest.OptionsPath,
            IconPath = manifest.IconPath,
        };

        UpsertAndSave(config);
        return config;
    }

    public BrowserExtensionConfig InstallFromCrxOrZip(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var bytes = File.ReadAllBytes(filePath);
        var zipBytes = ExtractZipFromCrx(bytes);

        var baseName = Path.GetFileNameWithoutExtension(filePath);
        var parsedId = ParseWebStoreId(baseName);
        var id = parsedId ?? ComputeStableId($"{baseName}:{bytes.Length}");

        var targetDir = Path.Combine(_unpackedRoot, id);
        ExtractZipSafely(zipBytes, targetDir);

        var extensionRoot = ResolveExtensionRoot(targetDir);
        var manifest = ParseManifest(extensionRoot);

        var config = new BrowserExtensionConfig
        {
            Id = id,
            WebStoreId = parsedId,
            Source = "crx",
            UnpackedPath = extensionRoot,
            IsEnabled = true,
            InstalledAtUtc = DateTime.UtcNow,
            Name = manifest.Name,
            Version = manifest.Version,
            Description = manifest.Description,
            ManifestVersion = manifest.ManifestVersion,
            PopupPath = manifest.PopupPath,
            OptionsPath = manifest.OptionsPath,
            IconPath = manifest.IconPath,
        };

        UpsertAndSave(config);
        return config;
    }

    public BrowserExtensionConfig InstallFromFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
        }

        var extensionRoot = ResolveExtensionRoot(folderPath);
        var manifest = ParseManifest(extensionRoot);
        var id = ComputeStableId(Path.GetFullPath(extensionRoot));

        var config = new BrowserExtensionConfig
        {
            Id = id,
            Source = "local",
            UnpackedPath = extensionRoot,
            IsEnabled = true,
            InstalledAtUtc = DateTime.UtcNow,
            Name = manifest.Name,
            Version = manifest.Version,
            Description = manifest.Description,
            ManifestVersion = manifest.ManifestVersion,
            PopupPath = manifest.PopupPath,
            OptionsPath = manifest.OptionsPath,
            IconPath = manifest.IconPath,
        };

        UpsertAndSave(config);
        return config;
    }

    public BrowserExtensionConfig SetEnabled(string id, bool enabled)
    {
        var ext = GetById(id) ?? throw new KeyNotFoundException($"Extension not found: {id}");
        ext.IsEnabled = enabled;
        SaveRegistry();
        return ext;
    }

    public BrowserExtensionConfig? Remove(string id)
    {
        var ext = GetById(id);
        if (ext is null)
        {
            return null;
        }

        _extensions.Remove(ext);
        SaveRegistry();

        if (!string.Equals(ext.Source, "local", StringComparison.OrdinalIgnoreCase))
        {
            var managedDir = Path.Combine(_unpackedRoot, ext.Id);
            if (Directory.Exists(managedDir))
            {
                try
                {
                    Directory.Delete(managedDir, recursive: true);
                }
                catch
                {
                    // Ignore locked files; will be overwritten if reinstalled
                }
            }
        }

        return ext;
    }

    private void UpsertAndSave(BrowserExtensionConfig config)
    {
        var existingIndex = _extensions.FindIndex(e => string.Equals(e.Id, config.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _extensions[existingIndex] = config;
        }
        else
        {
            _extensions.Add(config);
        }

        SaveRegistry();
    }

    private void LoadRegistry()
    {
        _extensions.Clear();
        if (!File.Exists(_registryPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_registryPath, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<List<BrowserExtensionConfig>>(json, JsonOptions);
            if (loaded is null)
            {
                return;
            }

            foreach (var item in loaded)
            {
                if (string.IsNullOrWhiteSpace(item.UnpackedPath) ||
                    !File.Exists(Path.Combine(item.UnpackedPath, "manifest.json")))
                {
                    continue;
                }

                try
                {
                    var manifest = ParseManifest(item.UnpackedPath);
                    item.Name = manifest.Name;
                    item.Version = manifest.Version;
                    item.Description = manifest.Description;
                    item.ManifestVersion = manifest.ManifestVersion;
                    item.PopupPath = manifest.PopupPath;
                    item.OptionsPath = manifest.OptionsPath;
                    item.IconPath = manifest.IconPath;
                    _extensions.Add(item);
                }
                catch
                {
                    // Skip broken extension
                }
            }
        }
        catch
        {
            // Ignore corrupted registry
        }
    }

    private void SaveRegistry()
    {
        Directory.CreateDirectory(_extensionsRoot);
        var json = JsonSerializer.Serialize(_extensions, JsonOptions);
        File.WriteAllText(_registryPath, json, Encoding.UTF8);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static string? PickBestIconPath(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }

        if (prop.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var preferredSizes = new[] { "48", "32", "64", "128", "24", "16", "256" };
        foreach (var size in preferredSizes)
        {
            if (prop.TryGetProperty(size, out var iconEl) && iconEl.ValueKind == JsonValueKind.String)
            {
                return iconEl.GetString();
            }
        }

        foreach (var entry in prop.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String)
            {
                return entry.Value.GetString();
            }
        }

        return null;
    }

    private static string? ResolveI18nMessage(string? value, string extensionDir, string? defaultLocale)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var match = I18nPlaceholderRegex().Match(value.Trim());
        if (!match.Success)
        {
            return value;
        }

        var messageKey = match.Groups[1].Value;
        var candidateLocales = new[] { defaultLocale, "en", "en_US", "en-US" }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var locale in candidateLocales)
        {
            var messagesPath = Path.Combine(extensionDir, "_locales", locale!, "messages.json");
            if (!File.Exists(messagesPath))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(messagesPath, Encoding.UTF8));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, messageKey, StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.TryGetProperty("message", out var msgEl) &&
                        msgEl.ValueKind == JsonValueKind.String)
                    {
                        var resolved = msgEl.GetString();
                        if (!string.IsNullOrWhiteSpace(resolved))
                        {
                            return resolved;
                        }
                    }
                }
            }
            catch
            {
                // Ignore malformed locale file
            }
        }

        return value;
    }

    private static string ComputeStableId(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }
}
