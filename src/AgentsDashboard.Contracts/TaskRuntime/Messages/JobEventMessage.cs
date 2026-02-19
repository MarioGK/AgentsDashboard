using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

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

    [Key(6)]
    public long Sequence { get; init; }

    [Key(7)]
    public string Category { get; set; } = string.Empty;

    [Key(8)]
    public string? PayloadJson { get; init; }

    [Key(9)]
    public string SchemaVersion { get; set; } = string.Empty;
}
