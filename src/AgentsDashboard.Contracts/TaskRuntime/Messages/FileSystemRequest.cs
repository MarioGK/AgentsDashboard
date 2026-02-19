using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record FileSystemRequest
{
    [Key(0)] public required string Path { get; init; }
    [Key(1)] public bool IncludeHidden { get; init; }
    [Key(2)] public bool Recursive { get; init; }
}
