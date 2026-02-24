using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record GetRunExecutionSnapshotRequest
{
    [Key(0)]
    public string RunId { get; set; } = string.Empty;
}
