using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]
public sealed record StatusRequestMessage
{
    [Key(0)]
    public required string RequestId { get; init; }

    [Key(1)]
    public long Timestamp { get; init; }
}
