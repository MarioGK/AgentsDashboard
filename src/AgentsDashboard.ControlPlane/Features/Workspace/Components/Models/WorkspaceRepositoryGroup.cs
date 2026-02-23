namespace AgentsDashboard.ControlPlane.Components.Workspace.Models;

public sealed record WorkspaceRepositoryGroup(
    string Name,
    IReadOnlyList<WorkspaceRepositoryListItem> Repositories);
