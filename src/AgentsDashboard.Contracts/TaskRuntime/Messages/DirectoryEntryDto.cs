using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record DirectoryEntryDto
{
    [Key(0)] public required string Name { get; init; }
    [Key(1)] public required string Path { get; init; }
    [Key(2)] public bool IsDirectory { get; init; }
    [Key(3)] public long SizeBytes { get; init; }
    [Key(4)] public DateTimeOffset LastModifiedAt { get; init; }
}
