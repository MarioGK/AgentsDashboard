using System.Collections.Generic;

using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]

public record GetHarnessToolsRequest
{
    [Key(0)] public required string RequestId { get; init; }
}
