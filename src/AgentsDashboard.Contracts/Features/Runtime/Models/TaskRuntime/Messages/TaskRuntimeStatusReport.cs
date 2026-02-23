using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]

public sealed record TaskRuntimeStatusReport
{
    [Key(0)]
    public required string TaskRuntimeId { get; init; }

    [Key(1)]
    public int ActiveSlots { get; init; }

    [Key(2)]
    public int MaxSlots { get; init; }

    [Key(3)]
    public double CpuUsagePercent { get; init; }

    [Key(4)]
    public long MemoryUsedBytes { get; init; }

    [Key(5)]
    public Dictionary<string, int> QueuedJobsByPriority { get; set; } = new();

    [Key(6)]
    public long Timestamp { get; init; }
}
