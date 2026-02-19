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
public record DispatchInputPart
{
    [Key(0)] public required string Type { get; init; }
    [Key(1)] public string Text { get; set; } = string.Empty;
    [Key(2)] public string ImageRef { get; set; } = string.Empty;
    [Key(3)] public string MimeType { get; set; } = string.Empty;
    [Key(4)] public int? Width { get; init; }
    [Key(5)] public int? Height { get; init; }
    [Key(6)] public long SizeBytes { get; init; }
    [Key(7)] public string Alt { get; set; } = string.Empty;
}
