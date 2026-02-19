using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record GitDiffResult
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public bool HasChanges { get; init; }
    [Key(3)] public string DiffText { get; set; } = string.Empty;
}
