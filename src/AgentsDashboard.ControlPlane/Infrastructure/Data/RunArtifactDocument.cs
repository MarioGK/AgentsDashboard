namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class RunArtifactDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileStorageId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
