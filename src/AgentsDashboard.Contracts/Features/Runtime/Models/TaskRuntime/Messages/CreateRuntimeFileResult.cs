using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record CreateRuntimeFileResult
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public bool Created { get; init; }
    [Key(2)] public string? Reason { get; init; }
    [Key(3)] public required string RelativePath { get; init; }
    [Key(4)] public long ContentLength { get; init; }
}
