using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]
public sealed record StartRuntimeCommandResult
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public string CommandId { get; init; } = string.Empty;
    [Key(3)] public DateTimeOffset AcceptedAt { get; init; }
}
