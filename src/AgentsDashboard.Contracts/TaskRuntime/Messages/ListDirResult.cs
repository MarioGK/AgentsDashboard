using System.Collections.Generic;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record ListDirResult
{
    [Key(0)] public required string Path { get; init; }
    [Key(1)] public List<DirectoryEntryDto> Entries { get; set; } = [];
}
