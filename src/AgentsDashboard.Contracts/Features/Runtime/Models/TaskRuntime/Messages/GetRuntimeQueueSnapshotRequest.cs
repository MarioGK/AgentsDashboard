using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record GetRuntimeQueueSnapshotRequest
{
    [Key(0)]
    public bool IncludeQueuedRuns { get; set; } = true;
}
