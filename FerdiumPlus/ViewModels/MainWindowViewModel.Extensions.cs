using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FerdiumApp.Models;
using FerdiumApp.Services;

namespace FerdiumApp.ViewModels;

public sealed partial class MainWindowViewModel
{
    private BrowserExtensionService? _extensionService;

    public event Func<Task>? ExtensionsChangedRequested;
    public event Action? BrowseCrxFileRequested;
    public event Action? BrowseExtensionFolderRequested;
    public event Action<BrowserExtensionConfig>? OpenExtensionOptionsRequested;

    public ObservableCollection<BrowserExtensionItemViewModel> InstalledExtensions { get; } = new();
    public ObservableCollection<BrowserExtensionItemViewModel> EnabledExtensions { get; } = new();

    [ObservableProperty]
    private string _webStoreExtensionInput = string.Empty;

    [ObservableProperty]
    private bool _isInstallingExtension;

    [ObservableProperty]
    private string _extensionStatusMessage = string.Empty;

    [ObservableProperty]
    private bool _isBitwardenInstalled;

    public bool HasNoExtensions => InstalledExtensions.Count == 0;
    public bool HasNoEnabledExtensions => EnabledExtensions.Count == 0;

    public void InitializeExtensions(BrowserExtensionService extensionService)
    {
        _extensionService = extensionService;
        RefreshExtensionCollections();
    }

    private void RefreshExtensionCollections()
    {
        InstalledExtensions.Clear();
        EnabledExtensions.Clear();

        if (_extensionService is null)
        {
            IsBitwardenInstalled = false;
            OnPropertyChanged(nameof(HasNoExtensions));
            OnPropertyChanged(nameof(HasNoEnabledExtensions));
            return;
        }

        foreach (var ext in _extensionService.GetAll())
        {
            var vm = new BrowserExtensionItemViewModel(ext, HandleExtensionEnabledToggled);
            InstalledExtensions.Add(vm);
            if (ext.IsEnabled)
            {
                EnabledExtensions.Add(vm);
            }
        }

        IsBitwardenInstalled = InstalledExtensions.Any(e =>
            string.Equals(e.Id, BrowserExtensionService.BitwardenWebStoreId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Config.WebStoreId, BrowserExtensionService.BitwardenWebStoreId, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(HasNoExtensions));
        OnPropertyChanged(nameof(HasNoEnabledExtensions));
    }

    private async void HandleExtensionEnabledToggled(BrowserExtensionItemViewModel item, bool enabled)
    {
        if (_extensionService is null)
        {
            return;
        }

        try
        {
            _extensionService.SetEnabled(item.Id, enabled);
            RefreshExtensionCollections();
            if (ExtensionsChangedRequested is not null)
            {
                await ExtensionsChangedRequested.Invoke();
            }
        }
        catch (Exception ex)
        {
            ExtensionStatusMessage = $"Failed to update extension: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenExtensionsSettings()
    {
        CloseAllOverlays();
        ActiveSettingsTab = "Extensions";
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private async Task InstallWebStoreExtensionAsync()
    {
        await InstallWebStoreByInputAsync(WebStoreExtensionInput);
    }

    [RelayCommand]
    private async Task InstallBitwardenAsync()
    {
        await InstallWebStoreByInputAsync(BrowserExtensionService.BitwardenWebStoreId);
    }

    private async Task InstallWebStoreByInputAsync(string input)
    {
        if (_extensionService is null || IsInstallingExtension || string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        IsInstallingExtension = true;
        ExtensionStatusMessage = "Downloading and unpacking extension from Chrome Web Store...";
        try
        {
            var installed = await _extensionService.InstallFromWebStoreAsync(input);
            WebStoreExtensionInput = string.Empty;
            RefreshExtensionCollections();
            if (ExtensionsChangedRequested is not null)
            {
                await ExtensionsChangedRequested.Invoke();
            }
            ExtensionStatusMessage = $"Installed \"{installed.Name}\" (v{installed.Version}). Click its icon in the title bar to open it.";
        }
        catch (Exception ex)
        {
            ExtensionStatusMessage = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsInstallingExtension = false;
        }
    }

    [RelayCommand]
    private void InstallCrxExtension()
    {
        BrowseCrxFileRequested?.Invoke();
    }

    [RelayCommand]
    private void LoadUnpackedExtension()
    {
        BrowseExtensionFolderRequested?.Invoke();
    }

    public async Task InstallExtensionFromCrxFileAsync(string filePath)
    {
        if (_extensionService is null || IsInstallingExtension)
        {
            return;
        }

        IsInstallingExtension = true;
        ExtensionStatusMessage = "Extracting extension package...";
        try
        {
            var installed = await Task.Run(() => _extensionService.InstallFromCrxOrZip(filePath));
            RefreshExtensionCollections();
            if (ExtensionsChangedRequested is not null)
            {
                await ExtensionsChangedRequested.Invoke();
            }
            ExtensionStatusMessage = $"Installed \"{installed.Name}\" (v{installed.Version}).";
        }
        catch (Exception ex)
        {
            ExtensionStatusMessage = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsInstallingExtension = false;
        }
    }

    public async Task InstallExtensionFromFolderAsync(string folderPath)
    {
        if (_extensionService is null || IsInstallingExtension)
        {
            return;
        }

        IsInstallingExtension = true;
        ExtensionStatusMessage = "Loading unpacked extension folder...";
        try
        {
            var installed = await Task.Run(() => _extensionService.InstallFromFolder(folderPath));
            RefreshExtensionCollections();
            if (ExtensionsChangedRequested is not null)
            {
                await ExtensionsChangedRequested.Invoke();
            }
            ExtensionStatusMessage = $"Loaded \"{installed.Name}\" (v{installed.Version}).";
        }
        catch (Exception ex)
        {
            ExtensionStatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsInstallingExtension = false;
        }
    }

    [RelayCommand]
    private async Task RemoveExtensionAsync(BrowserExtensionItemViewModel? item)
    {
        if (_extensionService is null || item is null)
        {
            return;
        }

        try
        {
            _extensionService.Remove(item.Id);
            RefreshExtensionCollections();
            if (ExtensionsChangedRequested is not null)
            {
                await ExtensionsChangedRequested.Invoke();
            }
            ExtensionStatusMessage = $"Removed \"{item.Name}\".";
        }
        catch (Exception ex)
        {
            ExtensionStatusMessage = $"Remove failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenExtensionOptions(BrowserExtensionItemViewModel? item)
    {
        if (item is not null)
        {
            OpenExtensionOptionsRequested?.Invoke(item.Config);
        }
    }
}
