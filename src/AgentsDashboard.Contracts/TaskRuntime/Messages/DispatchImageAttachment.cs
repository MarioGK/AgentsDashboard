using System.Collections.Generic;
using AgentsDashboard.Contracts.Domain;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]

[MessagePackObject]

// Request/Reply for DispatchJob
[MessagePackObject]

[MessagePackObject]

// Request/Reply for CancelJob
[MessagePackObject]

[MessagePackObject]

// Request/Reply for KillContainer
[MessagePackObject]

[MessagePackObject]

// Request/Reply for Heartbeat
[MessagePackObject]

[MessagePackObject]

// Request/Reply for ReconcileOrphanedContainers
[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]

[MessagePackObject]
public record DispatchImageAttachment
{
    [Key(0)] public required string Id { get; init; }
    [Key(1)] public required string FileName { get; init; }
    [Key(2)] public required string MimeType { get; init; }
    [Key(3)] public long SizeBytes { get; init; }
    [Key(4)] public required string StoragePath { get; init; }
    [Key(5)] public required string Sha256 { get; init; }
    [Key(6)] public int? Width { get; init; }
    [Key(7)] public int? Height { get; init; }
}
