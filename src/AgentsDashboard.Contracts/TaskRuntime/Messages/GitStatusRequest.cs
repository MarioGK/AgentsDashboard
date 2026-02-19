using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record GitStatusRequest
{
    [Key(0)] public required string RepositoryPath { get; init; }
    [Key(1)] public bool IncludeUntracked { get; set; } = true;
}
