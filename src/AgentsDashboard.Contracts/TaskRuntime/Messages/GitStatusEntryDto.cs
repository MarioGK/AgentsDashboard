using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record GitStatusEntryDto
{
    [Key(0)] public required string Path { get; init; }
    [Key(1)] public string IndexStatus { get; set; } = string.Empty;
    [Key(2)] public string WorkTreeStatus { get; set; } = string.Empty;
}
