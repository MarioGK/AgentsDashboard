using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record CancelRuntimeCommandRequest
{
    [Key(0)] public required string CommandId { get; init; }
}
