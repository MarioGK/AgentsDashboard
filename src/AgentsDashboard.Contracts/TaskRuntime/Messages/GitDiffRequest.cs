using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record GitDiffRequest
{
    [Key(0)] public required string RepositoryPath { get; init; }
    [Key(1)] public string? BaseRef { get; init; }
    [Key(2)] public string? TargetRef { get; init; }
    [Key(3)] public bool Staged { get; init; }
    [Key(4)] public string? Pathspec { get; init; }
}
