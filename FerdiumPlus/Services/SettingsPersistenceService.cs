using System.IO;
using System.Text.Json;
using FerdiumApp.Models;

namespace FerdiumApp.Services;

public sealed class SettingsPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _stateFilePath;

    public SettingsPersistenceService(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _stateFilePath = customPath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "FerdiumPlus");
            Directory.CreateDirectory(folder);
            _stateFilePath = Path.Combine(folder, "state.json");
        }
    }

    public AppState Load()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                var json = File.ReadAllText(_stateFilePath);
                var loaded = JsonSerializer.Deserialize<AppState>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.Settings ??= new AppSettings();
                    loaded.Services ??= new List<ServiceConfig>();
                    loaded.Workspaces ??= new List<WorkspaceConfig>();
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to clean default state if file is unreadable/corrupted
        }

        return CreateDefaultState();
    }

    public void Save(AppState state)
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{_stateFilePath}.tmp";
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _stateFilePath, overwrite: true);
    }

    private static AppState CreateDefaultState()
    {
        var catalog = new ServiceCatalogService();
        var all = catalog.GetAll();

        var starterServices = new List<ServiceConfig>
        {
            catalog.CreateServiceFromCatalog(all.First(c => c.Id == "whatsapp")),
            catalog.CreateServiceFromCatalog(all.First(c => c.Id == "slack")),
            catalog.CreateServiceFromCatalog(all.First(c => c.Id == "discord")),
            catalog.CreateServiceFromCatalog(all.First(c => c.Id == "gmail")),
        };

        var workWorkspace = new WorkspaceConfig
        {
            Name = "Work",
            ServiceIds = new List<string> { starterServices[1].Id, starterServices[3].Id },
        };

        var personalWorkspace = new WorkspaceConfig
        {
            Name = "Personal",
            ServiceIds = new List<string> { starterServices[0].Id, starterServices[2].Id },
        };

        return new AppState
        {
            Settings = new AppSettings(),
            Services = starterServices,
            Workspaces = new List<WorkspaceConfig> { workWorkspace, personalWorkspace },
            ActiveServiceId = starterServices[0].Id,
        };
    }
}
