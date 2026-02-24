using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record RepositoryWorkspaceResult
{
    [Key(0)] public bool Success { get; init; }
    [Key(1)] public string? ErrorMessage { get; init; }
    [Key(2)] public required string EffectiveGitUrl { get; init; }
    [Key(3)] public required string WorkspacePath { get; init; }
    [Key(4)] public required RepositoryGitStatusSnapshot GitStatus { get; init; }
    [Key(5)] public required List<RepositoryGitOperationAttempt> Attempts { get; init; }
    [Key(6)] public DateTimeOffset CompletedAt { get; init; }
}
