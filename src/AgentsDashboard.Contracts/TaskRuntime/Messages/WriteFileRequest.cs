using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

[MessagePackObject]
public record WriteFileRequest
{
    [Key(0)] public required string Path { get; init; }
    [Key(1)] public required string Content { get; init; }
    [Key(2)] public string Encoding { get; set; } = "utf-8";
    [Key(3)] public bool CreateDirectories { get; set; } = true;
}
