using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record GitCheckoutRequest
{
    [Key(0)] public required string RepositoryPath { get; init; }
    [Key(1)] public required string Reference { get; init; }
    [Key(2)] public bool CreateBranch { get; init; }
    [Key(3)] public bool Force { get; init; }
}
