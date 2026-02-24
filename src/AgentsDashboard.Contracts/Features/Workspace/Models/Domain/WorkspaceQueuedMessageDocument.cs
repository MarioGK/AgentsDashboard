namespace AgentsDashboard.Contracts.Features.Workspace.Models.Domain;

public sealed class WorkspaceQueuedMessageDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool HasImages { get; set; }
    public string ImagePayloadJson { get; set; } = string.Empty;
    public HarnessExecutionMode? ModeOverride { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int Order { get; set; }
}
