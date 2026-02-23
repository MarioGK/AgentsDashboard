namespace AgentsDashboard.Contracts.Features.Workspace.Models.Domain;

































































public sealed class WorkspacePromptEntryDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public bool HasImages { get; set; }
    public string ImageMetadataJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
