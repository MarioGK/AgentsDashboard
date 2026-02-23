namespace AgentsDashboard.Contracts.Features.Repositories.Models.Domain;

































































public sealed record RepositoryGitStatus(
    string CurrentBranch,
    string CurrentCommit,
    int AheadCount,
    int BehindCount,
    int ModifiedCount,
    int StagedCount,
    int UntrackedCount,
    DateTime ScannedAtUtc,
    DateTime? FetchedAtUtc,
    string LastSyncError = "");
