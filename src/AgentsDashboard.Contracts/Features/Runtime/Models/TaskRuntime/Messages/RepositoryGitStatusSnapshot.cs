using MessagePack;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

[MessagePackObject]
public sealed record RepositoryGitStatusSnapshot
{
    [Key(0)] public required string CurrentBranch { get; init; }
    [Key(1)] public required string CurrentCommit { get; init; }
    [Key(2)] public int AheadCount { get; init; }
    [Key(3)] public int BehindCount { get; init; }
    [Key(4)] public int ModifiedCount { get; init; }
    [Key(5)] public int StagedCount { get; init; }
    [Key(6)] public int UntrackedCount { get; init; }
    [Key(7)] public DateTime ScannedAtUtc { get; init; }
    [Key(8)] public DateTime? FetchedAtUtc { get; init; }
    [Key(9)] public required string LastSyncError { get; init; }
}
