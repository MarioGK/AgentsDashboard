using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]

public record CancelJobRequest
{
    [Key(0)] public required string RunId { get; init; }
}
