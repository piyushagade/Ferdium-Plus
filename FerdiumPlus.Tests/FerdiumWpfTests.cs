using System.IO;
using FerdiumApp.Models;
using FerdiumApp.Services;
using FerdiumApp.ViewModels;

namespace FerdiumApp.Tests;

public sealed class FerdiumWpfTests : IDisposable
{
    private readonly string _tempDir;

    public FerdiumWpfTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ferdium-wpf-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("(3) WhatsApp", 3, false)]
    [InlineData("[12] Slack - General", 12, false)]
    [InlineData("Inbox (5) - Gmail", 0, false)]
    [InlineData("Gmail (5)", 5, false)]
    [InlineData("• Discord", 0, true)]
    [InlineData("* Slack", 0, true)]
    [InlineData("WhatsApp", 0, false)]
    [InlineData("", 0, false)]
    public void UnreadBadgeParser_ParsesTitlesCorrectly(string title, int expectedDirect, bool expectedIndirect)
    {
        var (direct, indirect) = UnreadBadgeParser.ParseFromTitle(title);
        Assert.Equal(expectedDirect, direct);
        Assert.Equal(expectedIndirect, indirect);
    }

    [Fact]
    public void ServiceCatalog_SearchAndNormalizeUrl_WorkCorrectly()
    {
        var catalog = new ServiceCatalogService();

        var emailMatches = catalog.Search("email");
        Assert.Contains(emailMatches, c => c.Id == "gmail");
        Assert.Contains(emailMatches, c => c.Id == "outlook");

        var messageMatches = catalog.Search("messages");
        Assert.Contains(messageMatches, c => c.Id == "gmessages");
        Assert.Equal("gmessages", ServiceIconService.ResolveBundledId(null, "https://messages.google.com/web/"));

        var voiceMatches = catalog.Search("voice");
        Assert.Contains(voiceMatches, c => c.Id == "gvoice");
        Assert.Equal("gvoice", ServiceIconService.ResolveBundledId(null, "https://voice.google.com"));

        Assert.Contains(catalog.Search("instagram"), c => c.Id == "instagram");
        Assert.Contains(catalog.Search("facebook"), c => c.Id == "facebook");
        Assert.Contains(catalog.Search("youtube"), c => c.Id == "youtube");
        Assert.Equal("instagram", ServiceIconService.ResolveBundledId(null, "https://www.instagram.com"));
        Assert.Equal("facebook", ServiceIconService.ResolveBundledId(null, "https://www.facebook.com"));
        Assert.Equal("youtube", ServiceIconService.ResolveBundledId(null, "https://www.youtube.com"));

        Assert.Equal("https://example.com", ServiceCatalogService.NormalizeUrl("example.com"));
        Assert.Equal("https://example.com", ServiceCatalogService.NormalizeUrl("https://example.com"));
        Assert.Equal("MT", ServiceCatalogService.BuildMonogram("Microsoft Teams"));
        Assert.Equal("SL", ServiceCatalogService.BuildMonogram("Slack"));
    }

    [Fact]
    public void SettingsPersistence_SavesAndLoadsRoundTrip()
    {
        var statePath = Path.Combine(_tempDir, "state.json");
        var persistence = new SettingsPersistenceService(statePath);

        var initial = persistence.Load();
        Assert.NotEmpty(initial.Services);
        Assert.True(initial.Settings.UseMica);
        Assert.True(initial.Settings.LazyLoadServices);

        initial.Settings.AutoHibernateMinutes = 30;
        initial.Settings.ThemeMode = AppThemeMode.Light;
        persistence.Save(initial);

        var reloaded = persistence.Load();
        Assert.Equal(30, reloaded.Settings.AutoHibernateMinutes);
        Assert.Equal(AppThemeMode.Light, reloaded.Settings.ThemeMode);
        Assert.Equal(initial.Services.Count, reloaded.Services.Count);
    }

    [Fact]
    public async Task MainWindowViewModel_FiltersWorkspacesAndAutoHibernatesIdleServices()
    {
        var statePath = Path.Combine(_tempDir, "vm-state.json");
        var vm = new MainWindowViewModel(
            new SettingsPersistenceService(statePath),
            new ServiceCatalogService());

        var hibernatedIds = new List<string>();
        vm.ServiceHibernationRequested += (id, _) =>
        {
            hibernatedIds.Add(id);
            return Task.CompletedTask;
        };

        // Switch to "Work" workspace and verify filtering
        var workWs = vm.Workspaces.First(w => w.Name == "Work");
        vm.SelectWorkspace(workWs);
        Assert.Equal(2, vm.VisibleServices.Count);

        // Simulate one active loaded service and one stale idle loaded service
        var active = vm.AllServices[0];
        var stale = vm.AllServices[1];
        active.IsActive = true;
        active.IsLoaded = true;
        stale.IsActive = false;
        stale.IsLoaded = true;
        stale.LastUsedUtc = DateTime.UtcNow.AddMinutes(-45);

        vm.AutoHibernateMinutes = 15;
        await vm.CheckAutoHibernateAsync();

        Assert.Single(hibernatedIds);
        Assert.Equal(stale.Id, hibernatedIds[0]);
        Assert.False(stale.IsLoaded);
        Assert.True(active.IsLoaded);
    }

    [Fact]
    public void FerdiumSettingsImporter_ImportsSettingsAndShortcutsFromJson()
    {
        var configDir = Path.Combine(_tempDir, "ferdium-config");
        Directory.CreateDirectory(configDir);
        var settingsPath = Path.Combine(configDir, "settings.json");
        var shortcutsPath = Path.Combine(configDir, "shortcuts.json");

        File.WriteAllText(settingsPath, """
        {
          "autoLaunchOnStart": true,
          "runInBackground": true,
          "confirmOnQuit": true,
          "enableSystemTray": true,
          "reloadAfterResume": true,
          "reloadAfterResumeTime": 25,
          "startMinimized": true,
          "minimizeToSystemTray": true,
          "closeToSystemTray": true,
          "keepAllWorkspacesLoaded": true,
          "isTodosFeatureEnabled": true,
          "scheduledDNDEnabled": true,
          "scheduledDNDStart": "22:00",
          "scheduledDNDEnd": "07:30",
          "showServiceName": true,
          "iconSize": 30,
          "navigationBarBehaviour": "custom",
          "hibernationStrategy": "600",
          "adaptableDarkMode": false,
          "darkMode": true,
          "accentColor": "#0078D4",
          "serviceRibbonWidth": 80,
          "privateNotifications": true,
          "searchEngine": "duckDuckGo",
          "locale": "de-DE",
          "enableSpellchecking": false,
          "enableGPUAcceleration": false,
          "userAgentPref": "CustomAgent/1.0",
          "automaticUpdates": false,
          "beta": true
        }
        """);

        File.WriteAllText(shortcutsPath, """
        {
          "activateNextService": "Ctrl+PageDown",
          "activatePreviousService": "Ctrl+PageUp",
          "activateServiceUsesAlt": true
        }
        """);

        var settings = new AppSettings();
        var result = FerdiumSettingsImporter.ImportFromFile(settingsPath, settings);

        Assert.True(result.Success);
        Assert.True(result.ImportedCount >= 30);
        Assert.True(settings.AutoLaunchOnStart);
        Assert.True(settings.ConfirmOnQuit);
        Assert.Equal(25, settings.ReloadAfterResumeTime);
        Assert.True(settings.StartMinimized);
        Assert.True(settings.MinimizeToSystemTray);
        Assert.True(settings.KeepAllWorkspacesLoaded);
        Assert.True(settings.EnableTodos);
        Assert.True(settings.ScheduledDndEnabled);
        Assert.Equal("22:00", settings.ScheduledDndStart);
        Assert.Equal("07:30", settings.ScheduledDndEnd);
        Assert.True(settings.ShowServiceNames);
        Assert.Equal(30, settings.IconSize);
        Assert.Equal("custom", settings.NavigationBarBehaviour);
        Assert.Equal(600, settings.HibernationStrategySeconds);
        Assert.Equal(10, settings.AutoHibernateMinutes);
        Assert.Equal(AppThemeMode.Dark, settings.ThemeMode);
        Assert.Equal("#0078D4", settings.AccentColor);
        Assert.Equal(80, settings.ServiceRibbonWidth);
        Assert.True(settings.PrivateNotifications);
        Assert.Equal("duckDuckGo", settings.SearchEngine);
        Assert.Equal("de-DE", settings.Locale);
        Assert.False(settings.EnableSpellchecking);
        Assert.False(settings.EnableGpuAcceleration);
        Assert.Equal("CustomAgent/1.0", settings.UserAgentPref);
        Assert.False(settings.AutomaticUpdates);
        Assert.True(settings.BetaUpdates);
        Assert.Equal("Ctrl+PageDown", settings.ShortcutActivateNextService);
        Assert.Equal("Ctrl+PageUp", settings.ShortcutActivatePreviousService);
        Assert.True(settings.ActivateServiceUsesAlt);
    }

    [Theory]
    [InlineData(true, "17:00", "09:00", 22, 0, true)]
    [InlineData(true, "17:00", "09:00", 8, 30, true)]
    [InlineData(true, "17:00", "09:00", 12, 0, false)]
    [InlineData(true, "09:00", "17:00", 12, 0, true)]
    [InlineData(true, "09:00", "17:00", 18, 0, false)]
    [InlineData(false, "17:00", "09:00", 22, 0, false)]
    public void ScheduledDnd_EvaluatesOvernightAndDaytimeWindows(
        bool enabled, string start, string end, int hour, int minute, bool expected)
    {
        var actual = FerdiumSettingsImporter.IsWithinScheduledDnd(
            enabled, start, end, new TimeSpan(hour, minute, 0));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NotificationService_FormatsNormalAndPrivateNotifications()
    {
        var normal = NotificationService.FormatNotification("WhatsApp", "Alice", "Hey there!", privateNotifications: false);
        Assert.Equal("Alice", normal.Title);
        Assert.Equal("Hey there!", normal.Body);
        Assert.Equal("WhatsApp", normal.Attribution);

        var sameTitle = NotificationService.FormatNotification("WhatsApp", "WhatsApp", "2 new messages", privateNotifications: false);
        Assert.Equal("WhatsApp", sameTitle.Title);
        Assert.Null(sameTitle.Attribution);

        var masked = NotificationService.FormatNotification("WhatsApp", "Alice", "Secret message", privateNotifications: true);
        Assert.Equal("WhatsApp", masked.Title);
        Assert.Equal("New notification", masked.Body);
        Assert.Null(masked.Attribution);
    }

    [Fact]
    public void NotificationService_SanitizesToastKeysDeterministically()
    {
        Assert.Equal("whatsapp_1", NotificationService.SanitizeToastKey("whatsapp_1"));
        Assert.Equal("ferdium", NotificationService.SanitizeToastKey("   "));

        var longTag = new string('a', 80) + ":chat/123";
        var hash1 = NotificationService.SanitizeToastKey(longTag);
        var hash2 = NotificationService.SanitizeToastKey(longTag);
        Assert.Equal(hash1, hash2);
        Assert.Equal(16, hash1.Length);
    }

    [Fact]
    public async Task ActivateServiceCommand_StaysEnabledWhileActivationIsInFlight()
    {
        var statePath = Path.Combine(_tempDir, "activate-state.json");
        var vm = new MainWindowViewModel(
            new SettingsPersistenceService(statePath),
            new ServiceCatalogService());

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.ServiceActivationRequested += async (_, _, _) => await gate.Task;

        var first = vm.VisibleServices[0];
        var second = vm.VisibleServices[1];

        var activationTask = vm.ActivateServiceCommand.ExecuteAsync(first);

        // Command must remain enabled while WebView2 activation/suspension is awaited
        // so sidebar service buttons do not flash their disabled background.
        Assert.True(vm.ActivateServiceCommand.CanExecute(first));
        Assert.True(vm.ActivateServiceCommand.CanExecute(second));

        gate.SetResult();
        await activationTask;

        Assert.True(first.IsActive);
        Assert.False(second.IsActive);
    }

    [Fact]
    public async Task ServiceContextMenuCommands_ExecuteExpectedActions()
    {
        var statePath = Path.Combine(_tempDir, "ctx-state.json");
        var vm = new MainWindowViewModel(
            new SettingsPersistenceService(statePath),
            new ServiceCatalogService());

        var activatedIds = new List<string>();
        var reloadedIds = new List<string>();
        var hibernatedIds = new List<string>();
        var devToolsIds = new List<string>();

        vm.ServiceActivationRequested += (cfg, _, _) =>
        {
            activatedIds.Add(cfg.Id);
            var item = vm.AllServices.First(s => s.Id == cfg.Id);
            item.IsLoaded = true;
            return Task.CompletedTask;
        };
        vm.ServiceReloadRequested += id => reloadedIds.Add(id);
        vm.ServiceHibernationRequested += (id, _) =>
        {
            hibernatedIds.Add(id);
            return Task.CompletedTask;
        };
        vm.ServiceDevToolsRequested += id => devToolsIds.Add(id);

        var target = vm.VisibleServices[0];
        Assert.Equal("Wake Up", target.HibernateMenuLabel);
        Assert.Equal("Mute", target.MuteMenuLabel);

        // Reload on an unloaded service activates it; reload on a loaded service reloads it
        await vm.ReloadServiceCommand.ExecuteAsync(target);
        Assert.Contains(target.Id, activatedIds);
        Assert.Empty(reloadedIds);
        Assert.Equal("Hibernate (Free RAM)", target.HibernateMenuLabel);

        await vm.ReloadServiceCommand.ExecuteAsync(target);
        Assert.Single(reloadedIds, target.Id);

        // Toggle mute updates label and state
        vm.ToggleServiceMuteCommand.Execute(target);
        Assert.True(target.IsMuted);
        Assert.Equal("Unmute", target.MuteMenuLabel);

        // Toggle hibernate puts loaded service to sleep, and wakes it if called again
        await vm.ToggleHibernateServiceCommand.ExecuteAsync(target);
        Assert.Single(hibernatedIds, target.Id);
        Assert.False(target.IsLoaded);
        Assert.Equal("Wake Up", target.HibernateMenuLabel);

        // Open DevTools on a hibernated service activates it first, then opens DevTools
        await vm.OpenDevToolsForServiceCommand.ExecuteAsync(target);
        Assert.True(target.IsLoaded);
        Assert.Single(devToolsIds, target.Id);

        // Remove Service removes it from VisibleServices and AllServices
        var initialCount = vm.AllServices.Count;
        await vm.RemoveServiceCommand.ExecuteAsync(target);
        Assert.Equal(initialCount - 1, vm.AllServices.Count);
        Assert.DoesNotContain(vm.AllServices, s => s.Id == target.Id);
    }

    [Fact]
    public void ReorderService_UpdatesOrderAndPersistsAcrossReload()
    {
        var statePath = Path.Combine(_tempDir, "reorder-state.json");
        var persistence = new SettingsPersistenceService(statePath);
        var vm = new MainWindowViewModel(persistence, new ServiceCatalogService());

        Assert.True(vm.VisibleServices.Count >= 3);
        var first = vm.VisibleServices[0];
        var second = vm.VisibleServices[1];
        var third = vm.VisibleServices[2];

        // Move first after third
        Assert.True(vm.ReorderService(first, third, insertAfter: true));
        Assert.Equal(second.Id, vm.VisibleServices[0].Id);
        Assert.Equal(third.Id, vm.VisibleServices[1].Id);
        Assert.Equal(first.Id, vm.VisibleServices[2].Id);

        // Move first back before second
        Assert.True(vm.ReorderService(first, second, insertAfter: false));
        Assert.Equal(first.Id, vm.VisibleServices[0].Id);
        Assert.Equal(second.Id, vm.VisibleServices[1].Id);
        Assert.Equal(third.Id, vm.VisibleServices[2].Id);

        // Move third before first and verify persisted order on reload
        Assert.True(vm.ReorderService(third, first, insertAfter: false));
        var reloaded = persistence.Load();
        Assert.Equal(third.Id, reloaded.Services[0].Id);
        Assert.Equal(first.Id, reloaded.Services[1].Id);
        Assert.Equal(second.Id, reloaded.Services[2].Id);
    }

    [Fact]
    public async Task SessionSharing_ResolvesProfileFolderAndReloadsService()
    {
        var statePath = Path.Combine(_tempDir, "session-share-state.json");
        var vm = new MainWindowViewModel(
            new SettingsPersistenceService(statePath),
            new ServiceCatalogService());

        var hibernatedIds = new List<string>();
        var activatedConfigs = new List<ServiceConfig>();
        vm.ServiceHibernationRequested += (id, _) =>
        {
            hibernatedIds.Add(id);
            return Task.CompletedTask;
        };
        vm.ServiceActivationRequested += (cfg, _, _) =>
        {
            activatedConfigs.Add(cfg);
            return Task.CompletedTask;
        };

        var primary = vm.AllServices[0];
        var secondary = vm.AllServices[1];
        secondary.IsLoaded = true;

        // Shared by default so extensions like Bitwarden share one login across services
        Assert.Equal("_shared", WebViewHostManager.ResolveProfileFolderName(secondary.Config));

        // Explicit isolation keeps a service in its own profile folder
        var isolatedService = new ServiceConfig { Id = "svc-iso", SessionProfileKey = "isolated" };
        Assert.Equal("svc-iso", WebViewHostManager.ResolveProfileFolderName(isolatedService));

        // Open session share dialog for secondary and pick primary's dedicated session
        vm.OpenSessionShare(secondary);
        Assert.True(vm.IsSessionShareOpen);

        var shareWithPrimary = vm.SessionShareOptions.First(o => o.ProfileKey == primary.Id);
        await vm.ApplySessionShareAsync(shareWithPrimary);

        Assert.False(vm.IsSessionShareOpen);
        Assert.Equal(primary.Id, secondary.Config.SessionProfileKey);
        Assert.Equal(primary.Id, WebViewHostManager.ResolveProfileFolderName(secondary.Config));
        Assert.Contains(primary.Name, secondary.SessionBadgeLabel);
        Assert.Contains(secondary.Id, hibernatedIds);
        Assert.Contains(activatedConfigs, c => c.Id == secondary.Id && c.SessionProfileKey == primary.Id);

        // Shared default profile maps to "_shared"
        var sharedDefault = new ServiceConfig { Id = "svc1", SessionProfileKey = "shared" };
        Assert.Equal("_shared", WebViewHostManager.ResolveProfileFolderName(sharedDefault));
    }

    [Fact]
    public void BrowserExtensionService_ParsesWebStoreIdsAndCrxHeaders()
    {
        const string bitwardenId = BrowserExtensionService.BitwardenWebStoreId;

        Assert.Equal(bitwardenId, BrowserExtensionService.ParseWebStoreId(bitwardenId));
        Assert.Equal(
            bitwardenId,
            BrowserExtensionService.ParseWebStoreId(
                $"https://chromewebstore.google.com/detail/bitwarden-password-manage/{bitwardenId}?hl=en"));
        Assert.Equal(
            bitwardenId,
            BrowserExtensionService.ParseWebStoreId(
                $"https://chrome.google.com/webstore/detail/bitwarden-free-password-m/{bitwardenId}"));
        Assert.Null(BrowserExtensionService.ParseWebStoreId("https://example.com/not-an-extension"));

        // CRX3 header stripping
        var zipPayload = new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4, 5, 6, 7, 8 };
        Assert.Equal(zipPayload, BrowserExtensionService.ExtractZipFromCrx(zipPayload));

        var fakeHeader = System.Text.Encoding.ASCII.GetBytes("crx3-header");
        var crx3 = new byte[12 + fakeHeader.Length + zipPayload.Length];
        System.Text.Encoding.ASCII.GetBytes("Cr24").CopyTo(crx3, 0);
        BitConverter.GetBytes((uint)3).CopyTo(crx3, 4);
        BitConverter.GetBytes((uint)fakeHeader.Length).CopyTo(crx3, 8);
        fakeHeader.CopyTo(crx3, 12);
        zipPayload.CopyTo(crx3, 12 + fakeHeader.Length);

        Assert.Equal(zipPayload, BrowserExtensionService.ExtractZipFromCrx(crx3));
    }

    [Fact]
    public void BrowserExtensionService_InstallsUnpackedExtensionAndResolvesI18nManifest()
    {
        var extDir = Path.Combine(_tempDir, "sample-ext");
        Directory.CreateDirectory(Path.Combine(extDir, "_locales", "en"));
        Directory.CreateDirectory(Path.Combine(extDir, "images"));

        File.WriteAllText(
            Path.Combine(extDir, "_locales", "en", "messages.json"),
            """{"appName":{"message":"Bitwarden Password Manager"},"appDesc":{"message":"Secure password manager"}}""");
        File.WriteAllBytes(Path.Combine(extDir, "images", "icon48.png"), new byte[] { 137, 80, 78, 71 });
        File.WriteAllText(
            Path.Combine(extDir, "manifest.json"),
            """
            {
              "manifest_version": 3,
              "name": "__MSG_appName__",
              "description": "__MSG_appDesc__",
              "version": "2025.2.0",
              "default_locale": "en",
              "action": {
                "default_popup": "popup/index.html",
                "default_icon": { "48": "images/icon48.png" }
              }
            }
            """);

        var registryRoot = Path.Combine(_tempDir, "ext-store");
        var service = new BrowserExtensionService(registryRoot);

        var installed = service.InstallFromFolder(extDir);
        Assert.Equal("Bitwarden Password Manager", installed.Name);
        Assert.Equal("Secure password manager", installed.Description);
        Assert.Equal("2025.2.0", installed.Version);
        Assert.Equal(3, installed.ManifestVersion);
        Assert.Equal("popup/index.html", installed.PopupPath);
        Assert.True(File.Exists(installed.IconPath));

        // Persists across service reload
        var reloaded = new BrowserExtensionService(registryRoot);
        Assert.Single(reloaded.GetAll());
        reloaded.SetEnabled(installed.Id, false);
        Assert.Empty(reloaded.GetEnabled());
        reloaded.Remove(installed.Id);
        Assert.Empty(reloaded.GetAll());
    }

    [Fact]
    public async Task MultiAccount_AddAnotherAccountAndDuplicateIsolation_WorkCorrectly()
    {
        var statePath = Path.Combine(_tempDir, "multi-account-state.json");
        var persistence = new SettingsPersistenceService(statePath);
        var catalog = new ServiceCatalogService();
        var vm = new MainWindowViewModel(persistence, catalog);

        // Badge computation
        Assert.Equal("P", ServiceItemViewModel.ComputeAccountBadge(null, "Personal"));
        Assert.Equal("W2", ServiceItemViewModel.ComputeAccountBadge(null, "Work 2"));
        Assert.Equal("BIZ", ServiceItemViewModel.ComputeAccountBadge("biz", "Business"));
        Assert.Equal(string.Empty, ServiceItemViewModel.ComputeAccountBadge(null, null));

        // Add Instagram once -> opens Edit Service popup in AddNewService mode, then Save adds it
        var instaCatalog = catalog.GetAll().First(c => c.Id == "instagram");
        await vm.AddFromCatalogAsync(instaCatalog);
        Assert.True(vm.IsEditServiceOpen);
        Assert.Equal(EditServiceDialogMode.AddNewService, vm.EditDialogMode);
        await vm.SaveEditServiceAsync();
        var firstInsta = vm.AllServices.Last();
        Assert.Null(firstInsta.Config.SessionProfileKey);

        // Add Instagram a second time from catalog -> opens Edit Service popup pre-isolated
        await vm.AddFromCatalogAsync(instaCatalog);
        Assert.True(vm.IsEditServiceOpen);
        Assert.True(vm.EditIsolateSession);
        await vm.SaveEditServiceAsync();
        var secondInsta = vm.AllServices.Last();
        Assert.Equal("isolated", secondInsta.Config.SessionProfileKey);
        Assert.Equal(secondInsta.Id, WebViewHostManager.ResolveProfileFolderName(secondInsta.Config));

        // Use "Add Another Account..." on firstInsta
        vm.OpenAddAnotherAccount(firstInsta);
        Assert.True(vm.IsEditServiceOpen);
        Assert.True(vm.IsAddingAccountMode);
        Assert.True(vm.EditIsolateSession);

        vm.EditAccountLabel = "Work";
        vm.EditAccountBadge = "W";
        vm.SelectEditBadgeColor("#107C10");
        await vm.SaveEditServiceAsync();

        var workInsta = vm.AllServices.First(s => s.AccountLabel == "Work");
        Assert.Equal("Instagram (Work)", workInsta.DisplayName);
        Assert.Equal("W", workInsta.AccountBadgeText);
        Assert.Equal("isolated", workInsta.Config.SessionProfileKey);
        Assert.Equal(workInsta.Id, WebViewHostManager.ResolveProfileFolderName(workInsta.Config));

        // Edit existing service label & verify quick switch matches account label
        vm.OpenEditService(firstInsta);
        Assert.False(vm.IsAddingAccountMode);
        vm.EditAccountLabel = "Personal";
        await vm.SaveEditServiceAsync();

        Assert.Equal("Instagram (Personal)", firstInsta.DisplayName);
        Assert.Equal("P", firstInsta.AccountBadgeText);
        vm.QuickSwitchQuery = "Personal";
        Assert.Contains(vm.QuickSwitchResults, s => s.Id == firstInsta.Id);
    }

    [Fact]
    public void MainWindowViewModel_QuickSwitchArrowNavigation_WrapsCorrectly()
    {
        var statePath = Path.Combine(_tempDir, "qs-state.json");
        var vm = new MainWindowViewModel(
            new SettingsPersistenceService(statePath),
            new ServiceCatalogService());

        vm.OpenQuickSwitch();
        Assert.Equal(vm.QuickSwitchResults[0], vm.SelectedQuickSwitchItem);

        vm.MoveQuickSwitchSelection(1);
        Assert.Equal(vm.QuickSwitchResults[1], vm.SelectedQuickSwitchItem);

        vm.MoveQuickSwitchSelection(-1);
        Assert.Equal(vm.QuickSwitchResults[0], vm.SelectedQuickSwitchItem);

        // Wrapping backwards from index 0 selects the last item
        vm.MoveQuickSwitchSelection(-1);
        Assert.Equal(vm.QuickSwitchResults[^1], vm.SelectedQuickSwitchItem);
    }

    [Fact]
    public async Task EditService_StartupHibernationMuteAndLocks_WorkCorrectly()
    {
        var statePath = Path.Combine(_tempDir, "edit-service-features.json");
        var persistence = new SettingsPersistenceService(statePath);
        var vm = new MainWindowViewModel(persistence, new ServiceCatalogService());

        var target = vm.AllServices[0];
        var other = vm.AllServices[1];

        // 1-3: Startup & Hibernation mutual exclusion + Never Hibernate
        vm.OpenEditService(target);
        vm.EditLoadOnStartup = true;
        Assert.True(vm.EditLoadOnStartup);
        Assert.False(vm.EditHibernateOnStartup);

        vm.EditHibernateOnStartup = true;
        Assert.True(vm.EditHibernateOnStartup);
        Assert.False(vm.EditLoadOnStartup);

        vm.EditLoadOnStartup = true;
        vm.EditNeverHibernate = true;

        // 4: Mute & Notifications sub-options + snooze
        vm.EditMuteNotifications = false;
        vm.EditMuteNotificationSound = true;
        vm.EditMuteAudio = true;
        vm.EditHideUnreadBadge = true;
        vm.EditHideNotificationPreview = true;
        vm.SetEditMuteSnooze("1h");
        Assert.NotNull(vm.EditMuteUntilUtc);

        // 5: Passcode validation + hashing
        vm.EditLockModeIndex = (int)ServiceLockMode.Passcode;
        vm.EditLockSecret = "123"; // too short
        vm.EditLockSecretConfirm = "123";
        await vm.SaveEditServiceAsync();
        Assert.True(vm.IsEditServiceOpen);
        Assert.True(vm.HasEditLockValidationError);

        vm.EditLockSecret = "2468";
        vm.EditLockSecretConfirm = "2468";
        vm.EditRelockOnSwitchAway = true;
        await vm.SaveEditServiceAsync();

        Assert.False(vm.IsEditServiceOpen);
        Assert.True(target.Config.LoadOnStartup);
        Assert.False(target.Config.HibernateOnStartup);
        Assert.False(target.Config.IsHibernationEnabled);
        Assert.True(target.Config.MuteNotificationSound);
        Assert.True(target.Config.IsAudioEffectivelyMuted);
        Assert.True(target.Config.HideUnreadBadge);
        Assert.True(target.Config.HideNotificationPreview);
        Assert.True(target.Config.IsNotificationsEffectivelyMuted());
        Assert.Equal(ServiceLockMode.Passcode, target.Config.LockMode);
        Assert.True(ServiceLockService.VerifySecret("2468", target.Config.LockSecretHash, target.Config.LockSecretSalt));
        Assert.False(ServiceLockService.VerifySecret("0000", target.Config.LockSecretHash, target.Config.LockSecretSalt));

        // HideUnreadBadge suppresses badge visibility
        target.UnreadCount = 5;
        Assert.False(target.HasBadge);

        // Never Hibernate exempts target from auto-hibernate
        var hibernatedIds = new List<string>();
        vm.ServiceHibernationRequested += (id, _) =>
        {
            hibernatedIds.Add(id);
            return Task.CompletedTask;
        };
        target.IsLoaded = true;
        target.IsActive = false;
        target.LastUsedUtc = DateTime.UtcNow.AddMinutes(-60);
        vm.AutoHibernateMinutes = 15;
        await vm.CheckAutoHibernateAsync();
        Assert.DoesNotContain(target.Id, hibernatedIds);

        // Reloading VM starts locked, clicking target opens Unlock overlay, correct passcode unlocks
        var reloadedVm = new MainWindowViewModel(persistence, new ServiceCatalogService());
        var lockedTarget = reloadedVm.AllServices.First(s => s.Id == target.Id);
        var secondSvc = reloadedVm.AllServices.First(s => s.Id == other.Id);
        Assert.True(lockedTarget.IsLocked);

        await reloadedVm.ActivateServiceAsync(lockedTarget);
        Assert.True(reloadedVm.IsServiceUnlockOpen);
        Assert.NotEqual(lockedTarget.Id, reloadedVm.ActiveService?.Id);

        reloadedVm.UnlockSecretInput = "9999";
        await reloadedVm.SubmitServiceUnlockAsync();
        Assert.True(reloadedVm.HasUnlockError);
        Assert.True(lockedTarget.IsLocked);

        reloadedVm.UnlockSecretInput = "2468";
        await reloadedVm.SubmitServiceUnlockAsync();
        Assert.False(reloadedVm.IsServiceUnlockOpen);
        Assert.False(lockedTarget.IsLocked);
        Assert.Equal(lockedTarget.Id, reloadedVm.ActiveService?.Id);

        // Switching away re-locks when RelockOnSwitchAway is enabled
        await reloadedVm.ActivateServiceAsync(secondSvc);
        Assert.True(lockedTarget.IsLocked);

        // Windows Hello lock flow with mocked verifier
        reloadedVm.OpenEditService(secondSvc);
        reloadedVm.EditLockModeIndex = (int)ServiceLockMode.WindowsHello;
        await reloadedVm.SaveEditServiceAsync();
        await reloadedVm.LockServiceAsync(secondSvc);
        Assert.True(secondSvc.IsLocked);

        reloadedVm.WindowsHelloVerifier = _ => Task.FromResult((true, string.Empty));
        await reloadedVm.ActivateServiceAsync(secondSvc);
        Assert.False(secondSvc.IsLocked);
        Assert.Equal(secondSvc.Id, reloadedVm.ActiveService?.Id);
    }

    [Fact]
    public async Task QuickLockAndAutoLock_LockOnButtonInactivityMinimizeAndSuspend()
    {
        var statePath = Path.Combine(_tempDir, "autolock-state.json");
        var vm = new MainWindowViewModel(
            new SettingsPersistenceService(statePath),
            new ServiceCatalogService());

        var hiddenIds = new List<string>();
        vm.ServiceHideRequested += id => hiddenIds.Add(id);

        var svc = vm.AllServices[0];
        await vm.ActivateServiceAsync(svc);
        Assert.False(vm.CanQuickLockActiveService);

        // Configure Passcode lock + Auto-Lock options in Edit Service
        vm.OpenEditService(svc);
        vm.EditLockModeIndex = (int)ServiceLockMode.Passcode;
        vm.EditLockSecret = "1357";
        vm.EditLockSecretConfirm = "1357";
        vm.SetEditAutoLockMinutes("5");
        vm.EditRelockOnMinimize = true;
        vm.EditRelockOnSystemSuspend = true;
        await vm.SaveEditServiceAsync();

        Assert.True(svc.Config.AutoLockEnabled);
        Assert.Equal(5, svc.Config.AutoLockMinutes);
        Assert.True(svc.Config.RelockOnMinimize);
        Assert.True(svc.Config.RelockOnSystemSuspend);
        Assert.True(vm.CanQuickLockActiveService);

        // Quick-Lock button locks active service & hides its WebView
        await vm.LockActiveServiceAsync();
        Assert.True(svc.IsLocked);
        Assert.Null(vm.ActiveService);
        Assert.False(vm.CanQuickLockActiveService);
        Assert.Contains(svc.Id, hiddenIds);

        // Unlock again and verify auto-lock after inactivity duration
        vm.UnlockTargetService = svc;
        vm.UnlockSecretInput = "1357";
        await vm.SubmitServiceUnlockAsync();
        Assert.False(svc.IsLocked);

        svc.LastUsedUtc = DateTime.UtcNow.AddMinutes(-3);
        await vm.CheckAutoLockAsync();
        Assert.False(svc.IsLocked);

        svc.LastUsedUtc = DateTime.UtcNow.AddMinutes(-6);
        await vm.CheckAutoLockAsync();
        Assert.True(svc.IsLocked);

        // Unlock and verify RelockOnMinimize + RelockOnSystemSuspend
        vm.UnlockTargetService = svc;
        vm.UnlockSecretInput = "1357";
        await vm.SubmitServiceUnlockAsync();
        await vm.LockServicesOnMinimizeAsync();
        Assert.True(svc.IsLocked);

        vm.UnlockTargetService = svc;
        vm.UnlockSecretInput = "1357";
        await vm.SubmitServiceUnlockAsync();
        await vm.LockServicesOnSystemSuspendAsync();
        Assert.True(svc.IsLocked);
    }
}
