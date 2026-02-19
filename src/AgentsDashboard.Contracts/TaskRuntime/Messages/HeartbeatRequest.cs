using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]

[MessagePackObject]

// Request/Reply for DispatchJob
[MessagePackObject]

[MessagePackObject]

// Request/Reply for CancelJob
[MessagePackObject]

[MessagePackObject]

// Request/Reply for KillContainer
[MessagePackObject]

[MessagePackObject]

// Request/Reply for Heartbeat
[MessagePackObject]

[MessagePackObject]

// Request/Reply for ReconcileOrphanedContainers
[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]
public record HeartbeatRequest
{
    [Key(0)] public required string TaskRuntimeId { get; init; }
    [Key(1)] public required string HostName { get; init; }
    [Key(2)] public int ActiveSlots { get; init; }
    [Key(3)] public int MaxSlots { get; init; }
    [Key(4)] public DateTimeOffset Timestamp { get; init; }
}
