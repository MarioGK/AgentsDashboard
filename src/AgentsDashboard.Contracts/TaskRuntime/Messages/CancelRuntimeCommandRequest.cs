using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]
public sealed record CancelRuntimeCommandRequest
{
    [Key(0)] public required string CommandId { get; init; }
}
