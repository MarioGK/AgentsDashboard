using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record FileContentDto
{
    [Key(0)] public required string Path { get; init; }
    [Key(1)] public string Encoding { get; set; } = "utf-8";
    [Key(2)] public string Content { get; set; } = string.Empty;
    [Key(3)] public long SizeBytes { get; init; }
}
