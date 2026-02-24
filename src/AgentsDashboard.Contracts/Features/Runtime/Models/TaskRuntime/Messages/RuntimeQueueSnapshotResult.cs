using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record RuntimeQueueSnapshotResult
{
    [Key(0)]
    public bool Success { get; init; }

    [Key(1)]
    public string? ErrorMessage { get; init; }

    [Key(2)]
    public string TaskRuntimeId { get; set; } = string.Empty;

    [Key(3)]
    public List<string> ActiveRunIds { get; set; } = [];

    [Key(4)]
    public List<string> QueuedRunIds { get; set; } = [];

    [Key(5)]
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}
