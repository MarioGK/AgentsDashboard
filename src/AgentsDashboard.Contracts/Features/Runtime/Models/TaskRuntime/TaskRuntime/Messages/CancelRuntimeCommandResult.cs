using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]
public sealed record CancelRuntimeCommandResult
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public DateTimeOffset CanceledAt { get; init; }
}
