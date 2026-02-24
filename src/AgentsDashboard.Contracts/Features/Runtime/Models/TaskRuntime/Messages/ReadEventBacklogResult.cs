using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record ReadEventBacklogResult
{
    [Key(0)]
    public bool Success { get; init; }

    [Key(1)]
    public string? ErrorMessage { get; init; }

    [Key(2)]
    public List<JobEventMessage> Events { get; set; } = [];

    [Key(3)]
    public long LastDeliveryId { get; init; }

    [Key(4)]
    public bool HasMore { get; init; }
}
