using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record FileReadRequest
{
    [Key(0)] public required string Path { get; init; }
    [Key(1)] public string Encoding { get; set; } = "utf-8";
}
