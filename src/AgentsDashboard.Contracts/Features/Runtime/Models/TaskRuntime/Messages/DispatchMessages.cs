using System.Collections.Generic;

using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

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
