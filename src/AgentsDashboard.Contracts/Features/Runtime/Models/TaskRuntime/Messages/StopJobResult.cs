using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public record StopJobResult
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
}
