using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record RuntimeCancelResult
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
}
