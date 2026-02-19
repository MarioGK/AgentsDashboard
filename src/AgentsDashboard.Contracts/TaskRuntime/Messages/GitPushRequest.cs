using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record GitPushRequest
{
    [Key(0)] public required string RepositoryPath { get; init; }
    [Key(1)] public string Remote { get; set; } = "origin";
    [Key(2)] public string? Branch { get; init; }
    [Key(3)] public bool SetUpstream { get; init; }
}
