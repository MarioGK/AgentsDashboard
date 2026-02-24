using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record RunExecutionSnapshotResult
{
    [Key(0)]
    public bool Success { get; init; }

    [Key(1)]
    public string? ErrorMessage { get; init; }

    [Key(2)]
    public bool Found { get; init; }

    [Key(3)]
    public string RunId { get; set; } = string.Empty;

    [Key(4)]
    public string TaskId { get; set; } = string.Empty;

    [Key(5)]
    public TaskRuntimeExecutionState State { get; set; } = TaskRuntimeExecutionState.Unknown;

    [Key(6)]
    public string Summary { get; set; } = string.Empty;

    [Key(7)]
    public string PayloadJson { get; set; } = string.Empty;

    [Key(8)]
    public DateTimeOffset? StartedAt { get; init; }

    [Key(9)]
    public DateTimeOffset? EndedAt { get; init; }

    [Key(10)]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
