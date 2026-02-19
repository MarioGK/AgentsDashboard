using System.Collections.Generic;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]
public sealed record StartRuntimeCommandRequest
{
    [Key(0)] public required string RunId { get; init; }
    [Key(1)] public required string TaskId { get; init; }
    [Key(2)] public required string ExecutionToken { get; init; }
    [Key(3)] public required string Command { get; init; }
    [Key(4)] public List<string>? Arguments { get; init; }
    [Key(5)] public string? WorkingDirectory { get; init; }
    [Key(6)] public Dictionary<string, string>? EnvironmentVars { get; init; }
    [Key(7)] public int TimeoutSeconds { get; init; } = 600;
    [Key(8)] public int MaxOutputBytes { get; init; } = 2_097_152;
}
