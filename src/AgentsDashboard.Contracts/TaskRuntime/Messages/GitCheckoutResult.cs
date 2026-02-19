using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record GitCheckoutResult
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public string CurrentReference { get; set; } = string.Empty;
}
