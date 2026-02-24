namespace AgentsDashboard.ControlPlane.Components.Workspace.Models;

public sealed record WorkspaceChatMessage(
    string Id,
    WorkspaceChatMessageKind Kind,
    string Title,
    string Content,
    DateTime TimestampUtc,
    string Meta,
    string PromptEntryId = "",
    bool IsEditable = false,
    bool HasImages = false);
