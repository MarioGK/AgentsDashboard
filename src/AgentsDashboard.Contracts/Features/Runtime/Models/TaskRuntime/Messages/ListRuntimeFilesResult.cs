using System.Collections.Generic;
using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record ListRuntimeFilesResult
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public bool Found { get; init; }
    [Key(2)] public bool IsDirectory { get; init; }
    [Key(3)] public string? Reason { get; init; }
    [Key(4)] public required string ResolvedRelativePath { get; init; }
    [Key(5)] public required List<RuntimeFileSystemEntry> Entries { get; init; }
}
