using MessagePack;

namespace AgentsDashboard.Contracts.Worker;

[MessagePackObject]
public sealed record StatusRequestMessage
{
    [Key(0)]
    public required string RequestId { get; init; }

    [Key(1)]
    public long Timestamp { get; init; }
}

[MessagePackObject]
public sealed record ConfigurationChangedMessage
{
    [Key(0)]
    public required string ConfigurationKey { get; init; }

    [Key(1)]
    public string? NewValue { get; init; }

    [Key(2)]
    public long Timestamp { get; init; }
}

[MessagePackObject]
public sealed record ShutdownRequestMessage
{
    [Key(0)]
    public required string Reason { get; init; }

    [Key(1)]
    public long GracePeriodSeconds { get; init; } = 30;

    [Key(2)]
    public long Timestamp { get; init; }
}

[MessagePackObject]
public sealed record WorkerRegistrationRequest
{
    [Key(0)]
    public required string WorkerId { get; init; }

    [Key(1)]
    public string? Endpoint { get; init; }

    [Key(2)]
    public int MaxSlots { get; init; }

    [Key(3)]
    public string? Version { get; init; }

    [Key(4)]
    public Dictionary<string, string>? Capabilities { get; init; }
}

[MessagePackObject]
public sealed record WorkerRegistrationResult
{
    [Key(0)]
    public required bool Success { get; init; }

    [Key(1)]
    public string? AssignedId { get; init; }

    [Key(2)]
    public string? ErrorMessage { get; init; }

    [Key(3)]
    public long ServerTimestamp { get; init; }
}

[MessagePackObject]
public sealed record WorkerStatusReport
{
    [Key(0)]
    public required string WorkerId { get; init; }

    [Key(1)]
    public int ActiveSlots { get; init; }

    [Key(2)]
    public int MaxSlots { get; init; }

    [Key(3)]
    public double CpuUsagePercent { get; init; }

    [Key(4)]
    public long MemoryUsedBytes { get; init; }

    [Key(5)]
    public Dictionary<string, int> QueuedJobsByPriority { get; init; } = new();

    [Key(6)]
    public long Timestamp { get; init; }
}

[MessagePackObject]
public sealed record JobEventMessage
{
    [Key(0)]
    public required string RunId { get; init; }

    [Key(1)]
    public required string EventType { get; init; }

    [Key(2)]
    public string? Summary { get; init; }

    [Key(3)]
    public string? Error { get; init; }

    [Key(4)]
    public long Timestamp { get; init; }

    [Key(5)]
    public Dictionary<string, string>? Metadata { get; init; }
}

[MessagePackObject]
public sealed record WorkerStatusMessage
{
    [Key(0)]
    public required string WorkerId { get; init; }

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
