using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public sealed record ReadRuntimeFileResult
{
    [Key(0)] public bool Found { get; init; }
    [Key(1)] public bool IsDirectory { get; init; }
    [Key(2)] public bool Truncated { get; init; }
    [Key(3)] public long ContentLength { get; init; }
    [Key(4)] public byte[]? Content { get; init; }
    [Key(5)] public string? ContentType { get; init; }
    [Key(6)] public string? Reason { get; init; }
    [Key(7)] public required string RelativePath { get; init; }
}
