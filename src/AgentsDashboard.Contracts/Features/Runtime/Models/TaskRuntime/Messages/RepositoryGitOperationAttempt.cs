using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record RepositoryGitOperationAttempt
{
    [Key(0)] public required string Strategy { get; init; }
    [Key(1)] public required string GitUrl { get; init; }
    [Key(2)] public bool Success { get; init; }
    [Key(3)] public int ExitCode { get; init; }
    [Key(4)] public required string ErrorMessage { get; init; }
}
