using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]

public record HarnessToolStatus
{
    [Key(0)] public required string Command { get; init; }
    [Key(1)] public required string DisplayName { get; init; }
    [Key(2)] public required string Status { get; init; }
    [Key(3)] public string? Version { get; init; }
}
