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
public record KillContainerRequest
{
    [Key(0)] public required string ContainerId { get; init; }
}
