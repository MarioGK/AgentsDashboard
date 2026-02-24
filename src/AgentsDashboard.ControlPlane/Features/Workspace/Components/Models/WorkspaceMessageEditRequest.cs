namespace AgentsDashboard.ControlPlane.Components.Workspace.Models;

public sealed record WorkspaceMessageEditRequest(
    string MessageId,
    string PromptEntryId,
    string NewContent);
