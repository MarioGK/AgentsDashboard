using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public sealed record RuntimeFileSystemEntry
{
    [Key(0)] public required string Name { get; init; }
    [Key(1)] public required string RelativePath { get; init; }
    [Key(2)] public bool IsDirectory { get; init; }
    [Key(3)] public long Length { get; init; }
    [Key(4)] public DateTimeOffset LastModifiedUtc { get; init; }
}
