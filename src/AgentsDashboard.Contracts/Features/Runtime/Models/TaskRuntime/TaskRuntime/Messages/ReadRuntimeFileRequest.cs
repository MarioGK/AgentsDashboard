using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public sealed record ReadRuntimeFileRequest
{
    [Key(0)] public required string RepositoryId { get; init; }
    [Key(1)] public required string TaskId { get; init; }
    [Key(2)] public string? RunId { get; init; }
    [Key(3)] public required string RelativePath { get; init; }
    [Key(4)] public int MaxBytes { get; init; }
}
