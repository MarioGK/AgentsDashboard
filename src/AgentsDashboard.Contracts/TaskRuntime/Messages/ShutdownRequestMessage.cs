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
public sealed record ShutdownRequestMessage
{
    [Key(0)]
    public required string Reason { get; init; }

    [Key(1)]
    public long GracePeriodSeconds { get; set; } = 30;

    [Key(2)]
    public long Timestamp { get; init; }
}
