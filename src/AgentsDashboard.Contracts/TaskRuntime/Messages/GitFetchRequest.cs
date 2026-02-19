using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record GitFetchRequest
{
    [Key(0)] public required string RepositoryPath { get; init; }
    [Key(1)] public string Remote { get; set; } = "origin";
    [Key(2)] public bool Prune { get; init; }
}
