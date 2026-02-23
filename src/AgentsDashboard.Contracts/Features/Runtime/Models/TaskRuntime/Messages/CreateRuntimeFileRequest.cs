using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record CreateRuntimeFileRequest
{
    [Key(0)] public required string RepositoryId { get; init; }
    [Key(1)] public required string TaskId { get; init; }
    [Key(2)] public string? RunId { get; init; }
    [Key(3)] public required string RelativePath { get; init; }
    [Key(4)] public byte[]? Content { get; init; }
    [Key(5)] public bool Overwrite { get; init; }
    [Key(6)] public bool? CreateParentDirectories { get; init; }
}
