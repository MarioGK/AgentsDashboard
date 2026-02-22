using MudBlazor;

namespace AgentsDashboard.ControlPlane.Components.Workspace.Models;

public sealed record WorkspaceRepositoryListItem(
    string Id,
    string Name,
    string BranchLabel,
    string HealthLabel,
    Color HealthColor,
    int Progress,
    bool IsSelected);
