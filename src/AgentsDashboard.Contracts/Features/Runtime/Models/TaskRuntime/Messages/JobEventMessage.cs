using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]

public sealed record JobEventMessage
{
    [Key(0)]
    public required string RunId { get; init; }

    [Key(1)]
    public required string TaskId { get; init; }

    [Key(2)]
    public required string ExecutionToken { get; init; }

    [Key(3)]
    public required string EventType { get; init; }

    [Key(4)]
    public string? Summary { get; init; }

    [Key(5)]
    public string? Error { get; init; }

    [Key(6)]
    public long Timestamp { get; init; }

    [Key(7)]
    public Dictionary<string, string>? Metadata { get; init; }

    [Key(8)]
    public long Sequence { get; init; }

    [Key(9)]
    public string Category { get; set; } = string.Empty;

    [Key(10)]
    public string? PayloadJson { get; init; }

    [Key(11)]
    public string SchemaVersion { get; set; } = string.Empty;

    [Key(12)]
    public string? CommandId { get; init; }

    [Key(13)]
    public string? ArtifactId { get; init; }

    [Key(14)]
    public int? ChunkIndex { get; init; }

    [Key(15)]
    public bool? IsLastChunk { get; init; }

    [Key(16)]
    public byte[]? BinaryPayload { get; init; }

    [Key(17)]
    public string? ContentType { get; init; }
}
