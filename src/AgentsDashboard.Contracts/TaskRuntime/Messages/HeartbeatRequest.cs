using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]

public record HeartbeatRequest
{
    [Key(0)] public required string TaskRuntimeId { get; init; }
    [Key(1)] public required string HostName { get; init; }
    [Key(2)] public int ActiveSlots { get; init; }
    [Key(3)] public int MaxSlots { get; init; }
    [Key(4)] public DateTimeOffset Timestamp { get; init; }
}
