using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;
[MessagePackObject]
public sealed record RuntimeCommandStatusResult
{
    [Key(0)] public bool Found { get; init; }
    [Key(1)] public string? CommandId { get; init; }
    [Key(2)] public string? RunId { get; init; }
    [Key(3)] public string? TaskId { get; init; }
    [Key(4)] public string? ExecutionToken { get; init; }
    [Key(5)] public RuntimeCommandStatusValue Status { get; init; }
    [Key(6)] public int? ExitCode { get; init; }
    [Key(7)] public DateTimeOffset StartedAt { get; init; }
    [Key(8)] public DateTimeOffset? CompletedAt { get; init; }
    [Key(9)] public string? ErrorMessage { get; init; }
    [Key(10)] public bool TimedOut { get; init; }
    [Key(11)] public bool Canceled { get; init; }
    [Key(12)] public bool OutputTruncated { get; init; }
    [Key(13)] public long StandardOutputBytes { get; init; }
    [Key(14)] public long StandardErrorBytes { get; init; }
}
