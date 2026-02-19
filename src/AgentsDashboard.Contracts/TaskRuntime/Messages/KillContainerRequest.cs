using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]

public record KillContainerRequest
{
    [Key(0)] public required string ContainerId { get; init; }
}
