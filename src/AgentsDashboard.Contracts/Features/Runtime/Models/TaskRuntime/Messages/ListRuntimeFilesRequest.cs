using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public sealed record ListRuntimeFilesRequest
{
    [Key(0)] public required string RepositoryId { get; init; }
    [Key(1)] public required string TaskId { get; init; }
    [Key(2)] public string? RunId { get; init; }
    [Key(3)] public string? RelativePath { get; init; }
    [Key(4)] public bool IncludeHidden { get; init; }
}
