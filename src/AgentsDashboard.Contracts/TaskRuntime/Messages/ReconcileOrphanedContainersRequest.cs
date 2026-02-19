using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]

public record ReconcileOrphanedContainersRequest
{
    [Key(0)] public required string TaskRuntimeId { get; init; }
}
