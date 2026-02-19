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
public record ReconcileOrphanedContainersReply
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public int ReconciledCount { get; init; }
    [Key(3)] public List<string>? ContainerIds { get; init; }
}
