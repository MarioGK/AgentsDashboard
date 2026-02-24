using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record ReadEventBacklogRequest
{
    [Key(0)]
    public long AfterDeliveryId { get; init; }

    [Key(1)]
    public int MaxEvents { get; set; } = 500;
}
