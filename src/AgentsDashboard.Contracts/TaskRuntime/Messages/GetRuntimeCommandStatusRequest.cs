using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]
public sealed record GetRuntimeCommandStatusRequest
{
    [Key(0)] public required string CommandId { get; init; }
}
