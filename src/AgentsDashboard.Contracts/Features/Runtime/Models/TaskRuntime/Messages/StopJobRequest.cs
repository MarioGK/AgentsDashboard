using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public record StopJobRequest
{
    [Key(0)] public required string RunId { get; init; }
}
