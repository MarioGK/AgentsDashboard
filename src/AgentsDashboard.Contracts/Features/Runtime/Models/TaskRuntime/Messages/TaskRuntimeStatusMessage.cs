using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]

public sealed record TaskRuntimeStatusMessage
{
    [Key(0)]
    public required string TaskRuntimeId { get; init; }

    [Key(1)]
    public required string Status { get; init; }

    [Key(2)]
    public int ActiveSlots { get; init; }

    [Key(3)]
    public int MaxSlots { get; init; }

    [Key(4)]
    public long Timestamp { get; init; }

    [Key(5)]
    public string? Message { get; init; }
}
