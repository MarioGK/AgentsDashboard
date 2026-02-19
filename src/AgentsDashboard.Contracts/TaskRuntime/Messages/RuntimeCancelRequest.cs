using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record RuntimeCancelRequest
{
    [Key(0)] public required string RunId { get; init; }
}
