namespace AgentsDashboard.ControlPlane.Infrastructure.Data;






public sealed record TaskCleanupCandidate(
    string TaskId,
    string RepositoryId,
    DateTime CreatedAtUtc,
    DateTime LastActivityUtc,
    bool HasActiveRuns,
    int RunCount,
    DateTime? OldestRunUtc,
    bool IsRetentionEligible = false,
    bool IsDisabledInactiveEligible = false,
    bool HasOpenFindings = false);
