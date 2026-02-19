using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]

public record GetHarnessToolsRequest
{
    [Key(0)] public required string RequestId { get; init; }
}
