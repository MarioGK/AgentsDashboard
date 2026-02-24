namespace AgentsDashboard.Contracts.Features.Runtime.Models.Domain;

public sealed class TaskRuntimeEventCheckpointDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RuntimeId { get; set; } = string.Empty;
    public long LastDeliveryId { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
