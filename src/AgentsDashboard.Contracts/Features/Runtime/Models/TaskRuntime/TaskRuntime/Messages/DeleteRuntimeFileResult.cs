using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public sealed record DeleteRuntimeFileResult
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public bool Deleted { get; init; }
    [Key(2)] public bool WasDirectory { get; init; }
    [Key(3)] public string? Reason { get; init; }
    [Key(4)] public required string RelativePath { get; init; }
}
