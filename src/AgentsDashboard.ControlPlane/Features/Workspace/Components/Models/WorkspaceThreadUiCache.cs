

namespace AgentsDashboard.ControlPlane.Components.Workspace.Models;

public sealed class WorkspaceThreadUiCache
{
    public string SelectedRunId { get; set; } = string.Empty;

    public string ComposerDraft { get; set; } = string.Empty;

    public IReadOnlyList<WorkspaceImageInput> ComposerImages { get; set; } = [];

    public string ComposerGhostSuggestion { get; set; } = string.Empty;

    public string ComposerGhostSuffix { get; set; } = string.Empty;

    public bool HasUnreadActivity { get; set; }

    public DateTime LastActivityUtc { get; set; }
}
