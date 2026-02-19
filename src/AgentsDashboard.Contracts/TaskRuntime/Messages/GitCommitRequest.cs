using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record GitCommitRequest
{
    [Key(0)] public required string RepositoryPath { get; init; }
    [Key(1)] public required string Message { get; init; }
    [Key(2)] public string? AuthorName { get; init; }
    [Key(3)] public string? AuthorEmail { get; init; }
    [Key(4)] public bool Amend { get; init; }
    [Key(5)] public bool AllowEmpty { get; init; }
}
