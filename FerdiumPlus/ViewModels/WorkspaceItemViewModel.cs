using CommunityToolkit.Mvvm.ComponentModel;
using FerdiumApp.Models;

namespace FerdiumApp.ViewModels;

public sealed partial class WorkspaceItemViewModel : ObservableObject
{
    public WorkspaceConfig? Config { get; }
    public string? Id => Config?.Id;
    public string Name { get; }
    public bool IsAllServices => Config is null;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private int _serviceCount;

    public WorkspaceItemViewModel(WorkspaceConfig? config, string displayName, int serviceCount)
    {
        Config = config;
        Name = displayName;
        _serviceCount = serviceCount;
    }
}
