using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]

public record GetHarnessToolsReply
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public List<HarnessToolStatus> Tools { get; set; } = [];
    [Key(3)] public DateTimeOffset CheckedAt { get; init; }
}
