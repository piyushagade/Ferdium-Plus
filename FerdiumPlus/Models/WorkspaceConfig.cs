namespace FerdiumApp.Models;

public sealed class WorkspaceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> ServiceIds { get; set; } = new();
}

public sealed class AppState
{
    public AppSettings Settings { get; set; } = new();
    public List<ServiceConfig> Services { get; set; } = new();
    public List<WorkspaceConfig> Workspaces { get; set; } = new();
    public string? ActiveServiceId { get; set; }
    public string? ActiveWorkspaceId { get; set; }
}
