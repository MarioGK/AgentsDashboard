using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record GetRuntimeCommandStatusRequest
{
    [Key(0)] public required string CommandId { get; init; }
}
