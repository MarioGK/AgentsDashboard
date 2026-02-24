using MudBlazor;

namespace AgentsDashboard.ControlPlane.Components.Workspace.Models;

public sealed record WorkspaceThreadState(
    string TaskId,
    string Title,
    string Harness,
    string LatestStateLabel,
    Color LatestStateColor,
    bool IsSelected,
    bool HasUnread,
    DateTime LastActivityUtc,
    string LatestRunHint,
    bool IsPending = false,
    string PendingStatusText = "");
