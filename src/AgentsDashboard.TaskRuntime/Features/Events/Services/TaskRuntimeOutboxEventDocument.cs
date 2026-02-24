using LiteDB;

namespace AgentsDashboard.TaskRuntime.Features.Events.Services;

public sealed class TaskRuntimeOutboxEventDocument
{
    [BsonId]
    public long DeliveryId { get; set; }

    public string RunId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
