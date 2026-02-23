using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record StopJobRequest
{
    [Key(0)] public required string RunId { get; init; }
}
