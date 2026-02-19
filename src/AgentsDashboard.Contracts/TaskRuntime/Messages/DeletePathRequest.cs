using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record DeletePathRequest
{
    [Key(0)] public required string Path { get; init; }
    [Key(1)] public bool Recursive { get; init; }
}
